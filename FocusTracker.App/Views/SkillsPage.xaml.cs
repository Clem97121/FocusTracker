using FocusTracker.App.ViewModels;
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FocusTracker.App.Views
{
    public partial class SkillsPage : UserControl
    {
        private bool _cancelingAddCategory = false;
        private bool _suppressLostFocus = false;
        private bool _suppressSkillLostFocus = false;

        private readonly ISkillCategoryService _categoryService;
        private readonly ISkillService _skillService;
        private readonly SkillCategoriesViewModel _viewModel;

        public SkillsPage()
        {
            InitializeComponent();

            _categoryService = App.Services.GetRequiredService<ISkillCategoryService>();
            _skillService = App.Services.GetRequiredService<ISkillService>();
            _viewModel = new SkillCategoriesViewModel(_categoryService, _skillService);
            DataContext = _viewModel;
        }

        private SkillCategory? GetCategoryFromMenu(object sender)
        {
            return (sender as MenuItem)?.Parent is ContextMenu context &&
                   context.PlacementTarget is FrameworkElement fe &&
                   fe.DataContext is SkillCategory cat
                   ? cat
                   : null;
        }

        private void EditCategory_Click(object sender, RoutedEventArgs e)
        {
            var category = GetCategoryFromMenu(sender);
            if (category != null)
            {
                category.IsEditing = false; // Примусово скидаємо
                category.IsEditing = true;  // І знову вмикаємо
            }
        }

        private async void DeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            var category = GetCategoryFromMenu(sender);
            if (category != null)
            {
                var result = MessageBox.Show(
                    $"Видалити категорію: {category.Name}?\nУсі пов'язані навички також буде видалено.",
                    "Підтвердження",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result == MessageBoxResult.Yes)
                {
                    _categoryService.Delete(category.Id);
                    await _viewModel.LoadCategoriesAsync();
                }
            }
        }

        private void AddSkill_Click(object sender, RoutedEventArgs e)
        {
            var category = GetCategoryFromMenu(sender);
            if (category != null)
            {
                category.IsExpanded = true;
                category.IsAddingSkill = true;
            }
        }

        private async void AddCategory_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.IsAddingCategory)
                await _viewModel.AddNewCategoryAsync();
            else
                _viewModel.IsAddingCategory = true;
        }

        private void CancelNewCategory_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.IsAddingCategory = false;
            _viewModel.NewCategoryName = "";
            Dispatcher.InvokeAsync(() => _suppressLostFocus = false, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void CancelNewCategory_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _suppressLostFocus = true;
        }

        private void NewCategoryBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.IsVisible)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }

        private async void NewCategoryBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_suppressLostFocus)
                await _viewModel.AddNewCategoryAsync();
        }

        private async void NewCategoryBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await _viewModel.AddNewCategoryAsync();
        }

        private void NameBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.IsVisible)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }

        private void NameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Keyboard.ClearFocus();
        }

        private async void NameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is SkillCategory category)
            {
                category.IsEditing = false;
                _categoryService.Update(category);
                await _viewModel.LoadCategoriesAsync();
            }
        }

        private void CancelSkill_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _suppressSkillLostFocus = true;
        }

        private void CancelSkill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SkillCategory category)
            {
                category.NewSkillName = "";
                category.IsAddingSkill = false;
            }

            Dispatcher.InvokeAsync(() => _suppressSkillLostFocus = false, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void SkillBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.IsVisible)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }

        private void SkillBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Keyboard.ClearFocus();
        }

        private async void SkillBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_suppressSkillLostFocus) return;

            if (sender is TextBox tb && tb.DataContext is SkillCategory category)
            {
                var name = category.NewSkillName?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var newSkill = new Skill
                    {
                        Name = name,
                        CategoryId = category.Id,
                        Xp = 0,
                        Level = 1
                    };

                    _skillService.Add(newSkill);
                    category.NewSkillName = "";
                    category.IsAddingSkill = false;
                    category.Skills.Add(newSkill);
                }
            }
        }

        private void EditSkill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menu &&
                menu.Parent is ContextMenu context &&
                context.PlacementTarget is FrameworkElement fe &&
                fe.DataContext is Skill skill)
            {
                skill.IsEditing = true;
            }
        }

        private async void DeleteSkill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menu &&
                menu.Parent is ContextMenu context &&
                context.PlacementTarget is FrameworkElement fe &&
                fe.DataContext is Skill skill)
            {
                if (MessageBox.Show($"Видалити навичку: {skill.Name}?", "Підтвердження", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    _skillService.Delete(skill.Id);
                    await _viewModel.LoadCategoriesAsync();
                }
            }
        }

        private void SkillEditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Keyboard.ClearFocus();
        }

        private void SkillEditBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.IsVisible)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }

        private async void SkillEditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is Skill skill)
            {
                skill.IsEditing = false;
                _skillService.Update(skill);
                await _viewModel.LoadCategoriesAsync();
            }
        }
    }
}
