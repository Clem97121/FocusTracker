using FocusTracker.App.Jarvis;
using FocusTracker.Domain;
using System.Windows;

namespace FocusTracker.App.Views
{
    // Повідомлення асистента в правому нижньому куті, поверх усіх вікон.
    // Не забирає фокус, але й не зникає саме — щоб його не можна було просто не помітити.
    public partial class JarvisPopup : Window
    {
        private static JarvisPopup? _current;

        private JarvisPopup(JarvisNote note)
        {
            InitializeComponent();
            Title = AssistantInfo.Name;
            HeaderText.Text = note.Kind == "focus" ? $"{AssistantInfo.Name} · {note.At:HH:mm} · фокус-режим" : $"{AssistantInfo.Name} · {note.At:HH:mm}";
            MessageText.Text = note.Text;
            Loaded += (_, _) =>
            {
                var area = SystemParameters.WorkArea;
                Left = area.Right - ActualWidth - 16;
                Top = area.Bottom - ActualHeight - 16;
            };
        }

        // Нове повідомлення замінює попереднє, а не складається стосом
        public static void Show(JarvisNote note)
        {
            _current?.Close();
            _current = new JarvisPopup(note);
            _current.Show();
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => Close();

        private void Reply_Click(object sender, RoutedEventArgs e)
        {
            Close();
            if (Application.Current.MainWindow is MainWindow main)
                main.ShowJarvis();
        }
    }
}
