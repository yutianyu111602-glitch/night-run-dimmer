using System.Runtime.InteropServices;

namespace NightRunDimmer;

internal sealed class MonitorBrightnessService
{
    private const uint MC_CAPS_BRIGHTNESS = 0x00000002;
    private readonly Dictionary<string, uint> originalBrightness = new(StringComparer.Ordinal);

    public IReadOnlyList<PhysicalMonitorInfo> Probe()
    {
        return WithPhysicalMonitors(monitors =>
            monitors
                .Select(monitor =>
                {
                    var supported = TryGetBrightness(monitor.Handle, out var min, out var current, out var max);
                    return new PhysicalMonitorInfo(monitor.Description, supported, min, current, max);
                })
                .ToList());
    }

    public HardwareApplyResult SetBrightnessPercent(int percent)
    {
        var target = (uint)Math.Clamp(percent, 0, 100);
        var attempted = 0;
        var changed = 0;
        var failed = 0;

        WithPhysicalMonitors(monitors =>
        {
            foreach (var monitor in monitors)
            {
                if (!TryGetBrightness(monitor.Handle, out var min, out var current, out var max))
                {
                    continue;
                }

                attempted += 1;
                originalBrightness.TryAdd(monitor.Description, current);

                var absolute = min + ((max - min) * target / 100);
                if (SetMonitorBrightness(monitor.Handle, absolute))
                {
                    changed += 1;
                }
                else
                {
                    failed += 1;
                }
            }
            return 0;
        });

        return new HardwareApplyResult(attempted, changed, failed);
    }

    public HardwareApplyResult Restore()
    {
        var attempted = 0;
        var changed = 0;
        var failed = 0;

        WithPhysicalMonitors(monitors =>
        {
            foreach (var monitor in monitors)
            {
                if (!originalBrightness.TryGetValue(monitor.Description, out var brightness))
                {
                    continue;
                }

                attempted += 1;
                if (SetMonitorBrightness(monitor.Handle, brightness))
                {
                    changed += 1;
                }
                else
                {
                    failed += 1;
                }
            }
            return 0;
        });

        originalBrightness.Clear();
        return new HardwareApplyResult(attempted, changed, failed);
    }

    private static bool TryGetBrightness(nint handle, out uint min, out uint current, out uint max)
    {
        min = 0;
        current = 0;
        max = 0;

        if (!GetMonitorCapabilities(handle, out var caps, out _) || (caps & MC_CAPS_BRIGHTNESS) == 0)
        {
            return false;
        }

        return GetMonitorBrightness(handle, out min, out current, out max);
    }

    private static T WithPhysicalMonitors<T>(Func<List<PhysicalMonitor>, T> action)
    {
        var physicalMonitors = new List<PhysicalMonitor>();
        var handlesToDestroy = new List<nint>();

        try
        {
            EnumDisplayMonitors(nint.Zero, nint.Zero, (nint hMonitor, nint _, ref Rect _, nint _) =>
            {
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0)
                {
                    return true;
                }

                var monitors = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                {
                    return true;
                }

                foreach (var monitor in monitors)
                {
                    physicalMonitors.Add(monitor);
                    handlesToDestroy.Add(monitor.Handle);
                }

                return true;
            }, nint.Zero);

            return action(physicalMonitors);
        }
        finally
        {
            foreach (var handle in handlesToDestroy)
            {
                DestroyPhysicalMonitor(handle);
            }
        }
    }

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref Rect lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(nint hMonitor, uint dwPhysicalMonitorArraySize, [Out] PhysicalMonitor[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(nint hMonitor);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorCapabilities(nint hMonitor, out uint pdwMonitorCapabilities, out uint pdwSupportedColorTemperatures);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(nint hMonitor, out uint pdwMinimumBrightness, out uint pdwCurrentBrightness, out uint pdwMaximumBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(nint hMonitor, uint dwNewBrightness);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public nint Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }
}

internal sealed record PhysicalMonitorInfo(string Description, bool SupportsBrightness, uint Min, uint Current, uint Max);

internal sealed record HardwareApplyResult(int Attempted, int Changed, int Failed);
