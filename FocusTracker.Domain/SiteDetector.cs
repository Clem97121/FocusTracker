using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FocusTracker.Domain
{
    // Сайти в браузері як окремі «програми»: за заголовком вікна браузера визначаємо відомий сайт
    // («Відео - YouTube - Google Chrome» → YouTube). Назва сайту — як у статистиці телефона,
    // тож обмеження й фокус-режим для «YouTube» діють і на телефоні, і на сайт у браузері.
    public static class SiteDetector
    {
        public static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "yandex", "browser", "arc", "zen",
        };

        // Як сайт підписує вкладку → назва в статистиці
        private static readonly (string Marker, string Site)[] Known =
        {
            ("YouTube", "YouTube"),
            ("Twitch", "Twitch"),
            ("Netflix", "Netflix"),
            ("Instagram", "Instagram"),
            ("Facebook", "Facebook"),
            ("TikTok", "TikTok"),
            ("Reddit", "Reddit"),
            ("X", "X"),
            ("Twitter", "X"),
            ("Discord", "Discord"),
            ("Telegram", "Telegram"),
            ("Telegram Web", "Telegram"),
            ("WhatsApp", "WhatsApp"),
            ("Pinterest", "Pinterest"),
            ("Steam", "Steam"),
            ("Kick", "Kick"),
            ("LinkedIn", "LinkedIn"),
            ("Djinni", "Djinni"),
            ("DOU", "DOU"),
            ("Work.ua", "Work.ua"),
            ("robota.ua", "robota.ua"),
            ("Indeed", "Indeed"),
            ("Glassdoor", "Glassdoor"),
            ("GitHub", "GitHub"),
            ("GitLab", "GitLab"),
            ("Stack Overflow", "Stack Overflow"),
            ("Habr", "Habr"),
            ("Хабр", "Habr"),
            ("Medium", "Medium"),
            ("Wikipedia", "Wikipedia"),
            ("Вікіпедія", "Wikipedia"),
            ("Википедия", "Wikipedia"),
            ("Gmail", "Gmail"),
            ("Google Docs", "Google Docs"),
            ("Google Документи", "Google Docs"),
            ("Google Sheets", "Google Sheets"),
            ("Google Таблиці", "Google Sheets"),
            ("Google Drive", "Google Drive"),
            ("Google Диск", "Google Drive"),
            ("Google Calendar", "Google Calendar"),
            ("Google Search", "Google"),
            ("Пошук Google", "Google"),
            ("Поиск в Google", "Google"),
            ("ChatGPT", "ChatGPT"),
            ("Claude", "Claude"),
            ("Gemini", "Gemini"),
            ("Notion", "Notion"),
            ("Coursera", "Coursera"),
            ("Udemy", "Udemy"),
            ("KodeKloud", "KodeKloud"),
            ("Microsoft Learn", "Microsoft Learn"),
            ("AWS", "AWS"),
            ("Azure", "Azure"),
            ("Docker Hub", "Docker Hub"),
            ("Kubernetes", "Kubernetes"),
            ("Jira", "Jira"),
            ("Confluence", "Confluence"),
            ("Supabase", "Supabase"),
        };

        private static readonly Regex Counter = new(@"^\(\d+\+?\)\s*|\s*\(\d+\+?\)$", RegexOptions.Compiled);

        private static readonly string[] Separators = { " - ", " – ", " — ", " | ", " · " };

        public static bool IsBrowser(string processName) => Browsers.Contains(processName);

        public static readonly HashSet<string> SiteNames = new(Known.Select(k => k.Site), StringComparer.OrdinalIgnoreCase);

        public static bool IsSite(string appName) => SiteNames.Contains(appName);

        // Назва сайту з заголовка вікна браузера або null, якщо сайт невідомий
        public static string? Detect(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;

            // Частини заголовка; остання зазвичай — назва браузера, передостання — сайт.
            // Лічильники сповіщень на кшталт «(3) YouTube» відкидаємо.
            var parts = title.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                var part = Counter.Replace(parts[i], "");
                foreach (var (marker, site) in Known)
                    if (string.Equals(part, marker, StringComparison.OrdinalIgnoreCase))
                        return site;
            }
            return null;
        }
    }
}
