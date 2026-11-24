using System.Runtime.InteropServices;
using DrakenHub.Core;

namespace DrakenHub.UI;

internal enum CaptionGlyph
{
    Minimize,
    Maximize,
    Restore,
    Close
}

/// <summary>Custom title bar: icon, name, and minimize / maximize / close.</summary>
internal sealed class WindowChrome : Panel
{
    public const int Grip = 6;
    public const int ButtonWidth = 46;

    private readonly CaptionButton _min;
    private readonly CaptionButton _max;
    private readonly CaptionButton _close;
    private readonly Bitmap _icon;
    private readonly Form _form;

    public WindowChrome(Form form)
    {
        _form = form;
        _icon = AppIcon.Bitmap(64);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Dock = DockStyle.Top;
        Height = 40;
        BackColor = Theme.Header;

        _min = new CaptionButton(CaptionGlyph.Minimize);
        _max = new CaptionButton(CaptionGlyph.Maximize);
        _close = new CaptionButton(CaptionGlyph.Close);
        _min.Click += (_, _) => form.WindowState = FormWindowState.Minimized;
        _max.Click += (_, _) => ToggleMaximize();
        _close.Click += (_, _) => form.Close();

        // Docked last lands on the right edge.
        Controls.Add(_min);
        Controls.Add(_max);
        Controls.Add(_close);

        MouseDown += BeginDrag;
    }

    public void SetMaximized(bool maximized)
    {
        _max.Glyph = maximized ? CaptionGlyph.Restore : CaptionGlyph.Maximize;
    }

    public void ApplyTooltips(ToolTip tips, Localization loc)
    {
        tips.SetToolTip(_min, loc.T("win.minimize"));
        tips.SetToolTip(_max, loc.T(_max.Glyph == CaptionGlyph.Restore ? "win.restore" : "win.maximize"));
        tips.SetToolTip(_close, loc.T("win.close"));
    }

    public static void EnableShadow(IntPtr hwnd)
    {
        try
        {
            var corner = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
            var margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (e.Clicks >= 2)
        {
            ToggleMaximize();
            return;
        }

        ReleaseCapture();
        SendMessage(_form.Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }

    private void ToggleMaximize()
    {
        _form.WindowState = _form.WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        var side = Math.Max(16, Height - 16);
        g.DrawImage(_icon, 12, (Height - side) / 2, side, side);
        TextRenderer.DrawText(
            g,
            "DrakenHub",
            Theme.UiFontValue,
            new Rectangle(12 + side + 8, 0, Math.Max(0, Width - ButtonWidth * 3 - side - 28), Height),
            Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using var line = new Pen(Theme.HeaderLine);
        g.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _icon.Dispose();
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }
}

internal sealed class CaptionButton : Control
{
    private bool _hot;
    private CaptionGlyph _glyph;

    public CaptionButton(CaptionGlyph glyph)
    {
        _glyph = glyph;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Width = WindowChrome.ButtonWidth;
        Dock = DockStyle.Right;
        Cursor = Cursors.Hand;
        BackColor = Theme.Header;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public CaptionGlyph Glyph
    {
        get => _glyph;
        set
        {
            if (_glyph == value) return;
            _glyph = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var bg = BackColor;
        if (_hot)
            bg = Glyph == CaptionGlyph.Close ? Theme.Danger : Theme.NavHover;
        using (var brush = new SolidBrush(bg))
            g.FillRectangle(brush, ClientRectangle);

        var ink = _hot && Glyph == CaptionGlyph.Close ? Color.FromArgb(248, 236, 232) : Theme.Text;
        using var pen = new Pen(ink, 1.4f);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var cx = Width / 2;
        var cy = Height / 2;
        const int s = 5;
        switch (Glyph)
        {
            case CaptionGlyph.Minimize:
                g.DrawLine(pen, cx - s, cy, cx + s, cy);
                break;
            case CaptionGlyph.Maximize:
                g.DrawRectangle(pen, cx - s, cy - s, s * 2, s * 2);
                break;
            case CaptionGlyph.Restore:
                g.DrawRectangle(pen, cx - s + 2, cy - s, s * 2 - 2, s * 2 - 2);
                g.DrawLine(pen, cx - s, cy - s + 3, cx - s, cy + s);
                g.DrawLine(pen, cx - s, cy + s, cx + s - 2, cy + s);
                g.DrawLine(pen, cx - s, cy - s + 3, cx - 1, cy - s + 3);
                break;
            default:
                g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
                break;
        }
    }
}
