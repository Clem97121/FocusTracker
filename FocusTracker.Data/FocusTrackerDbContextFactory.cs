using FocusTracker.Data;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;

public class FocusTrackerDbContextFactory : IDesignTimeDbContextFactory<FocusTrackerDbContext>
{
    public FocusTrackerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FocusTrackerDbContext>();

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FocusTracker",
            "focus_tracker.db");

        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new FocusTrackerDbContext(optionsBuilder.Options);
    }
}
