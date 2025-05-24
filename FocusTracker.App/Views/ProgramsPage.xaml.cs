using FocusTracker.App.ViewModels;
using FocusTracker.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace FocusTracker.App.Views
{
    public partial class ProgramsPage : UserControl
    {
        private readonly ProgramsViewModel _viewModel;

        public ProgramsPage()
        {
            InitializeComponent();

            _viewModel = new ProgramsViewModel(
                App.Services.GetRequiredService<FocusTracker.Domain.Interfaces.IProgramService>()
            );

            DataContext = _viewModel;
        }

        private async void DataGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.Row.Item is TrackedProgram program)
            {
                await _viewModel.UpdateProgramAsync(program);
            }
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProgramsViewModel vm)
                await vm.LoadProgramsAsync();
        }

        private const double AnimationDuration = 0.1;

        private void ToggleHiddenPrograms_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProgramsViewModel vm)
            {
                bool expanding = !vm.ShowHidden;
                vm.ShowHidden = expanding;

                var animation = new DoubleAnimation
                {
                    To = expanding ? 1 : 0,
                    Duration = TimeSpan.FromSeconds(AnimationDuration),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };

                HiddenProgramsScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            }
        }
    }
}
