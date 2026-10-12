using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FocusTracker.Data.Services
{
    public class TrackedProgramService : ITrackedProgramService
    {
        private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;

        public TrackedProgramService(IDbContextFactory<FocusTrackerDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public List<TrackedProgram> GetAll()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.TrackedPrograms.AsNoTracking().ToList();
        }

        public void Add(TrackedProgram program)
        {
            using var db = _dbFactory.CreateDbContext();
            db.TrackedPrograms.Add(program);
            db.SaveChanges();
        }

        public void Delete(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var prog = db.TrackedPrograms.Find(id);
            if (prog != null)
            {
                db.TrackedPrograms.Remove(prog);
                db.SaveChanges();
            }
        }

        // старый метод, можно удалить позже
        public void UpdateDisplayNameIfNeeded(string identifier, string displayName)
        {
            using var db = _dbFactory.CreateDbContext();
            var program = db.TrackedPrograms.FirstOrDefault(p => p.Identifier == identifier);
            if (program != null && program.DisplayName != displayName && !string.IsNullOrWhiteSpace(displayName))
            {
                program.DisplayName = displayName;
                db.SaveChanges();
            }
        }

        // ✅ Новый метод — загрузка display name и иконки, если нужно
        public async Task UpdateDisplayInfoIfNeededAsync(TrackedProgram program)
        {
            if (program == null || string.IsNullOrWhiteSpace(program.Identifier))
                return;

            if (!string.IsNullOrWhiteSpace(program.DisplayName) && program.IconBytes != null)
                return;

            var exePath = FindExecutablePath(program.Identifier);
            if (exePath == null || !File.Exists(exePath))
                return;

            // Обновить DisplayName
            if (string.IsNullOrWhiteSpace(program.DisplayName))
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                program.DisplayName = versionInfo.FileDescription ?? Path.GetFileNameWithoutExtension(exePath);
            }

            // Обновить IconBytes
            if (program.IconBytes == null)
            {
                try
                {
                    using var icon = Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null)
                    {
                        using var ms = new MemoryStream();
                        icon.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        program.IconBytes = ms.ToArray(); // сработает RaisePropertyChanged
                    }
                }
                catch
                {
                    // можно добавить лог
                }
            }

            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Entry(program).State = EntityState.Modified;
            await db.SaveChangesAsync();
        }

        // 🔍 Метод для нахождения EXE — можешь заменить на свой
        private string? FindExecutablePath(string identifier)
        {
            try
            {
                var process = Process.GetProcessesByName(identifier).FirstOrDefault();
                return process?.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }
    }
}
