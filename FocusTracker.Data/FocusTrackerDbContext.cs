using Microsoft.EntityFrameworkCore;
using FocusTracker.Domain.Models;

namespace FocusTracker.Data
{
    public class FocusTrackerDbContext : DbContext
    {
        public FocusTrackerDbContext(DbContextOptions<FocusTrackerDbContext> options)
            : base(options)
        {
        }

        public DbSet<SkillCategory> SkillCategories { get; set; }
        public DbSet<Skill> Skills { get; set; }
        public DbSet<TaskItem> TaskItems { get; set; }
        public DbSet<ExperienceHistory> ExperienceHistories { get; set; }
        public DbSet<TrackedProgram> TrackedPrograms { get; set; }
        public DbSet<TaskProgram> TaskPrograms { get; set; }
        public DbSet<TaskProgramUsage> TaskProgramUsages { get; set; }
        public DbSet<Restriction> Restrictions { get; set; }
        public DbSet<RestrictionRule> RestrictionRules { get; set; }
        public DbSet<AppUsageStat> AppUsageStats { get; set; }
        public DbSet<HourlyAppUsageLog> HourlyAppUsageLogs { get; set; }
        public DbSet<RemoteHourlyUsage> RemoteHourlyUsages { get; set; }
        public DbSet<RemoteDevice> RemoteDevices { get; set; }
        public DbSet<SyncState> SyncStates { get; set; }
        public DbSet<SyncTombstone> SyncTombstones { get; set; }
        public DbSet<ActivityDay> ActivityDays { get; set; }
        public DbSet<RemoteActivityDay> RemoteActivityDays { get; set; }

        // Увімкнено, коли застосовуються зміни з інших пристроїв: тоді їх не позначаємо як власні
        public bool SuppressSyncTracking { get; set; }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            var touched = TrackSyncChanges();
            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            if (touched.Tasks.Count > 0)
                TaskItems.Where(t => touched.Tasks.Contains(t.Id))
                    .ExecuteUpdate(s => s.SetProperty(t => t.SyncUpdatedAt, touched.At));
            if (touched.Restrictions.Count > 0)
                Restrictions.Where(r => touched.Restrictions.Contains(r.Id))
                    .ExecuteUpdate(s => s.SetProperty(r => r.SyncUpdatedAt, touched.At));
            return result;
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            var touched = TrackSyncChanges();
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (touched.Tasks.Count > 0)
                await TaskItems.Where(t => touched.Tasks.Contains(t.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.SyncUpdatedAt, touched.At), cancellationToken);
            if (touched.Restrictions.Count > 0)
                await Restrictions.Where(r => touched.Restrictions.Contains(r.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.SyncUpdatedAt, touched.At), cancellationToken);
            return result;
        }

        // Ставить SyncId і час зміни, записує сліди видалень. Зміни зв'язків (програми задачі,
        // правила й цілі обмеження) позначають змінами батьківської сутності — повертаємо їхні Id.
        private (DateTime At, List<int> Tasks, List<int> Restrictions) TrackSyncChanges()
        {
            var now = DateTime.UtcNow;
            if (SuppressSyncTracking)
                return (now, new List<int>(), new List<int>());

            foreach (var entry in ChangeTracker.Entries<ISyncEntity>().ToList())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                    case EntityState.Modified:
                        if (entry.Entity.SyncId == Guid.Empty)
                            entry.Entity.SyncId = Guid.NewGuid();
                        entry.Entity.SyncUpdatedAt = now;
                        break;
                    case EntityState.Deleted when entry.Entity.SyncId != Guid.Empty:
                        SyncTombstones.Add(new SyncTombstone
                        {
                            Kind = SyncKinds.Of(entry.Entity),
                            SyncId = entry.Entity.SyncId,
                            DeletedAt = now
                        });
                        break;
                }
            }

            var changedStates = new[] { EntityState.Added, EntityState.Modified, EntityState.Deleted };
            var tasks = ChangeTracker.Entries<TaskProgram>()
                .Where(e => changedStates.Contains(e.State))
                .Select(e => e.Entity.TaskId)
                .Distinct()
                .ToList();
            var restrictions = ChangeTracker.Entries<RestrictionRule>()
                .Where(e => changedStates.Contains(e.State))
                .Select(e => e.Entity.RestrictionId)
                .Concat(ChangeTracker.Entries<RestrictionToProgram>()
                    .Where(e => changedStates.Contains(e.State))
                    .Select(e => e.Entity.RestrictionId))
                .Where(id => id != 0)
                .Distinct()
                .ToList();

            return (now, tasks, restrictions);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // === Хмарна синхронізація
            modelBuilder.Entity<HourlyAppUsageLog>()
                .HasIndex(h => h.UpdatedAt);

            modelBuilder.Entity<RemoteHourlyUsage>()
                .HasIndex(r => new { r.DeviceId, r.AppName, r.Date, r.Hour })
                .IsUnique();

            modelBuilder.Entity<RemoteHourlyUsage>()
                .HasIndex(r => r.Date);

            modelBuilder.Entity<RemoteDevice>()
                .HasKey(d => d.DeviceId);

            modelBuilder.Entity<SyncState>()
                .HasKey(s => s.Key);

            modelBuilder.Entity<ActivityDay>()
                .HasKey(a => a.Date);

            modelBuilder.Entity<RemoteActivityDay>()
                .HasIndex(a => new { a.DeviceId, a.Date })
                .IsUnique();

            // === TaskProgram (связь задача <-> программа)
            modelBuilder.Entity<TaskProgram>()
                .HasKey(tp => new { tp.TaskId, tp.ProgramId });

            modelBuilder.Entity<TaskProgram>()
                .HasOne(tp => tp.Task)
                .WithMany()
                .HasForeignKey(tp => tp.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TaskProgram>()
                .HasOne(tp => tp.Program)
                .WithMany()
                .HasForeignKey(tp => tp.ProgramId)
                .OnDelete(DeleteBehavior.Cascade);

            // === Skill → TaskItem: каскадное удаление задач при удалении навыка
            modelBuilder.Entity<TaskItem>()
                .HasOne(t => t.Skill)
                .WithMany(s => s.Tasks)
                .HasForeignKey(t => t.SkillId)
                .OnDelete(DeleteBehavior.Cascade);

            // === TaskItem → TaskProgramUsage: каскадное удаление использования при удалении задачи
            modelBuilder.Entity<TaskProgramUsage>()
                .HasOne(u => u.Task)
                .WithMany()
                .HasForeignKey(u => u.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            // === TaskItem → TaskProgram (вторая связь уже задана выше — оставим её)

            modelBuilder.Entity<ExperienceHistory>()
                .HasOne(e => e.Task)
                .WithMany()
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Skill>()
                .HasOne(s => s.Category)
                .WithMany()
                .HasForeignKey(s => s.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // === RestrictionToProgram: связь Ограничение <-> Программа

            modelBuilder.Entity<RestrictionToProgram>()
                .HasKey(rp => new { rp.RestrictionId, rp.ProgramId });

            modelBuilder.Entity<RestrictionToProgram>()
                .HasOne(rp => rp.Restriction)
                .WithMany(r => r.Targets)
                .HasForeignKey(rp => rp.RestrictionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RestrictionToProgram>()
                .HasOne(rp => rp.Program)
                .WithMany()
                .HasForeignKey(rp => rp.ProgramId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
