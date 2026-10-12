using FocusTracker.Domain.Models;

namespace FocusTracker.Data
{
    // Назви видів сутностей у хмарній таблиці ft_entities. Порядок — порядок застосування:
    // батьківські сутності раніше за дочірні.
    public static class SyncKinds
    {
        public const string Category = "category";
        public const string Skill = "skill";
        public const string Task = "task";
        public const string Restriction = "restriction";
        public const string Xp = "xp";

        public static readonly string[] ApplyOrder = { Category, Skill, Task, Restriction, Xp };

        public static string Of(ISyncEntity entity) => entity switch
        {
            Domain.Models.SkillCategory => Category,
            Domain.Models.Skill => Skill,
            TaskItem => Task,
            Domain.Models.Restriction => Restriction,
            ExperienceHistory => Xp,
            _ => throw new ArgumentOutOfRangeException(nameof(entity), entity.GetType().Name, "Невідомий вид сутності")
        };
    }
}
