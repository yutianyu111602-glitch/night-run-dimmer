namespace NightRunDimmer;

internal sealed class OverlayManager : IDisposable
{
    private readonly Dictionary<string, DimOverlayForm> overlays = new();
    private int dimPercent;

    public bool Enabled { get; private set; }

    public void SetEnabled(bool enabled, int percent, Form? owner = null)
    {
        Enabled = enabled;
        dimPercent = Math.Clamp(percent, 0, 90);
        if (Enabled)
        {
            Refresh(owner);
        }
        else
        {
            CloseAll();
        }
    }

    public void SetDim(int percent, Form? owner = null)
    {
        dimPercent = Math.Clamp(percent, 0, 90);
        if (!Enabled) return;

        Refresh(owner);
        foreach (var overlay in overlays.Values)
        {
            overlay.SetDim(dimPercent);
        }
    }

    public void Refresh(Form? owner = null)
    {
        if (!Enabled) return;

        var currentScreens = Screen.AllScreens.ToDictionary(screen => screen.DeviceName, StringComparer.OrdinalIgnoreCase);
        foreach (var staleKey in overlays.Keys.Where(key => !currentScreens.ContainsKey(key)).ToList())
        {
            overlays[staleKey].Close();
            overlays[staleKey].Dispose();
            overlays.Remove(staleKey);
        }

        foreach (var screen in currentScreens.Values)
        {
            if (!overlays.TryGetValue(screen.DeviceName, out var overlay))
            {
                overlay = new DimOverlayForm(screen, dimPercent);
                overlays.Add(screen.DeviceName, overlay);
                overlay.Show();
            }
            else
            {
                overlay.UpdateScreen(screen);
                overlay.SetDim(dimPercent);
            }
        }

        if (owner is not null)
        {
            owner.TopMost = true;
            owner.BringToFront();
        }
    }

    private void CloseAll()
    {
        foreach (var overlay in overlays.Values)
        {
            overlay.Close();
            overlay.Dispose();
        }
        overlays.Clear();
    }

    public void Dispose()
    {
        CloseAll();
    }
}
