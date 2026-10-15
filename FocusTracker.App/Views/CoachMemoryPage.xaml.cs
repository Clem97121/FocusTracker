using FocusTracker.App.Jarvis;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FocusTracker.App.Views
{
    // Що Коуч пам'ятає про користувача: профіль за розділами й щоденник. Усе можна виправити чи стерти.
    public partial class CoachMemoryPage : UserControl
    {
        // Ключі — як у supabase/functions/assistant/memory.ts
        private static readonly (string Key, string Title)[] Sections =
        {
            ("about", "Хто ви: робота, навчання, обставини"),
            ("goals", "Що для вас важливо і чому"),
            ("patterns", "Звички й закономірності"),
            ("works", "Що допомагає"),
            ("fails", "Що не працює або дратує"),
            ("agreements", "Домовленості"),
            ("notes", "Інше"),
        };

        private const int JournalDays = 60;

        public class SectionItem
        {
            public required string Key { get; init; }
            public required string Title { get; init; }
            public required string Original { get; set; }
            public string Content { get; set; } = "";
        }

        public class JournalItem
        {
            public required DateOnly Date { get; init; }
            public required string Original { get; set; }
            public string Summary { get; set; } = "";
            public string Header => Date.ToString("dddd, d MMMM yyyy", CultureInfo.GetCultureInfo("uk-UA"));
        }

        private readonly CloudAssistant _cloud;
        private readonly ObservableCollection<SectionItem> _sections = new();
        private readonly ObservableCollection<JournalItem> _journal = new();

        public CoachMemoryPage()
        {
            InitializeComponent();
            _cloud = App.Services.GetRequiredService<CloudAssistant>();
            SectionItems.ItemsSource = _sections;
            JournalItems.ItemsSource = _journal;
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            SaveButton.IsEnabled = false;
            try
            {
                var store = _cloud.RequireStore();
                var profile = (await store.GetCoachProfileAsync()).ToDictionary(s => s.Key, s => s.Content);
                var journal = await store.GetCoachJournalAsync(JournalDays);

                _sections.Clear();
                foreach (var (key, title) in Sections)
                {
                    var content = profile.GetValueOrDefault(key, "");
                    _sections.Add(new SectionItem { Key = key, Title = title, Original = content, Content = content });
                }
                _journal.Clear();
                foreach (var entry in journal)
                    _journal.Add(new JournalItem { Date = entry.Date, Original = entry.Summary, Summary = entry.Summary });
                UpdateJournalEmpty();
                SaveButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Не вдалося завантажити пам'ять Коуча: {ex.Message}";
            }
        }

        private void UpdateJournalEmpty() =>
            JournalEmpty.Visibility = _journal.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveButton.IsEnabled = false;
            try
            {
                var store = _cloud.RequireStore();
                var changed = 0;
                foreach (var section in _sections.Where(s => s.Content.Trim() != s.Original.Trim()))
                {
                    await store.SaveCoachProfileSectionAsync(section.Key, section.Content.Trim());
                    section.Original = section.Content;
                    changed++;
                }
                foreach (var day in _journal.Where(d => d.Summary.Trim() != d.Original.Trim()))
                {
                    await store.SaveCoachJournalAsync(day.Date, day.Summary.Trim());
                    day.Original = day.Summary;
                    changed++;
                }
                StatusText.Text = changed == 0 ? "Змін немає." : $"Збережено ({changed}). Коуч уже знає вашу версію.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Не вдалося зберегти: {ex.Message}";
            }
            SaveButton.IsEnabled = true;
        }

        private async void DeleteDay_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not JournalItem day) return;
            try
            {
                await _cloud.RequireStore().DeleteCoachJournalAsync(day.Date);
                _journal.Remove(day);
                UpdateJournalEmpty();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Не вдалося видалити: {ex.Message}";
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Parent is ContentControl host)
                host.Content = new JarvisPage();
        }
    }
}
