using System;

namespace FocusTracker.Domain.Interfaces
{
    public interface IActiveWindowProvider
    {
        event Action<string, TimeSpan> OnAppChanged;
        void Start();
        void Stop();
    }
}
