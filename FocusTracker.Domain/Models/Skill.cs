using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace FocusTracker.Domain.Models
{
    public class Skill : INotifyPropertyChanged
    {
        public int Id { get; set; }

        private string _name;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public int CategoryId { get; set; }
        public int Level { get; set; }
        public int Xp { get; set; }

        [NotMapped]
        public int XpToNextLevel => (Level + 1) * 100;

        [NotMapped]
        public double XpProgressPercent => (double)Xp / XpToNextLevel * 100;


        public SkillCategory Category { get; set; }
        public ICollection<TaskItem> Tasks { get; set; }

        [NotMapped]
        private bool _isEditing;
        [NotMapped]
        public bool IsEditing
        {
            get => _isEditing;
            set { _isEditing = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
