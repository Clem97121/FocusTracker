using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FocusTracker.App.Views;

namespace FocusTracker.App
{
    public partial class MainWindow : Window
    {
       public MainWindow()
{
    InitializeComponent();

    // Устанавливаем контент и заголовок
    MainContent.Content = new StatsPage();
    SetPageTitle("Статистика");

    // Подсвечиваем кнопку статистики
    HighlightNavButton(StatsButton);

    SizeChanged += (s, e) =>
    {
        MainBorder.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, ActualWidth, ActualHeight),
            RadiusX = 20,
            RadiusY = 20
        };
    };
}


        private Button _currentNavButton;

        private void HighlightNavButton(Button button)
        {
            if (_currentNavButton != null)
            {
                _currentNavButton.Tag = null; // сбросим активность
            }

            button.Tag = "active"; // активная
            _currentNavButton = button;
        }


        private void SetPageTitle(string title)
        {
            PageTitleTextBlock.Text = title;
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void ToggleMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // Прячем окно вместо выхода, если не запрошено завершение явно
            if (!(Application.Current as App)?.IsExitRequested ?? false)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void ShowStatsPage(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new StatsPage();
            SetPageTitle("Статистика");
            HighlightNavButton((Button)sender);
        }

        private void ShowSkillsPage(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new SkillsPage();
            SetPageTitle("Навички");
            HighlightNavButton((Button)sender);
        }

        private void ShowTasksPage(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new TasksPage();
            SetPageTitle("Завдання");
            HighlightNavButton((Button)sender);
        }

        private void ShowRestrictionsPage(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new RestrictionsPage();
            SetPageTitle("Обмеження");
            HighlightNavButton((Button)sender);
        }

        private void ShowProgramsPage(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new ProgramsPage();
            SetPageTitle("Застосунки");
            HighlightNavButton((Button)sender);
        }
    }
}
