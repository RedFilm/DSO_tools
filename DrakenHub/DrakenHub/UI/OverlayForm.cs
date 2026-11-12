using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DrakenHub.Core;

namespace DrakenHub.UI;

internal sealed class OverlayForm : Form
{
    private const int LoupeSrc = 17;   // odd: clear center pixel
    private const int LoupeZoom = 10;
    private const int LoupeGap = 28;

    private readonly int _size;
    private readonly bool _rectangle;
    private Point _cursor;
    private bool _saved;
    private bool _cancelled;
    private Bitmap? _screen;
    private Rectangle _virtualScreen;

    /// <summary>Cursor position at click (screen coords).</summary>
    public Point? Result { get; private set; }

    /// <summary>Top-left of the picker rectangle (screen coords). Same as Result for crosshair.</summary>
    public Point? Origin { get; private set; }

    public OverlayForm(int size, bool rectangle = false)
    {
        _size = Math.Max(8, size);
        _rectangle = rectangle;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Opacity = 0.85;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        Bounds = SystemInformation.VirtualScreen;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        CaptureScreenBehind();
        Capture = true;
        Focus();
        _cursor = Cursor.Position;
        var timer = new System.Windows.Forms.Timer { Interval = 16 };
        timer.Tick += (_, _) =>
        {
            if (_saved || _cancelled) return;
            var p = Cursor.Position;
            if (p != _cursor)
            {
                _cursor = p;
                Invalidate();
            }
        };
        timer.Start();
        Tag = timer;
        Invalidate();
    }

    private void CaptureScreenBehind()
    {
        _virtualScreen = SystemInformation.VirtualScreen;
        Opacity = 0;
        Refresh();
        _screen?.Dispose();
        _screen = new Bitmap(_virtualScreen.Width, _virtualScreen.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(_screen))
            g.CopyFromScreen(_virtualScreen.Location, Point.Empty, _virtualScreen.Size);
        Opacity = 0.85;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using var pen = new Pen(Color.Red, 2);
        using var brush = new SolidBrush(Color.FromArgb(80, 255, 0, 0));
        var local = PointToClient(_cursor);
        var half = _size / 2;
        var rect = new Rectangle(local.X - half, local.Y - half, _size, _size);

        g.FillRectangle(brush, rect);
        g.DrawRectangle(pen, rect);

        if (!_rectangle)
        {
            g.DrawLine(pen, local.X - half, local.Y, local.X + half, local.Y);
            g.DrawLine(pen, local.X, local.Y - half, local.X, local.Y + half);
        }

        DrawLoupe(g, local);
    }

    private void DrawLoupe(Graphics g, Point local)
    {
        if (_screen == null) return;

        var dstSize = LoupeSrc * LoupeZoom;
        var loupe = PlaceLoupe(local, dstSize);
        var halfSrc = LoupeSrc / 2;
        var srcX = _cursor.X - _virtualScreen.X - halfSrc;
        var srcY = _cursor.Y - _virtualScreen.Y - halfSrc;
        var src = new Rectangle(srcX, srcY, LoupeSrc, LoupeSrc);

        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceOver;

        using (var bg = new SolidBrush(Color.FromArgb(220, 20, 20, 24)))
            g.FillRectangle(bg, loupe);

        // Clip to loupe so out-of-bounds edges stay dark
        var state = g.Save();
        g.SetClip(loupe);
        g.DrawImage(_screen, loupe, src, GraphicsUnit.Pixel);
        g.Restore(state);

        // Center pixel outline
        var cx = loupe.X + halfSrc * LoupeZoom;
        var cy = loupe.Y + halfSrc * LoupeZoom;
        using (var pixelPen = new Pen(Color.Lime, 2))
            g.DrawRectangle(pixelPen, cx, cy, LoupeZoom, LoupeZoom);

        using (var border = new Pen(Color.White, 2))
            g.DrawRectangle(border, loupe);

        var color = SampleScreen(_cursor.X, _cursor.Y);
        var label = $"{color.R},{color.G},{color.B}";
        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        var labelSize = g.MeasureString(label, font);
        var labelRect = new RectangleF(
            loupe.X,
            loupe.Bottom + 4,
            Math.Max(loupe.Width, labelSize.Width + 10),
            labelSize.Height + 4);
        if (labelRect.Bottom > ClientSize.Height)
            labelRect.Y = loupe.Y - labelSize.Height - 6;
        using (var labelBg = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            g.FillRectangle(labelBg, labelRect);
        using (var labelBrush = new SolidBrush(Color.White))
            g.DrawString(label, font, labelBrush, labelRect.X + 4, labelRect.Y + 1);

        using var swatch = new SolidBrush(color);
        g.FillRectangle(swatch, loupe.Right - 18, labelRect.Y, 14, labelRect.Height);
        using (var swatchPen = new Pen(Color.White))
            g.DrawRectangle(swatchPen, loupe.Right - 18, (int)labelRect.Y, 14, (int)labelRect.Height);
    }

    private Rectangle PlaceLoupe(Point local, int dstSize)
    {
        var x = local.X + LoupeGap;
        var y = local.Y + LoupeGap;
        if (x + dstSize > ClientSize.Width - 8)
            x = local.X - LoupeGap - dstSize;
        if (y + dstSize + 28 > ClientSize.Height - 8)
            y = local.Y - LoupeGap - dstSize;
        x = Math.Clamp(x, 8, Math.Max(8, ClientSize.Width - dstSize - 8));
        y = Math.Clamp(y, 8, Math.Max(8, ClientSize.Height - dstSize - 8));
        return new Rectangle(x, y, dstSize, dstSize);
    }

    private Color SampleScreen(int screenX, int screenY)
    {
        if (_screen == null) return Color.Black;
        var x = screenX - _virtualScreen.X;
        var y = screenY - _virtualScreen.Y;
        if (x < 0 || y < 0 || x >= _screen.Width || y >= _screen.Height)
            return Color.Black;
        return _screen.GetPixel(x, y);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var cursor = Cursor.Position;
        Result = cursor;
        var half = _size / 2;
        Origin = _rectangle
            ? new Point(cursor.X - half, cursor.Y - half)
            : cursor;
        _saved = true;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Result = null;
            Origin = null;
            _cancelled = true;
            Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (Tag is System.Windows.Forms.Timer timer)
        {
            timer.Stop();
            timer.Dispose();
        }
        _screen?.Dispose();
        _screen = null;
        base.OnFormClosed(e);
    }
}

internal sealed class GridOverlayForm : Form
{
    private List<Rectangle> _cells;
    private List<Rectangle> _tabs;
    private List<Point> _samplePoints;

    public GridOverlayForm(List<Rectangle> cells, List<Rectangle> tabs, List<Point> samplePoints)
    {
        _cells = cells;
        _tabs = tabs;
        _samplePoints = samplePoints;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Bounds = SystemInformation.VirtualScreen;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void UpdateGeometry(List<Rectangle> cells, List<Rectangle> tabs, List<Point> samplePoints)
    {
        _cells = cells;
        _tabs = tabs;
        _samplePoints = samplePoints;
        if (!Visible) Show();
        else
        {
            BringToFront();
            Invalidate();
            Update();
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // Layered + transparent to clicks, but still painted.
            cp.ExStyle |= 0x80000 | 0x20; // WS_EX_LAYERED | WS_EX_TRANSPARENT
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.Red, 1);
        using var brush = new SolidBrush(Color.Red);
        foreach (var cell in _cells)
        {
            var r = ToClientRect(cell);
            e.Graphics.DrawRectangle(pen, r);
        }
        foreach (var tab in _tabs)
        {
            var r = ToClientRect(tab);
            e.Graphics.FillRectangle(brush, r);
        }
        foreach (var p in _samplePoints)
        {
            var c = PointToClient(p);
            e.Graphics.FillRectangle(brush, c.X, c.Y, 2, 2);
        }
    }

    private Rectangle ToClientRect(Rectangle screenRect)
    {
        var tl = PointToClient(screenRect.Location);
        return new Rectangle(tl, screenRect.Size);
    }
}
