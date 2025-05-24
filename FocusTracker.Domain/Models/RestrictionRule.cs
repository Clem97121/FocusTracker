// 📄 RestrictionRule.cs
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FocusTracker.Domain.Models
{
    public class RestrictionRule
    {
        public int Id { get; set; }
        public int RestrictionId { get; set; }
        public string RuleType { get; set; }     // max_total_seconds, time_interval, etc.
        public string Value { get; set; }
        public bool IsMandatory { get; set; }

        public Restriction Restriction { get; set; }

        // ✅ Не сохраняется в базу, используется для UI
        [NotMapped]
        public string DisplayName { get; set; }
        public override string ToString() => DisplayName; // ← это тоже поможет!
        [NotMapped]
        public string Description { get; set; }
    }
}
