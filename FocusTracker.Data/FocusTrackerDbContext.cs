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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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

        }
    }
}
