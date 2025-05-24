using FocusTracker.App.Helpers;
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using FocusTracker.App.Helpers; // для RelayCommand
using System.Windows.Input;
using FocusTracker.Data.Services;

namespace FocusTracker.App.ViewModels
{
    public class SkillCategoriesViewModel : INotifyPropertyChanged
    {
        private readonly ISkillCategoryService _categoryService;
        private readonly ISkillService _skillService;
        public ObservableCollection<SkillCategory> Categories { get; } = new();

        public bool IsAddingCategory
        {
            get => _isAddingCategory;
            set { _isAddingCategory = value; OnPropertyChanged(); }
        }
        private bool _isAddingCategory;

        public string NewCategoryName
        {
            get => _newCategoryName;
            set { _newCategoryName = value; OnPropertyChanged(); }
        }
        private string _newCategoryName = "";

        public SkillCategoriesViewModel(ISkillCategoryService categoryService, ISkillService skillService)
        {
            _categoryService = categoryService;
            _skillService = skillService;
            _ = LoadCategoriesAsync();
        }


        public async Task LoadCategoriesAsync()
        {
            try
            {
                var categories = await _categoryService.GetAllAsync();
                Categories.Clear();

                foreach (var category in categories)
                {
                    var skills = _skillService.GetByCategoryId(category.Id);
                    category.Skills = new ObservableCollection<Skill>(skills);

                    category.IsExpanded = true; // 🔥 Розгорнути всі категорії
                    Categories.Add(category);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Load error: {ex.Message}");
            }
        }



        public async Task AddNewCategoryAsync()
        {
            if (string.IsNullOrWhiteSpace(NewCategoryName)) return;

            _categoryService.Add(new SkillCategory
            {
                Name = NewCategoryName,
                SortOrder = 0,
                Color = "#FFFFFF"
            });

            NewCategoryName = "";
            IsAddingCategory = false;

            await LoadCategoriesAsync();
        }
        public ICommand AddSkillCommand => new RelayCommand(obj =>
        {
            if (obj is SkillCategory category)
            {
                category.IsAddingSkill = true;
            }
        });


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

}
