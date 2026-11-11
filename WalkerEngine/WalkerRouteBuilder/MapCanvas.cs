using System.ComponentModel;
using System.Drawing.Drawing2D;
using WalkerEngine;

namespace WalkerRouteBuilder;

internal readonly record struct PointMove(int Index, MapPoint Point, bool BeginsGesture, bool EndsGesture = false);

internal readonly record struct PointInsert(int Index, MapPoint Point);

internal sealed class MapCanvas : Control
{
    private Bitmap? _bitmap;
    private MapBounds _bounds = MapBounds.Empty;
    private MapPoint? _detectedMarker;
    private IReadOnlyList<MapPoint> _points = [];
    private HashSet<int> _badSegments = [];
    private int _selected = -1;
    private bool _showRunner;
    private MapPoint _runner;
    private double _runnerHeading;
    private int _activeSegment = -1;
    private float _zoom = 1;
    private float _panX;
    private float _panY;
    private bool _fitted;

    private int _pressIndex = -1;
    private Point _pressScreen;
    private bool _draggingPoint;
    private bool _gestureSent;
    private bool _bornThisGesture;
    private bool _panning;
    private bool _panMoved;
    private Point _panAnchor;
    private float _panStartX;
    private float _panStartY;
    private int _hoverSegment = -1;

    public MapCanvas()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.FromArgb(12, 12, 12);
        TabStop = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool InputLocked { get; set; }

    public event EventHandler<MapPoint>? PointAdded;

    public event EventHandler<PointInsert>? PointInserted;

    public event EventHandler<PointMove>? PointMoved;

    public event EventHandler<int>? PointRemoveRequested;

    public event EventHandler<int>? SelectionChanged;

    public event EventHandler<MapPoint?>? Hovered;

    public void SetMap(Bitmap bitmap, MapBounds bounds, MapPoint? detectedMarker)
    {
        _bitmap = bitmap;
        _bounds = bounds;
        _detectedMarker = detectedMarker;
        _fitted = false;
        Fit();
    }

    public void SetRoute(IReadOnlyList<MapPoint> points, int selected, HashSet<int> badSegments)
    {
        _points = points;
        _selected = selected;
        _badSegments = badSegments;
        Invalidate();
    }

    public void SetRunner(MapPoint position, double headingRadians, int segmentIndex)
    {
        _showRunner = true;
        _runner = position;
        _runnerHeading = headingRadians;
        _activeSegment = segmentIndex;
        Invalidate();
    }

    public void ClearRunner()
    {
        if (!_showRunner && _activeSegment < 0)
            return;
        _showRunner = false;
        _activeSegment = -1;
        Invalidate();
    }

    public void Fit()
    {
        if (_bitmap is null || Width < 20 || Height < 20)
            return;

        float zoomX = (Width - 32f) / _bitmap.Width;
        float zoomY = (Height - 32f) / _bitmap.Height;
        _zoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.15f, 16f);
        _panX = (Width - _bitmap.Width * _zoom) / 2f;
        _panY = (Height - _bitmap.Height * _zoom) / 2f;
        _fitted = true;
        Invalidate();
    }

    public void CenterOn(MapPoint point)
    {
        if (!TryPixel(point, out double pixelX, out double pixelY))
            return;
        _panX = Width / 2f - (float)pixelX * _zoom;
        _panY = Height / 2f - (float)pixelY * _zoom;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_fitted)
            Fit();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (FindForm()?.ActiveControl is not NumericUpDown)
            Focus();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverSegment < 0)
            return;
        _hoverSegment = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_bitmap is null)
            return;

        if (e.Button == MouseButtons.Right)
        {
            _panning = true;
            _panMoved = false;
            _panAnchor = e.Location;
            _panStartX = _panX;
            _panStartY = _panY;
            return;
        }

        if (e.Button != MouseButtons.Left || InputLocked)
            return;

        int hit = HitPoint(e.Location);
        if (hit >= 0)
        {
            _pressIndex = hit;
            _pressScreen = e.Location;
            _draggingPoint = false;
            _gestureSent = false;
            _bornThisGesture = false;
            Capture = true;
            SelectionChanged?.Invoke(this, hit);
            return;
        }

        if (!TryMapPoint(e.Location, out MapPoint point))
            return;

        // Клик по ребру маршрута — вставка между точками (в место клика).
        int segment = HitSegment(e.Location, out _);
        if (segment >= 0)
        {
            _pressIndex = segment + 1;
            _pressScreen = e.Location;
            _draggingPoint = false;
            _gestureSent = false;
            _bornThisGesture = true;
            Capture = true;
            PointInserted?.Invoke(this, new PointInsert(_pressIndex, point));
            return;
        }

        _pressIndex = _points.Count;
        _pressScreen = e.Location;
        _draggingPoint = false;
        _gestureSent = false;
        _bornThisGesture = true;
        Capture = true;
        PointAdded?.Invoke(this, point);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_panning)
        {
            int dx = e.X - _panAnchor.X;
            int dy = e.Y - _panAnchor.Y;
            if (dx * dx + dy * dy > 9)
                _panMoved = true;
            _panX = _panStartX + dx;
            _panY = _panStartY + dy;
            Cursor = Cursors.SizeAll;
            Invalidate();
            return;
        }

        if (_pressIndex >= 0 && (e.Button & MouseButtons.Left) != 0 && !InputLocked)
        {
            int dx = e.X - _pressScreen.X;
            int dy = e.Y - _pressScreen.Y;
            if (!_draggingPoint && dx * dx + dy * dy >= 9)
                _draggingPoint = true;

            if (_draggingPoint && TryMapPoint(e.Location, out MapPoint moved))
            {
                PreviewPoint(_pressIndex, moved);
                bool begins = !_bornThisGesture && !_gestureSent;
                _gestureSent = true;
                PointMoved?.Invoke(this, new PointMove(_pressIndex, moved, begins));
            }

            Cursor = Cursors.SizeAll;
        }
        else
        {
            int overPoint = HitPoint(e.Location);
            int overSegment = overPoint < 0 ? HitSegment(e.Location, out _) : -1;
            if (overSegment != _hoverSegment)
            {
                _hoverSegment = overSegment;
                Invalidate();
            }

            Cursor = overPoint >= 0
                ? Cursors.Hand
                : overSegment >= 0 ? Cursors.Hand : Cursors.Cross;
        }

        Hovered?.Invoke(this, TryMapPoint(e.Location, out MapPoint hover) ? hover : null);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            if (_panning && !_panMoved)
            {
                int hit = HitPoint(e.Location);
                if (hit >= 0 && !InputLocked)
                    PointRemoveRequested?.Invoke(this, hit);
            }

            _panning = false;
            Cursor = Cursors.Cross;
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            if (_draggingPoint && _pressIndex >= 0 && TryMapPoint(e.Location, out MapPoint dropped))
            {
                PreviewPoint(_pressIndex, dropped);
                PointMoved?.Invoke(this, new PointMove(_pressIndex, dropped, BeginsGesture: false, EndsGesture: true));
            }

            _pressIndex = -1;
            _draggingPoint = false;
            _gestureSent = false;
            _bornThisGesture = false;
            if (Capture)
                Capture = false;
        }
    }

    private void PreviewPoint(int index, MapPoint point)
    {
        if (index < 0 || index >= _points.Count)
            return;
        var copy = new MapPoint[_points.Count];
        for (int i = 0; i < _points.Count; i++)
            copy[i] = i == index ? point : _points[i];
        _points = copy;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_bitmap is null)
            return;

        float next = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1f : 1f / 1.1f), 0.15f, 16f);
        if (Math.Abs(next - _zoom) < 0.0001f)
            return;

        float scale = next / _zoom;
        _zoom = next;
        _panX = e.X - (e.X - _panX) * scale;
        _panY = e.Y - (e.Y - _panY) * scale;
        _fitted = true;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.Clear(BackColor);
        if (_bitmap is null)
        {
            DrawHint(graphics, "Откройте карту. Затем левой кнопкой расставьте точки маршрута.");
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.TranslateTransform(_panX, _panY);
        graphics.ScaleTransform(_zoom, _zoom);

        graphics.DrawImage(_bitmap, 0, 0, _bitmap.Width, _bitmap.Height);
        DrawBounds(graphics);
        DrawSegments(graphics);
        DrawDetectedMarker(graphics);
        DrawPoints(graphics);
        DrawRunner(graphics);

        graphics.ResetTransform();
    }

    private void DrawBounds(Graphics graphics)
    {
        if (_bounds.IsEmpty)
            return;

        using var pen = new Pen(Color.FromArgb(160, 186, 90, 196), 1.4f / _zoom);
        graphics.DrawRectangle(pen, _bounds.Left, _bounds.Top, _bounds.Width - 1, _bounds.Height - 1);
    }

    private void DrawSegments(Graphics graphics)
    {
        if (_points.Count < 2)
            return;

        using var pen = new Pen(Color.FromArgb(255, 214, 92), 2.2f / _zoom);
        using var head = new SolidBrush(Color.FromArgb(255, 214, 92));
        float headSize = 13f / _zoom;

        for (int i = 0; i < _points.Count - 1; i++)
        {
            if (!TryPixel(_points[i], out double ax, out double ay) ||
                !TryPixel(_points[i + 1], out double bx, out double by))
                continue;

            bool bad = _badSegments.Contains(i);
            bool active = i == _activeSegment;
            bool hover = i == _hoverSegment;
            var color = bad
                ? Color.FromArgb(255, 120, 72)
                : active ? Color.White
                : hover ? Color.FromArgb(255, 240, 160)
                : Color.FromArgb(255, 214, 92);
            pen.Color = color;
            pen.Width = (active || hover ? 3.4f : 2.2f) / _zoom;
            pen.DashStyle = bad ? DashStyle.Dash : DashStyle.Solid;
            head.Color = color;

            double angle = Math.Atan2(by - ay, bx - ax);
            float length = (float)Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            var start = new PointF((float)ax, (float)ay);
            var tip = new PointF((float)bx, (float)by);
            if (length > headSize)
            {
                float shorten = headSize * 0.55f;
                var lineEnd = new PointF(
                    tip.X - shorten * (float)Math.Cos(angle),
                    tip.Y - shorten * (float)Math.Sin(angle));
                graphics.DrawLine(pen, start, lineEnd);
                DrawArrowHead(graphics, head, tip, angle, headSize);
            }
            else
            {
                graphics.DrawLine(pen, start, tip);
            }

            if (length * _zoom > 52f)
            {
                double degrees = (angle * 180.0 / Math.PI % 360 + 360) % 360;
                DrawLabel(
                    graphics,
                    $"{i + 1} · {degrees:0}°",
                    (float)((ax + bx) / 2) - (float)Math.Sin(angle) * (14f / _zoom),
                    (float)((ay + by) / 2) + (float)Math.Cos(angle) * (14f / _zoom),
                    color);
            }
        }
    }

    private void DrawDetectedMarker(Graphics graphics)
    {
        if (_detectedMarker is not MapPoint marker || !TryPixel(marker, out double x, out double y))
            return;

        float radius = 7f / _zoom;
        using var pen = new Pen(Color.FromArgb(120, 230, 90), 1.6f / _zoom);
        graphics.DrawEllipse(pen, (float)x - radius, (float)y - radius, radius * 2, radius * 2);
    }

    private void DrawPoints(Graphics graphics)
    {
        float radius = 5.5f / _zoom;
        using var fill = new SolidBrush(Color.White);
        using var selected = new SolidBrush(Color.FromArgb(255, 220, 90));
        using var outline = new Pen(Color.Black, 1.6f / _zoom);

        for (int i = 0; i < _points.Count; i++)
        {
            if (!TryPixel(_points[i], out double x, out double y))
                continue;

            float r = i == _selected ? radius + 2f / _zoom : radius;
            var rect = new RectangleF((float)x - r, (float)y - r, r * 2, r * 2);
            graphics.FillEllipse(i == _selected ? selected : fill, rect);
            graphics.DrawEllipse(outline, rect);
            DrawLabel(graphics, (i + 1).ToString(), (float)x + r + 2f / _zoom, (float)y - r, Color.White);
        }
    }

    private void DrawRunner(Graphics graphics)
    {
        if (!_showRunner || !TryPixel(_runner, out double x, out double y))
            return;

        float radius = 8f / _zoom;
        using var fill = new SolidBrush(Color.FromArgb(96, 220, 80));
        using var outline = new Pen(Color.White, 1.8f / _zoom);
        graphics.FillEllipse(fill, (float)x - radius, (float)y - radius, radius * 2, radius * 2);
        graphics.DrawEllipse(outline, (float)x - radius, (float)y - radius, radius * 2, radius * 2);

        float nose = 16f / _zoom;
        graphics.DrawLine(
            outline,
            (float)x,
            (float)y,
            (float)x + nose * (float)Math.Cos(_runnerHeading),
            (float)y + nose * (float)Math.Sin(_runnerHeading));
    }

    private static void DrawArrowHead(Graphics graphics, Brush brush, PointF tip, double angle, float size)
    {
        double left = angle + Math.PI - 0.42;
        double right = angle + Math.PI + 0.42;
        PointF a = new(tip.X + size * (float)Math.Cos(left), tip.Y + size * (float)Math.Sin(left));
        PointF b = new(tip.X + size * (float)Math.Cos(right), tip.Y + size * (float)Math.Sin(right));
        graphics.FillPolygon(brush, [tip, a, b]);
    }

    private void DrawLabel(Graphics graphics, string text, float x, float y, Color color)
    {
        using var font = new Font(Font.FontFamily, Math.Max(12f / _zoom, 1f), FontStyle.Bold, GraphicsUnit.Pixel);
        float shadow = Math.Max(1f / _zoom, 0.4f);
        using var dark = new SolidBrush(Color.FromArgb(210, 0, 0, 0));
        using var brush = new SolidBrush(color);
        graphics.DrawString(text, font, dark, x + shadow, y + shadow);
        graphics.DrawString(text, font, brush, x, y);
    }

    private static void DrawHint(Graphics graphics, string text)
    {
        using var font = new Font("Segoe UI", 11f);
        using var brush = new SolidBrush(Color.FromArgb(210, 210, 210));
        graphics.DrawString(text, font, brush, new PointF(24, 24));
    }

    private int HitPoint(Point screen)
    {
        for (int i = _points.Count - 1; i >= 0; i--)
        {
            if (!TryScreen(_points[i], out PointF place))
                continue;
            float dx = place.X - screen.X;
            float dy = place.Y - screen.Y;
            if (dx * dx + dy * dy <= 12f * 12f)
                return i;
        }

        return -1;
    }

    private int HitSegment(Point screen, out float t)
    {
        t = 0;
        var cursor = new PointF(screen.X, screen.Y);
        int found = -1;
        float best = 14f;
        for (int i = 0; i < _points.Count - 1; i++)
        {
            if (!TryScreen(_points[i], out PointF a) || !TryScreen(_points[i + 1], out PointF b))
                continue;

            float distance = DistanceToSegment(cursor, a, b, out float localT);
            if (localT < 0.03f || localT > 0.97f || distance >= best)
                continue;

            best = distance;
            found = i;
            t = localT;
        }

        return found;
    }

    private static float DistanceToSegment(PointF point, PointF a, PointF b, out float t)
    {
        float abx = b.X - a.X;
        float aby = b.Y - a.Y;
        float lengthSquared = abx * abx + aby * aby;
        if (lengthSquared < 0.001f)
        {
            t = 0;
            float dx0 = point.X - a.X;
            float dy0 = point.Y - a.Y;
            return MathF.Sqrt(dx0 * dx0 + dy0 * dy0);
        }

        t = Math.Clamp(((point.X - a.X) * abx + (point.Y - a.Y) * aby) / lengthSquared, 0f, 1f);
        float x = a.X + abx * t;
        float y = a.Y + aby * t;
        float dx = point.X - x;
        float dy = point.Y - y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private bool TryMapPoint(Point screen, out MapPoint point)
    {
        point = default;
        if (_bitmap is null)
            return false;

        double pixelX = (screen.X - _panX) / _zoom;
        double pixelY = (screen.Y - _panY) / _zoom;
        if (pixelX < -40 || pixelY < -40 || pixelX > _bitmap.Width + 40 || pixelY > _bitmap.Height + 40)
            return false;

        var bounds = EffectiveBounds();
        point = bounds.ToNormalized(pixelX, pixelY);
        return true;
    }

    private bool TryPixel(MapPoint point, out double pixelX, out double pixelY)
    {
        if (_bitmap is null)
        {
            pixelX = 0;
            pixelY = 0;
            return false;
        }

        (pixelX, pixelY) = EffectiveBounds().ToPixel(point);
        return true;
    }

    private bool TryScreen(MapPoint point, out PointF screen)
    {
        if (!TryPixel(point, out double pixelX, out double pixelY))
        {
            screen = default;
            return false;
        }

        screen = new PointF(_panX + (float)pixelX * _zoom, _panY + (float)pixelY * _zoom);
        return true;
    }

    private MapBounds EffectiveBounds()
    {
        if (!_bounds.IsEmpty)
            return _bounds;
        return _bitmap is null
            ? MapBounds.Empty
            : new MapBounds(0, 0, _bitmap.Width - 1, _bitmap.Height - 1);
    }
}
