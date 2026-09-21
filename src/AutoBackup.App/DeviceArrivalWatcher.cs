using System.Runtime.InteropServices;

namespace AutoBackup.App;

/// <summary>
/// Raises an event when Windows reports a volume being mounted, so the app can react to the
/// backup drive being plugged in instead of polling for it.
/// </summary>
/// <remarks>
/// Polling was the wrong shape for this job: a short interval kept external drives from ever
/// idling down, and an interval long enough to let them sleep just made them spin down and get
/// woken again in a loop - and each spin-up spends one of the limited start/stop cycles a
/// mechanical drive is rated for. Listening costs the drive nothing and reacts instantly.
/// </remarks>
internal sealed class DeviceArrivalWatcher : NativeWindow, IDisposable
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDevTypVolume = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastHeader
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
    }

    public event Action? VolumeArrived;

    public DeviceArrivalWatcher()
    {
        // Volume arrival is broadcast to top-level windows, so this must be one - a
        // message-only window would never be sent the notification.
        CreateHandle(new CreateParams());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmDeviceChange && (int)m.WParam == DbtDeviceArrival && IsVolume(m.LParam))
        {
            VolumeArrived?.Invoke();
        }
        base.WndProc(ref m);
    }

    // The same arrival message fires for any device class; only volumes can be a backup target.
    private static bool IsVolume(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return false;
        var header = Marshal.PtrToStructure<DevBroadcastHeader>(lParam);
        return header.DeviceType == DbtDevTypVolume;
    }

    public void Dispose() => DestroyHandle();
}
