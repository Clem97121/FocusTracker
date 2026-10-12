namespace FocusTracker.Domain
{
    public enum StatsScopeKind
    {
        AllDevices,
        ThisDevice,
        RemoteDevice
    }

    // Чию статистику показують екрани: усіх пристроїв, лише цього чи одного з інших
    public class StatsScope
    {
        public StatsScopeKind Kind { get; private set; } = StatsScopeKind.AllDevices;
        public Guid RemoteDeviceId { get; private set; }

        public bool IncludesLocal => Kind != StatsScopeKind.RemoteDevice;

        public void SetAllDevices() => (Kind, RemoteDeviceId) = (StatsScopeKind.AllDevices, Guid.Empty);
        public void SetThisDevice() => (Kind, RemoteDeviceId) = (StatsScopeKind.ThisDevice, Guid.Empty);
        public void SetRemoteDevice(Guid deviceId) => (Kind, RemoteDeviceId) = (StatsScopeKind.RemoteDevice, deviceId);
    }
}
