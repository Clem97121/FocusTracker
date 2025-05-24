using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FocusTracker.Data
{
    public class FocusTrackerDbContextFactory : IDesignTimeDbContextFactory<FocusTrackerDbContext>
    {
        public FocusTrackerDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<FocusTrackerDbContext>();
            optionsBuilder.UseSqlite("Data Source=focus_tracker.db");

            return new FocusTrackerDbContext(optionsBuilder.Options);
        }
    }
}
