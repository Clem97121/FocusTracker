using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FocusTracker.App.Views;
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;


namespace FocusTracker.App
{
    public partial class MainWindow : Window
    {
       public MainWindow()
        {
            InitializeComponent();

            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(hwnd).AddHook(WindowProc);
            };


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

        private const int WM_NCHITTEST = 0x0084;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int HTCLIENT = 1;

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                Point mousePos = GetMousePosition(lParam);
                double edge = 8; // ширина зоны для resize

                if (mousePos.Y <= edge)
                {
                    if (mousePos.X <= edge)
                    {
                        handled = true;
                        return (IntPtr)HTTOPLEFT;
                    }
                    else if (mousePos.X >= ActualWidth - edge)
                    {
                        handled = true;
                        return (IntPtr)HTTOPRIGHT;
                    }
                    else
                    {
                        handled = true;
                        return (IntPtr)HTTOP;
                    }
                }
                else if (mousePos.Y >= ActualHeight - edge)
                {
                    if (mousePos.X <= edge)
                    {
                        handled = true;
                        return (IntPtr)HTBOTTOMLEFT;
                    }
                    else if (mousePos.X >= ActualWidth - edge)
                    {
                        handled = true;
                        return (IntPtr)HTBOTTOMRIGHT;
                    }
                    else
                    {
                        handled = true;
                        return (IntPtr)HTBOTTOM;
                    }
                }
                else if (mousePos.X <= edge)
                {
                    handled = true;
                    return (IntPtr)HTLEFT;
                }
                else if (mousePos.X >= ActualWidth - edge)
                {
                    handled = true;
                    return (IntPtr)HTRIGHT;
                }
            }

            return IntPtr.Zero;
        }

        private Point GetMousePosition(IntPtr lParam)
        {
            int x = (short)((uint)lParam & 0xFFFF);
            int y = (short)(((uint)lParam >> 16) & 0xFFFF);
            return PointFromScreen(new Point(x, y));
        }

    }
}
