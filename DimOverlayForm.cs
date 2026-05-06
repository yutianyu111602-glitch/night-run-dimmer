using System.Runtime.InteropServices;

namespace NightRunDimmer;

internal sealed class DimOverlayForm : Form
{
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    public DimOverlayForm(Screen screen, int dimPercent)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Bounds = screen.Bounds;
        TopMost = true;
        Enabled = false;
        SetDim(dimPercent);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void UpdateScreen(Screen screen)
    {
        Bounds = screen.Bounds;
    }

    public void SetDim(int dimPercent)
    {
        var clamped = Math.Clamp(dimPercent, 0, 90);
        Opacity = clamped / 100d;
    }
}
