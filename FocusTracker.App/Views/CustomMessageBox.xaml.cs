using System.Windows;
using System.Windows.Input;
using System.Media;

namespace FocusTracker.App.Views
{
    public partial class CustomMessageBox : Window
    {
        public CustomMessageBox(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
            SystemSounds.Asterisk.Play(); // Можно заменить на Beep, Exclamation, Hand, Question
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
        public static void Show(string message)
        {
            var box = new CustomMessageBox(message);
            box.ShowDialog();
        }

    }
}
