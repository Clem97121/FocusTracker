using System.Text.Json;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data.Cloud
{
    // Синхронізація категорій навичок, навичок, задач, обмежень і історії XP.
    // Кожна сутність — один JSON-документ у ft_entities; перемагає новіша зміна.
    public partial class CloudSyncService
    {
        private const string EntitiesUploadWatermarkKey = "cloud.entities_upload_watermark";
        private const string EntitiesDownloadWatermarkKey = "cloud.entities_download_watermark";

        private static readonly JsonSerializerOptions DocJson = new(JsonSerializerDefaults.Web);

        private record CategoryDoc(string Name, string Color, int SortOrder);
        private record SkillDoc(string Name, Guid CategoryId, int Level, int Xp);
        private record TaskDoc(string Title, string? Description, Guid SkillId, int EstimatedMinutes, int Difficulty,
            DateTime DateCreated, bool Completed, int? EarnedXp, int? ActiveSeconds, bool IsPassive, List<string> Programs);
        private record RuleDoc(string RuleType, string Value);
        private record RestrictionDoc(string? Note, List<RuleDoc> Rules, List<string> Programs);
        private record XpDoc(Guid TaskId, int XpEarned, string RecordedAt);

        private async Task<int> UploadEntitiesAsync(CloudStore cloud, Guid deviceId, CancellationToken token)
        {
            var startedAt = DateTime.UtcNow;
            var watermark = ParseDate(await GetStateAsync(EntitiesUploadWatermarkKey));
            var entities = new List<CloudEntity>();

            await using (var db = await _dbFactory.CreateDbContextAsync(token))
            {
                await AssignSyncIdsAsync(db, firstSync: watermark == null, startedAt, token);

                bool Changed(ISyncEntity e) => watermark == null || e.SyncUpdatedAt > watermark;
                CloudEntity Doc(string kind, ISyncEntity e, object doc) => new(kind, e.SyncId,
                    JsonSerializer.Serialize(doc, DocJson), false, e.SyncUpdatedAt ?? startedAt, deviceId);

                foreach (var c in (await db.SkillCategories.AsNoTracking().ToListAsync(token)).Where(Changed))
                    entities.Add(Doc(SyncKinds.Category, c, new CategoryDoc(c.Name, c.Color, c.SortOrder)));

                foreach (var s in (await db.Skills.Include(s => s.Category).AsNoTracking().ToListAsync(token)).Where(Changed))
                    entities.Add(Doc(SyncKinds.Skill, s, new SkillDoc(s.Name, s.Category.SyncId, s.Level, s.Xp)));

                var tasks = (await db.TaskItems.Include(t => t.Skill).AsNoTracking().ToListAsync(token)).Where(Changed).ToList();
                var taskIds = tasks.Select(t => t.Id).ToList();
                var taskPrograms = (await db.TaskPrograms.Include(tp => tp.Program)
                        .Where(tp => taskIds.Contains(tp.TaskId)).AsNoTracking().ToListAsync(token))
                    .ToLookup(tp => tp.TaskId, tp => tp.Program.Identifier);
                foreach (var t in tasks)
                {
                    entities.Add(Doc(SyncKinds.Task, t, new TaskDoc(t.Title, t.Description, t.Skill.SyncId,
                        t.EstimatedMinutes, t.Difficulty, t.DateCreated, t.Completed, t.EarnedXp, t.ActiveSeconds,
                        t.IsPassive, taskPrograms[t.Id].Distinct().ToList())));
                }

                var restrictions = await db.Restrictions
                    .Include(r => r.Rules)
                    .Include(r => r.Targets).ThenInclude(t => t.Program)
                    .AsNoTracking().ToListAsync(token);
                foreach (var r in restrictions.Where(Changed))
                {
                    entities.Add(Doc(SyncKinds.Restriction, r, new RestrictionDoc(r.Note,
                        (r.Rules ?? new List<RestrictionRule>()).Select(rule => new RuleDoc(rule.RuleType, rule.Value)).ToList(),
                        (r.Targets ?? new List<RestrictionToProgram>()).Select(t => t.Program.Identifier).Distinct().ToList())));
                }

                foreach (var x in (await db.ExperienceHistories.Include(x => x.Task).AsNoTracking().ToListAsync(token)).Where(Changed))
                {
                    if (x.Task == null) continue;
                    entities.Add(Doc(SyncKinds.Xp, x, new XpDoc(x.Task.SyncId, x.XpEarned, x.RecordedAt)));
                }

                var tombstones = await db.SyncTombstones.AsNoTracking()
                    .Where(t => watermark == null || t.DeletedAt > watermark.Value)
                    .ToListAsync(token);
                entities.AddRange(tombstones.Select(t => new CloudEntity(t.Kind, t.SyncId, null, true, t.DeletedAt, deviceId)));
            }

            // Батьківські сутності першими, щоб на інших пристроях вони з'являлись раніше за дочірні
            var ordered = entities.OrderBy(e => Array.IndexOf(SyncKinds.ApplyOrder, e.Kind)).ToList();
            foreach (var batch in ordered.Chunk(UploadBatchSize))
                await cloud.UploadEntitiesAsync(deviceId, batch, token);

            await SetStateAsync(EntitiesUploadWatermarkKey, FormatDate(startedAt));
            return entities.Count;
        }

        // Сутностям, створеним до синхронізації, видаємо SyncId; уперше — ще й час зміни
        private static async Task AssignSyncIdsAsync(FocusTrackerDbContext db, bool firstSync, DateTime now, CancellationToken token)
        {
            db.SuppressSyncTracking = true;

            void Assign(IEnumerable<ISyncEntity> items)
            {
                foreach (var item in items)
                {
                    if (item.SyncId == Guid.Empty)
                        item.SyncId = Guid.NewGuid();
                    if (firstSync && item.SyncUpdatedAt == null)
                        item.SyncUpdatedAt = now;
                }
            }

            Assign(await db.SkillCategories.Where(e => e.SyncId == Guid.Empty || (firstSync && e.SyncUpdatedAt == null)).ToListAsync(token));
            Assign(await db.Skills.Where(e => e.SyncId == Guid.Empty || (firstSync && e.SyncUpdatedAt == null)).ToListAsync(token));
            Assign(await db.TaskItems.Where(e => e.SyncId == Guid.Empty || (firstSync && e.SyncUpdatedAt == null)).ToListAsync(token));
            Assign(await db.Restrictions.Where(e => e.SyncId == Guid.Empty || (firstSync && e.SyncUpdatedAt == null)).ToListAsync(token));
            Assign(await db.ExperienceHistories.Where(e => e.SyncId == Guid.Empty || (firstSync && e.SyncUpdatedAt == null)).ToListAsync(token));
            await db.SaveChangesAsync(token);

            db.SuppressSyncTracking = false;
        }

        private async Task<int> DownloadEntitiesAsync(CloudStore cloud, Guid deviceId, CancellationToken token)
        {
            var watermark = ParseDate(await GetStateAsync(EntitiesDownloadWatermarkKey));
            var since = watermark.HasValue ? DateTime.SpecifyKind(watermark.Value, DateTimeKind.Utc) - DownloadOverlap : (DateTime?)null;

            var rows = await cloud.DownloadEntitiesAsync(deviceId, since, token);
            if (rows.Count == 0) return 0;

            await ApplyRemoteEntitiesAsync(rows.Select(r => r.Entity).ToList(), token);
            await SetStateAsync(EntitiesDownloadWatermarkKey, FormatDate(rows.Max(r => r.SyncedAtUtc)));
            return rows.Count;
        }

        // Застосовує зміни з інших пристроїв, якщо вони новіші за локальні.
        // Повертає кількість сутностей, які не вдалося застосувати (немає батьківської сутності).
        public async Task<int> ApplyRemoteEntitiesAsync(IReadOnlyCollection<CloudEntity> entities, CancellationToken token = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(token);
            db.SuppressSyncTracking = true;

            var categories = (await db.SkillCategories.ToListAsync(token)).Where(e => e.SyncId != Guid.Empty).ToDictionary(e => e.SyncId);
            var skills = (await db.Skills.ToListAsync(token)).Where(e => e.SyncId != Guid.Empty).ToDictionary(e => e.SyncId);
            var tasks = (await db.TaskItems.ToListAsync(token)).Where(e => e.SyncId != Guid.Empty).ToDictionary(e => e.SyncId);
            var restrictions = (await db.Restrictions.Include(r => r.Rules).Include(r => r.Targets).ToListAsync(token))
                .Where(e => e.SyncId != Guid.Empty).ToDictionary(e => e.SyncId);
            var xps = (await db.ExperienceHistories.ToListAsync(token)).Where(e => e.SyncId != Guid.Empty).ToDictionary(e => e.SyncId);
            var programs = (await db.TrackedPrograms.ToListAsync(token))
                .GroupBy(p => p.Identifier).ToDictionary(g => g.Key, g => g.First());

            TrackedProgram Program(string identifier)
            {
                if (!programs.TryGetValue(identifier, out var program))
                {
                    program = new TrackedProgram { Name = identifier, Identifier = identifier, Category = "Суміжні", IsTracked = true };
                    db.TrackedPrograms.Add(program);
                    programs[identifier] = program;
                }
                return program;
            }

            // Застосовувати чи ні: локальна версія відсутня або старіша
            bool IsNewer<T>(Dictionary<Guid, T> map, CloudEntity e, out T? local) where T : class, ISyncEntity =>
                !map.TryGetValue(e.SyncId, out local) || local.SyncUpdatedAt == null || local.SyncUpdatedAt < e.UpdatedAtUtc;

            T Upsert<T>(Dictionary<Guid, T> map, T? local, CloudEntity e, Func<T> create) where T : class, ISyncEntity
            {
                if (local == null)
                {
                    local = create();
                    local.SyncId = e.SyncId;
                    db.Add(local);
                    map[e.SyncId] = local;
                }
                local.SyncUpdatedAt = e.UpdatedAtUtc;
                return local;
            }

            void Delete<T>(Dictionary<Guid, T> map, T? local) where T : class, ISyncEntity
            {
                if (local == null) return;
                db.Remove(local);
                map.Remove(local.SyncId);
            }

            var skipped = 0;
            var ordered = entities
                .OrderBy(e => Array.IndexOf(SyncKinds.ApplyOrder, e.Kind))
                .ThenBy(e => e.UpdatedAtUtc);

            foreach (var e in ordered)
            {
                switch (e.Kind)
                {
                    case SyncKinds.Category:
                    {
                        if (!IsNewer(categories, e, out var local)) break;
                        if (e.Deleted) { Delete(categories, local); break; }
                        var doc = JsonSerializer.Deserialize<CategoryDoc>(e.Data!, DocJson)!;
                        local = Upsert(categories, local, e, () => new SkillCategory());
                        local.Name = doc.Name;
                        local.Color = doc.Color;
                        local.SortOrder = doc.SortOrder;
                        break;
                    }
                    case SyncKinds.Skill:
                    {
                        if (!IsNewer(skills, e, out var local)) break;
                        if (e.Deleted) { Delete(skills, local); break; }
                        var doc = JsonSerializer.Deserialize<SkillDoc>(e.Data!, DocJson)!;
                        if (!categories.TryGetValue(doc.CategoryId, out var category)) { skipped++; break; }
                        local = Upsert(skills, local, e, () => new Skill());
                        local.Name = doc.Name;
                        local.Category = category;
                        local.Level = doc.Level;
                        local.Xp = doc.Xp;
                        break;
                    }
                    case SyncKinds.Task:
                    {
                        if (!IsNewer(tasks, e, out var local)) break;
                        if (e.Deleted) { Delete(tasks, local); break; }
                        var doc = JsonSerializer.Deserialize<TaskDoc>(e.Data!, DocJson)!;
                        if (!skills.TryGetValue(doc.SkillId, out var skill)) { skipped++; break; }
                        local = Upsert(tasks, local, e, () => new TaskItem());
                        local.Title = doc.Title;
                        local.Description = doc.Description ?? string.Empty;
                        local.Skill = skill;
                        local.EstimatedMinutes = doc.EstimatedMinutes;
                        local.Difficulty = doc.Difficulty;
                        local.DateCreated = doc.DateCreated;
                        local.Completed = doc.Completed;
                        local.EarnedXp = doc.EarnedXp;
                        local.ActiveSeconds = doc.ActiveSeconds;
                        local.IsPassive = doc.IsPassive;

                        if (local.Id != 0)
                            db.TaskPrograms.RemoveRange(await db.TaskPrograms.Where(tp => tp.TaskId == local.Id).ToListAsync(token));
                        foreach (var identifier in doc.Programs.Distinct())
                            db.TaskPrograms.Add(new TaskProgram { Task = local, Program = Program(identifier) });
                        break;
                    }
                    case SyncKinds.Restriction:
                    {
                        if (!IsNewer(restrictions, e, out var local)) break;
                        if (e.Deleted) { Delete(restrictions, local); break; }
                        var doc = JsonSerializer.Deserialize<RestrictionDoc>(e.Data!, DocJson)!;
                        local = Upsert(restrictions, local, e, () => new Restriction());
                        local.Note = doc.Note ?? string.Empty;

                        if (local.Rules != null) db.RestrictionRules.RemoveRange(local.Rules);
                        local.Rules = doc.Rules.Select(r => new RestrictionRule { RuleType = r.RuleType, Value = r.Value }).ToList();
                        if (local.Targets != null) db.RemoveRange(local.Targets);
                        local.Targets = doc.Programs.Distinct()
                            .Select(identifier => new RestrictionToProgram { Program = Program(identifier) })
                            .ToList();
                        break;
                    }
                    case SyncKinds.Xp:
                    {
                        if (!IsNewer(xps, e, out var local)) break;
                        if (e.Deleted) { Delete(xps, local); break; }
                        var doc = JsonSerializer.Deserialize<XpDoc>(e.Data!, DocJson)!;
                        if (!tasks.TryGetValue(doc.TaskId, out var task)) { skipped++; break; }
                        local = Upsert(xps, local, e, () => new ExperienceHistory());
                        local.Task = task;
                        local.XpEarned = doc.XpEarned;
                        local.RecordedAt = doc.RecordedAt;
                        break;
                    }
                }
            }

            await db.SaveChangesAsync(token);
            return skipped;
        }
    }
}
