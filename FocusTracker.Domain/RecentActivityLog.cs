using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FocusTracker.Domain
{
    // Що було у фокусі останні години: програма й заголовок вікна по секундах.
    // Лише в пам'яті — заголовки не пишуться в базу. У хмару вони йдуть, лише коли
    // увімкнено «мозок асистента у хмарі»; там зберігаються 3 години.
    public class RecentActivityLog
    {
        public static readonly TimeSpan Keep = TimeSpan.FromHours(2);
        private const int MaxTitleLength = 120;

        private readonly object _lock = new();
        private readonly LinkedList<(DateTime At, string App, string Title, TimeSpan Duration)> _items = new();

        public void Add(string app, string title, TimeSpan duration)
        {
            if (string.IsNullOrWhiteSpace(app)) return;
            title = title.Length > MaxTitleLength ? title[..MaxTitleLength] + "…" : title;

            var now = DateTime.Now;
            lock (_lock)
            {
                // Сусідні секунди з тим самим вікном зливаємо в один запис
                if (_items.Last is { } last && last.Value.App == app && last.Value.Title == title
                    && now - last.Value.At - last.Value.Duration < TimeSpan.FromSeconds(10))
                {
                    last.Value = last.Value with { Duration = last.Value.Duration + duration };
                }
                else
                {
                    _items.AddLast((now - duration, app, title, duration));
                }

                while (_items.First is { } first && now - first.Value.At > Keep)
                    _items.RemoveFirst();
            }
        }

        // Записи, які тривали після since: нові або подовжені — для відправки в хмару
        public List<(DateTime At, string App, string Title, TimeSpan Duration)> ChangedSince(DateTime since)
        {
            lock (_lock)
                return _items.Where(i => i.At + i.Duration > since).ToList();
        }

        // Хронологія за останні minutes хвилин: «14:05–14:32 (27 хв) chrome — Djinni — вакансії DevOps»
        public string Describe(TimeSpan period)
        {
            var since = DateTime.Now - period;
            List<(DateTime At, string App, string Title, TimeSpan Duration)> items;
            lock (_lock)
                items = _items.Where(i => i.At + i.Duration >= since).ToList();

            if (items.Count == 0)
                return "(немає даних: користувача не було за цим пристроєм)";

            var text = new StringBuilder();
            foreach (var item in items.Where(i => i.Duration >= TimeSpan.FromSeconds(30)))
            {
                text.AppendLine($"{item.At:HH:mm}–{item.At + item.Duration:HH:mm} ({FormatDuration(item.Duration)}) {item.App} — {item.Title}");
            }

            var shortOnes = items.Where(i => i.Duration < TimeSpan.FromSeconds(30)).ToList();
            if (shortOnes.Count > 0)
                text.AppendLine($"…і ще {shortOnes.Count} коротких перемикань (разом {FormatDuration(TimeSpan.FromSeconds(shortOnes.Sum(i => i.Duration.TotalSeconds)))})");
            return text.ToString();
        }

        private static string FormatDuration(TimeSpan d) =>
            d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes} хв" : $"{(int)d.TotalSeconds} с";
    }
}
