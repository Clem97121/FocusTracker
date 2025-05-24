using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace FocusTracker.Domain.Models
{
    public class SkillCategory : INotifyPropertyChanged
    {
        public int Id { get; set; }

        private string _name;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Color { get; set; }
        public int SortOrder { get; set; }

        private bool _isEditing;

        [NotMapped]
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged();
                }
            }
        }
        private bool _isAddingSkill;
        [NotMapped]
        public bool IsAddingSkill
        {
            get => _isAddingSkill;
            set { _isAddingSkill = value; OnPropertyChanged(); }
        }
        private bool _isExpanded;
        [NotMapped]
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }


        [NotMapped] public string NewSkillName { get; set; } = "";

        [NotMapped] public ObservableCollection<Skill> Skills { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
