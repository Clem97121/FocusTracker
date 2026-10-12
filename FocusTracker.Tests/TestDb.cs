using FocusTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Tests;

// Тимчасова SQLite-БД у файлі з усіма міграціями
public sealed class TestDb : IDbContextFactory<FocusTrackerDbContext>, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"focustracker-test-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<FocusTrackerDbContext> _options;

    public TestDb()
    {
        _options = new DbContextOptionsBuilder<FocusTrackerDbContext>()
            .UseSqlite($"Data Source={_path};Pooling=False")
            .Options;

        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    public FocusTrackerDbContext CreateDbContext() => new(_options);

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }
}
