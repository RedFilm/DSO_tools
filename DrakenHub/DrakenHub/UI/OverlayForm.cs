using DrakenHub.Core;

namespace DrakenHub.UI;

internal sealed class OverlayForm : Form
{
    private readonly int _size;
    private readonly bool _rectangle;
    private Point _cursor;
    private bool _saved;
    private bool _cancelled;

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
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Capture = true;
        Focus();
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
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.Red, 2);
        using var brush = new SolidBrush(Color.FromArgb(80, 255, 0, 0));
        var local = PointToClient(_cursor);
        var half = _size / 2;
        var rect = new Rectangle(local.X - half, local.Y - half, _size, _size);

        e.Graphics.FillRectangle(brush, rect);
        e.Graphics.DrawRectangle(pen, rect);

        if (!_rectangle)
        {
            e.Graphics.DrawLine(pen, local.X - half, local.Y, local.X + half, local.Y);
            e.Graphics.DrawLine(pen, local.X, local.Y - half, local.X, local.Y + half);
        }
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
