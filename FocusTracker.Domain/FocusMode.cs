using System;
using System.Collections.Generic;
using System.Linq;

namespace FocusTracker.Domain
{
    // Фокус-режим: до вказаного часу перелічені програми закриваються (з попередженням за хвилину)
    public class FocusMode
    {
        private readonly object _lock = new();
        private HashSet<string> _blocked = new(StringComparer.OrdinalIgnoreCase);

        public DateTime? Until { get; private set; }
        public string Reason { get; private set; } = string.Empty;

        public bool IsActive => Until is { } until && DateTime.Now < until;

        public IReadOnlyCollection<string> BlockedApps
        {
            get { lock (_lock) return _blocked.ToList(); }
        }

        public event Action? Changed;

        public void Start(TimeSpan duration, IEnumerable<string> apps, string reason)
        {
            lock (_lock)
                _blocked = new HashSet<string>(apps.Where(a => !string.IsNullOrWhiteSpace(a)), StringComparer.OrdinalIgnoreCase);
            Until = DateTime.Now + duration;
            Reason = reason;
            Changed?.Invoke();
        }

        public void Stop()
        {
            Until = null;
            lock (_lock) _blocked.Clear();
            Changed?.Invoke();
        }

        public bool Blocks(string appName)
        {
            if (!IsActive) return false;
            lock (_lock) return _blocked.Contains(appName);
        }
    }
}
