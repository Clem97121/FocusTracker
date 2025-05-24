using System;

namespace FocusTracker.Domain.Interfaces
{
    public interface IAppUsageTracker
    {
        void Start();
        void Stop();
    }
}
