using FocusTracker.App.ViewModels;
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace FocusTracker.App.Views
{
    public partial class TasksPage : UserControl
    {
        private TasksPageViewModel ViewModel;

        public TasksPage()
        {
            InitializeComponent();

            // Отримуємо сервіси з DI
            var taskService = App.Services.GetRequiredService<ITaskItemService>();
            var skillService = App.Services.GetRequiredService<ISkillService>();
            var programService = App.Services.GetRequiredService<ITrackedProgramService>();
            var taskProgramService = App.Services.GetRequiredService<ITaskProgramService>();
            var usageStatService = App.Services.GetRequiredService<IAppUsageStatService>();
            var taskProgramUsageService = App.Services.GetRequiredService<ITaskProgramUsageService>();

            // Передаємо сервіси до ViewModel
            ViewModel = new TasksPageViewModel(
                taskService,
                skillService,
                programService,
                taskProgramService,
                usageStatService,
                taskProgramUsageService
            );

            DataContext = ViewModel;
        }

        private void ShowAddForm_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.StartNewTask();
            AddForm.Visibility = Visibility.Visible;
        }

        private void CancelAddForm_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.CancelTaskEdit();
            AddForm.Visibility = Visibility.Collapsed;
        }

        private void AddTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsEditing)
                ViewModel.SaveTaskChanges();
            else
                ViewModel.AddTask();

            AddForm.Visibility = Visibility.Collapsed;
        }

        private void EditTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TaskItem task)
            {
                ViewModel.EditTask(task);
                AddForm.Visibility = Visibility.Visible;
            }
        }

        private void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TaskItem task)
            {
                ViewModel.DeleteTask(task);
            }
        }

        private async void CompleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TaskItem task)
            {
                int xpEarned = await ViewModel.CompleteTask(task);
                ShowXpPopup(xpEarned); // 🔥 Показываем всплывашку
            }
        }

        private void ProgramsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TrackedProgram selected)
            {
                if (!ViewModel.SelectedPrograms.Contains(selected))
                    ViewModel.SelectedPrograms.Add(selected);
            }

            ((ComboBox)sender).SelectedItem = null;
        }

        private void RemoveProgram_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TrackedProgram program)
            {
                if (ViewModel.SelectedPrograms.Contains(program))
                    ViewModel.SelectedPrograms.Remove(program);
            }
        }

        private void ShowXpPopup(int xp)
        {
            PopupText.Text = $"+{xp} XP";
            XpPopup.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(30));
            var moveUp = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(1000));
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1000))
            {
                BeginTime = TimeSpan.FromSeconds(0.1)
            };

            fadeOut.Completed += (s, e) =>
            {
                XpPopup.Visibility = Visibility.Collapsed;
            };

            XpPopup.BeginAnimation(OpacityProperty, fadeIn);
            PopupTransform.BeginAnimation(TranslateTransform.YProperty, moveUp);
            XpPopup.BeginAnimation(OpacityProperty, fadeOut);
        }
        private void ToggleHistory_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ShowHistory = !ViewModel.ShowHistory;
        }
    }
}
