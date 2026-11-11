using System.Drawing.Drawing2D;

namespace WalkerRouteBuilder;

internal sealed class DiagramDrop : EventArgs
{
    public required string Kind { get; init; }

    public required List<ScenarioBlock> Parent { get; init; }

    public required int Index { get; init; }
}

internal readonly record struct DiagramPick(ScenarioBlock? Block, bool Toggle, bool Range, bool KeepIfPicked);

internal sealed class DiagramMove : EventArgs
{
    public required ScenarioBlock Block { get; init; }

    public required List<ScenarioBlock> Parent { get; init; }

    public required int Index { get; init; }
}

internal sealed class ChainDiagramDrop : EventArgs
{
    public required string ScenarioFile { get; init; }

    public required int Index { get; init; }
}

internal sealed class ChainDiagramMove : EventArgs
{
    public required ChainStep Step { get; init; }

    public required int Index { get; init; }
}

internal readonly record struct ChainStepInfo(string Title, string Detail, bool Missing);

internal sealed class DiagramCanvas : Control
{
    public const string ScenarioRefPrefix = "scenario-ref:";

    private const float CardWidth = 214f;
    private const float CardHeight = 86f;
    private const float Gap = 54f;
    private static readonly Color ChainAccent = Color.FromArgb(120, 176, 255);

    private readonly List<NodeBox> _nodes = [];
    private readonly List<StepBox> _steps = [];
    private readonly List<PauseBadge> _pauses = [];
    private readonly List<StepPauseBadge> _stepPauses = [];
    private Scenario? _scenario;
    private ScenarioChain? _chain;
    private readonly List<ScenarioBlock> _picked = [];
    private ChainStep? _pickedStep;
    private ScenarioBlock? _runningBlock;
    private ChainStep? _runningStep;
    private bool _selectionRunning;
    private bool _scenarioRunning;
    private string _caption = "";
    private Func<ChainStep, ChainStepInfo>? _stepInfo;
    private float _zoom = 1f;
    private float _panX = 36f;
    private float _panY = 28f;
    private bool _fitted;
    private RectangleF _content = new(0, 0, 640, 240);

    private ScenarioBlock? _dragBlock;
    private ChainStep? _dragStep;
    private bool _draggingNode;
    private bool _panning;
    private bool _panMoved;
    private Point _press;
    private float _panStartX;
    private float _panStartY;
    private bool _dropActive;
    private List<ScenarioBlock>? _dropParent;
    private int _dropIndex;
    private bool _chainDropActive;
    private int _chainDropIndex;

    public DiagramCanvas()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.FromArgb(17, 19, 22);
        AllowDrop = true;
        TabStop = true;
    }

    public bool IsChainMode => _chain is not null;

    public event Action<DiagramPick>? BlockSelected;

    public event EventHandler<DiagramDrop>? InsertKind;

    public event EventHandler<DiagramMove>? MoveBlock;

    public event Action<ScenarioBlock, Point>? BlockMenu;

    public event Action<ScenarioBlock>? PauseRequested;

    public event Action<ChainStep?>? StepSelected;

    public event Action<ChainStep>? StepOpened;

    public event EventHandler<ChainDiagramDrop>? InsertScenario;

    public event EventHandler<ChainDiagramMove>? MoveStep;

    public event Action<ChainStep, Point>? StepMenu;

    public event Action<ChainStep>? StepPauseRequested;

    public void Show(Scenario? scenario, ScenarioBlock? selected, IEnumerable<ScenarioBlock> picked, bool scenarioRunning, ScenarioBlock? runningBlock, bool selectionRunning, string caption)
    {
        bool switched = !ReferenceEquals(_scenario, scenario) || _chain is not null;
        _chain = null;
        _pickedStep = null;
        _runningStep = null;
        _stepInfo = null;
        _scenario = scenario;
        _picked.Clear();
        _picked.AddRange(picked);
        if (selected is not null && !_picked.Any(block => ReferenceEquals(block, selected)))
            _picked.Add(selected);
        _scenarioRunning = scenarioRunning;
        _runningBlock = runningBlock;
        _selectionRunning = selectionRunning;
        _caption = caption;
        if (switched)
        {
            _fitted = false;
            Fit();
        }

        Invalidate();
    }

    public void ShowChain(
        ScenarioChain? chain,
        ChainStep? selected,
        ChainStep? running,
        string caption,
        Func<ChainStep, ChainStepInfo> info)
    {
        bool switched = !ReferenceEquals(_chain, chain) || _scenario is not null;
        _scenario = null;
        _picked.Clear();
        _runningBlock = null;
        _selectionRunning = false;
        _scenarioRunning = false;
        _chain = chain;
        _pickedStep = selected;
        _runningStep = running;
        _caption = caption;
        _stepInfo = info;
        if (switched)
        {
            _fitted = false;
            Fit();
        }

        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_fitted && Width > 80 && Height > 80)
            Fit();
    }

    public void Fit()
    {
        LayoutNodes();
        if (_content.Width < 8 || Width < 40 || Height < 40)
        {
            _zoom = 1f;
            _panX = 36f;
            _panY = 28f;
            return;
        }

        float zoomX = (Width - 72f) / _content.Width;
        float zoomY = (Height - 88f) / _content.Height;
        _zoom = Math.Clamp(Math.Min(1f, Math.Min(zoomX, zoomY)), 0.55f, 1f);
        _panX = Math.Max(28f, (Width - _content.Width * _zoom) / 2f);
        _panY = 36f;
        _fitted = true;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(BackColor);
        DrawGrid(graphics);
        LayoutNodes();

        if (_chain is not null)
        {
            PaintChain(graphics);
            return;
        }

        if (_scenario is null)
        {
            DrawMessage(graphics, "Выберите карту слева", "Добавьте карты и соберите для каждой свой сценарий.");
            DrawChrome(graphics);
            return;
        }

        if (_scenario.Blocks.Count == 0 && _nodes.Count == 0)
            DrawMessage(graphics, "Пустой сценарий", "Перетащите блок из панели слева на эту область.");

        graphics.TranslateTransform(_panX, _panY);
        graphics.ScaleTransform(_zoom, _zoom);
        DrawLinks(graphics);
        if (_dropActive && _dropParent is not null)
            DrawDropMark(graphics);
        foreach (NodeBox node in _nodes)
            DrawNode(graphics, node);
        graphics.ResetTransform();
        DrawChrome(graphics);
    }

    private void PaintChain(Graphics graphics)
    {
        if (_chain!.Steps.Count == 0)
            DrawMessage(graphics, "Пустая цепочка", "Перетащите сценарий из библиотеки на эту область.");

        graphics.TranslateTransform(_panX, _panY);
        graphics.ScaleTransform(_zoom, _zoom);
        DrawStepLinks(graphics);
        if (_chainDropActive)
            DrawChainDropMark(graphics);
        foreach (StepBox step in _steps)
            DrawStep(graphics, step);
        graphics.ResetTransform();
        DrawChrome(graphics);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_chain is null || e.Button != MouseButtons.Left)
            return;
        if (HitStep(e.Location) is StepBox hit)
            StepOpened?.Invoke(hit.Step);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_chain is not null)
        {
            OnChainMouseDown(e);
            return;
        }

        if (e.Button == MouseButtons.Left && HitPause(e.Location) is ScenarioBlock paused)
        {
            PauseRequested?.Invoke(paused);
            return;
        }

        _press = e.Location;
        _panMoved = false;
        NodeBox? hit = Hit(e.Location);

        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && hit is null))
        {
            _panning = true;
            _panStartX = _panX;
            _panStartY = _panY;
            Capture = true;
            return;
        }

        if (e.Button == MouseButtons.Right && hit is not null)
        {
            BlockSelected?.Invoke(new DiagramPick(hit.Block, false, false, true));
            BlockMenu?.Invoke(hit.Block, e.Location);
            return;
        }

        if (e.Button != MouseButtons.Left || hit is null)
            return;

        bool toggle = (ModifierKeys & Keys.Control) == Keys.Control;
        bool range = (ModifierKeys & Keys.Shift) == Keys.Shift;
        if (!toggle && !range)
        {
            _dragBlock = hit.Block;
            Capture = true;
        }

        BlockSelected?.Invoke(new DiagramPick(hit.Block, toggle, range, false));
        Invalidate();
    }

    private void OnChainMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && HitStepPause(e.Location) is ChainStep paused)
        {
            StepPauseRequested?.Invoke(paused);
            return;
        }

        _press = e.Location;
        _panMoved = false;
        StepBox? hit = HitStep(e.Location);

        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && hit is null))
        {
            _panning = true;
            _panStartX = _panX;
            _panStartY = _panY;
            Capture = true;
            return;
        }

        if (e.Button == MouseButtons.Right && hit is not null)
        {
            _pickedStep = hit.Step;
            StepSelected?.Invoke(hit.Step);
            StepMenu?.Invoke(hit.Step, e.Location);
            Invalidate();
            return;
        }

        if (e.Button != MouseButtons.Left || hit is null)
            return;

        _dragStep = hit.Step;
        Capture = true;
        _pickedStep = hit.Step;
        StepSelected?.Invoke(hit.Step);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_chain is not null)
        {
            OnChainMouseMove(e);
            return;
        }

        if (!_panning && _dragBlock is null)
            Cursor = HitPause(e.Location) is null ? Cursors.Default : Cursors.Hand;
        if (_panning)
        {
            _panX = _panStartX + e.X - _press.X;
            _panY = _panStartY + e.Y - _press.Y;
            _panMoved = Math.Abs(e.X - _press.X) + Math.Abs(e.Y - _press.Y) > 3;
            Invalidate();
            return;
        }

        if (_dragBlock is null || e.Button != MouseButtons.Left)
            return;

        if (!_draggingNode && Math.Abs(e.X - _press.X) + Math.Abs(e.Y - _press.Y) > 5)
            _draggingNode = true;
        if (!_draggingNode)
            return;

        UpdateDrop(e.Location);
        Invalidate();
    }

    private void OnChainMouseMove(MouseEventArgs e)
    {
        if (!_panning && _dragStep is null)
            Cursor = HitStepPause(e.Location) is null ? Cursors.Default : Cursors.Hand;
        if (_panning)
        {
            _panX = _panStartX + e.X - _press.X;
            _panY = _panStartY + e.Y - _press.Y;
            _panMoved = Math.Abs(e.X - _press.X) + Math.Abs(e.Y - _press.Y) > 3;
            Invalidate();
            return;
        }

        if (_dragStep is null || e.Button != MouseButtons.Left)
            return;

        if (!_draggingNode && Math.Abs(e.X - _press.X) + Math.Abs(e.Y - _press.Y) > 5)
            _draggingNode = true;
        if (!_draggingNode)
            return;

        UpdateChainDrop(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_chain is not null)
        {
            OnChainMouseUp(e);
            return;
        }

        bool wasPanning = _panning;
        bool moved = _panMoved;
        bool dragging = _draggingNode;
        ScenarioBlock? block = _dragBlock;
        List<ScenarioBlock>? parent = _dropParent;
        int index = _dropIndex;
        _panning = false;
        _draggingNode = false;
        _dragBlock = null;
        _dropActive = false;
        Capture = false;

        if (wasPanning && !moved && e.Button == MouseButtons.Left)
        {
            BlockSelected?.Invoke(new DiagramPick(null, false, false, false));
        }
        else if (dragging && block is not null && parent is not null && _scenario is not null && !ContainsList(block, parent))
        {
            MoveBlock?.Invoke(this, new DiagramMove { Block = block, Parent = parent, Index = index });
        }

        Invalidate();
    }

    private void OnChainMouseUp(MouseEventArgs e)
    {
        bool wasPanning = _panning;
        bool moved = _panMoved;
        bool dragging = _draggingNode;
        ChainStep? step = _dragStep;
        int index = _chainDropIndex;
        _panning = false;
        _draggingNode = false;
        _dragStep = null;
        _chainDropActive = false;
        Capture = false;

        if (wasPanning && !moved && e.Button == MouseButtons.Left)
            StepSelected?.Invoke(null);
        else if (dragging && step is not null)
            MoveStep?.Invoke(this, new ChainDiagramMove { Step = step, Index = index });

        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float previous = _zoom;
        float next = Math.Clamp(e.Delta > 0 ? _zoom * 1.08f : _zoom / 1.08f, 0.5f, 1.55f);
        float worldX = (e.X - _panX) / previous;
        float worldY = (e.Y - _panY) / previous;
        _zoom = next;
        _panX = e.X - worldX * _zoom;
        _panY = e.Y - worldY * _zoom;
        Invalidate();
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (_chain is not null)
        {
            e.Effect = ReadScenarioRef(e.Data) is not null ? DragDropEffects.Copy : DragDropEffects.None;
            return;
        }

        e.Effect = HasBlockKind(e.Data) && ReadScenarioRef(e.Data) is null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (_chain is not null)
        {
            if (ReadScenarioRef(e.Data) is null)
            {
                e.Effect = DragDropEffects.None;
                _chainDropActive = false;
                Invalidate();
                return;
            }

            e.Effect = DragDropEffects.Copy;
            UpdateChainDrop(PointToClient(new Point(e.X, e.Y)));
            Invalidate();
            return;
        }

        if (!HasBlockKind(e.Data) || ReadScenarioRef(e.Data) is not null || _scenario is null)
        {
            e.Effect = DragDropEffects.None;
            _dropActive = false;
            Invalidate();
            return;
        }

        e.Effect = DragDropEffects.Copy;
        UpdateDrop(PointToClient(new Point(e.X, e.Y)));
        Invalidate();
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _dropActive = false;
        _chainDropActive = false;
        Invalidate();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (_chain is not null)
        {
            _chainDropActive = false;
            if (ReadScenarioRef(e.Data) is string path)
                InsertScenario?.Invoke(this, new ChainDiagramDrop { ScenarioFile = path, Index = _chainDropIndex });
            Invalidate();
            return;
        }

        _dropActive = false;
        if (_scenario is null || ReadKind(e.Data) is not string kind || ReadScenarioRef(e.Data) is not null || _dropParent is null)
        {
            Invalidate();
            return;
        }

        InsertKind?.Invoke(this, new DiagramDrop { Kind = kind, Parent = _dropParent, Index = _dropIndex });
        Invalidate();
    }

    private void UpdateDrop(Point location)
    {
        if (_scenario is null)
        {
            _dropActive = false;
            return;
        }

        LayoutNodes();
        PointF world = ToWorld(location);
        NodeBox? container = null;
        foreach (NodeBox node in _nodes)
        {
            if (!node.IsRepeat || ReferenceEquals(node.Block, _dragBlock))
                continue;
            if (node.Frame.Contains(world) && !node.Header.Contains(world))
                container = node;
        }

        List<ScenarioBlock> parent = container is null
            ? _scenario.Blocks
            : ((RepeatBlock)container.Block).Children;
        if (_dragBlock is not null && ContainsList(_dragBlock, parent))
            parent = _scenario.Blocks;

        var siblings = _nodes
            .Where(node => ReferenceEquals(node.Parent, parent) && !ReferenceEquals(node.Block, _dragBlock))
            .OrderBy(node => node.Index)
            .ToList();
        int index = parent.Count;
        foreach (NodeBox sibling in siblings)
        {
            float mid = sibling.Frame.Left + sibling.Frame.Width / 2f;
            if (world.X < mid)
            {
                index = sibling.Index;
                break;
            }
        }

        _dropParent = parent;
        _dropIndex = index;
        _dropActive = true;
    }

    private void UpdateChainDrop(Point location)
    {
        if (_chain is null)
        {
            _chainDropActive = false;
            return;
        }

        LayoutNodes();
        PointF world = ToWorld(location);
        var siblings = _steps
            .Where(step => !ReferenceEquals(step.Step, _dragStep))
            .OrderBy(step => step.Index)
            .ToList();
        int index = _chain.Steps.Count;
        foreach (StepBox sibling in siblings)
        {
            float mid = sibling.Frame.Left + sibling.Frame.Width / 2f;
            if (world.X < mid)
            {
                index = sibling.Index;
                break;
            }
        }

        _chainDropIndex = index;
        _chainDropActive = true;
    }

    private void LayoutNodes()
    {
        _nodes.Clear();
        _steps.Clear();
        if (_chain is not null)
        {
            SizeF size = LayoutChainSteps(_chain.Steps, 12f, 12f);
            _content = new RectangleF(0, 0, Math.Max(320f, size.Width + 24f), Math.Max(180f, size.Height + 24f));
            return;
        }

        if (_scenario is null)
        {
            _content = new RectangleF(0, 0, 640, 240);
            return;
        }

        var counters = new int[8];
        SizeF sizeBlocks = LayoutChain(_scenario.Blocks, 12f, 12f, 0, counters);
        _content = new RectangleF(0, 0, Math.Max(320f, sizeBlocks.Width + 24f), Math.Max(180f, sizeBlocks.Height + 24f));
    }

    private SizeF LayoutChainSteps(List<ChainStep> steps, float x, float y)
    {
        if (steps.Count == 0)
            return new SizeF(48f, CardHeight);

        float cursor = x;
        for (int i = 0; i < steps.Count; i++)
        {
            var box = new StepBox
            {
                Step = steps[i],
                Index = i,
                Number = (i + 1).ToString(),
                Frame = new RectangleF(cursor, y, CardWidth, CardHeight),
            };
            _steps.Add(box);
            cursor += CardWidth + Gap;
        }

        return new SizeF(cursor - x - Gap, CardHeight);
    }

    private SizeF LayoutChain(List<ScenarioBlock> blocks, float x, float y, int depth, int[] counters)
    {
        if (blocks.Count == 0)
            return new SizeF(48f, CardHeight);

        float cursor = x;
        float height = 0f;
        int level = Math.Min(depth, counters.Length - 1);
        for (int i = 0; i < blocks.Count; i++)
        {
            counters[level]++;
            for (int deeper = level + 1; deeper < counters.Length; deeper++)
                counters[deeper] = 0;

            ScenarioBlock block = blocks[i];
            var node = new NodeBox
            {
                Block = block,
                Parent = blocks,
                Index = i,
                Number = string.Join('.', counters.Take(level + 1)),
                Depth = depth,
            };
            _nodes.Add(node);

            if (block is RepeatBlock repeat)
            {
                SizeF inner = LayoutChain(repeat.Children, cursor + 18f, y + 74f, depth + 1, counters);
                float width = Math.Max(CardWidth + 28f, inner.Width + 36f);
                float frameHeight = 74f + Math.Max(inner.Height, 72f) + 16f;
                node.Header = new RectangleF(cursor, y, width, 62f);
                node.Frame = new RectangleF(cursor, y, width, frameHeight);
                node.IsRepeat = true;
                cursor += width + Gap;
                height = Math.Max(height, frameHeight);
            }
            else
            {
                node.Header = new RectangleF(cursor, y, CardWidth, CardHeight);
                node.Frame = node.Header;
                cursor += CardWidth + Gap;
                height = Math.Max(height, CardHeight);
            }
        }

        return new SizeF(cursor - x - Gap, height);
    }

    private void DrawLinks(Graphics graphics)
    {
        CollectPauses();
        using var pen = new Pen(Color.FromArgb(150, 92, 104, 118), 1.7f);
        pen.CustomEndCap = new AdjustableArrowCap(4.2f, 5.4f, true);
        pen.StartCap = LineCap.Round;
        using var font = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        foreach (PauseBadge badge in _pauses)
        {
            graphics.DrawBezier(pen, badge.Start.X, badge.Start.Y, badge.Control1.X, badge.Control1.Y, badge.Control2.X, badge.Control2.Y, badge.End.X, badge.End.Y);
            bool idle = badge.Block.PauseAfterMilliseconds <= 0;
            using var fill = new SolidBrush(idle ? Color.FromArgb(28, 32, 38) : Color.FromArgb(48, 42, 28));
            using var border = new Pen(idle ? Color.FromArgb(92, 104, 118) : Color.FromArgb(214, 176, 96), 1f);
            using var textBrush = new SolidBrush(idle ? Color.FromArgb(168, 176, 186) : Color.FromArgb(255, 214, 140));
            FillRound(graphics, fill, badge.Bounds, 8f);
            DrawRound(graphics, border, badge.Bounds, 8f);
            graphics.DrawString(FormatPause(badge.Block.PauseAfterMilliseconds), font, textBrush, badge.Bounds, format);
        }
    }

    private void DrawStepLinks(Graphics graphics)
    {
        CollectStepPauses();
        using var pen = new Pen(Color.FromArgb(150, 92, 104, 118), 1.7f);
        pen.CustomEndCap = new AdjustableArrowCap(4.2f, 5.4f, true);
        pen.StartCap = LineCap.Round;
        using var font = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        foreach (StepPauseBadge badge in _stepPauses)
        {
            graphics.DrawBezier(pen, badge.Start.X, badge.Start.Y, badge.Control1.X, badge.Control1.Y, badge.Control2.X, badge.Control2.Y, badge.End.X, badge.End.Y);
            bool idle = badge.Step.PauseAfterMilliseconds <= 0;
            using var fill = new SolidBrush(idle ? Color.FromArgb(28, 32, 38) : Color.FromArgb(48, 42, 28));
            using var border = new Pen(idle ? Color.FromArgb(92, 104, 118) : Color.FromArgb(214, 176, 96), 1f);
            using var textBrush = new SolidBrush(idle ? Color.FromArgb(168, 176, 186) : Color.FromArgb(255, 214, 140));
            FillRound(graphics, fill, badge.Bounds, 8f);
            DrawRound(graphics, border, badge.Bounds, 8f);
            graphics.DrawString(FormatPause(badge.Step.PauseAfterMilliseconds), font, textBrush, badge.Bounds, format);
        }
    }

    private void CollectPauses()
    {
        _pauses.Clear();
        foreach (IGrouping<List<ScenarioBlock>, NodeBox> group in _nodes.GroupBy(node => node.Parent))
        {
            NodeBox[] chain = group.OrderBy(node => node.Index).ToArray();
            for (int i = 0; i < chain.Length - 1; i++)
            {
                RectangleF from = chain[i].Frame;
                RectangleF to = chain[i + 1].Frame;
                float x1 = from.Right;
                float y1 = from.Top + (chain[i].IsRepeat ? 31f : from.Height / 2f);
                float x2 = to.Left;
                float y2 = to.Top + (chain[i + 1].IsRepeat ? 31f : to.Height / 2f);
                float bend = Math.Max(28f, (x2 - x1) * 0.45f);
                float midX = (x1 + x2) / 2f;
                float midY = (y1 + y2) / 2f;
                var bounds = new RectangleF(midX - 24f, midY - 10f, 48f, 20f);
                _pauses.Add(new PauseBadge
                {
                    Block = chain[i].Block,
                    Bounds = bounds,
                    Start = new PointF(x1, y1),
                    Control1 = new PointF(x1 + bend, y1),
                    Control2 = new PointF(x2 - bend, y2),
                    End = new PointF(x2, y2),
                });
            }
        }
    }

    private void CollectStepPauses()
    {
        _stepPauses.Clear();
        StepBox[] chain = _steps.OrderBy(step => step.Index).ToArray();
        for (int i = 0; i < chain.Length - 1; i++)
        {
            RectangleF from = chain[i].Frame;
            RectangleF to = chain[i + 1].Frame;
            float x1 = from.Right;
            float y1 = from.Top + from.Height / 2f;
            float x2 = to.Left;
            float y2 = to.Top + to.Height / 2f;
            float bend = Math.Max(28f, (x2 - x1) * 0.45f);
            float midX = (x1 + x2) / 2f;
            float midY = (y1 + y2) / 2f;
            _stepPauses.Add(new StepPauseBadge
            {
                Step = chain[i].Step,
                Bounds = new RectangleF(midX - 24f, midY - 10f, 48f, 20f),
                Start = new PointF(x1, y1),
                Control1 = new PointF(x1 + bend, y1),
                Control2 = new PointF(x2 - bend, y2),
                End = new PointF(x2, y2),
            });
        }
    }

    private ScenarioBlock? HitPause(Point location)
    {
        if (_scenario is null)
            return null;
        LayoutNodes();
        CollectPauses();
        PointF world = ToWorld(location);
        for (int i = _pauses.Count - 1; i >= 0; i--)
        {
            if (_pauses[i].Bounds.Contains(world))
                return _pauses[i].Block;
        }

        return null;
    }

    private ChainStep? HitStepPause(Point location)
    {
        if (_chain is null)
            return null;
        LayoutNodes();
        CollectStepPauses();
        PointF world = ToWorld(location);
        for (int i = _stepPauses.Count - 1; i >= 0; i--)
        {
            if (_stepPauses[i].Bounds.Contains(world))
                return _stepPauses[i].Step;
        }

        return null;
    }

    private static string FormatPause(int milliseconds)
    {
        if (milliseconds <= 0)
            return "0 с";
        if (milliseconds % 1000 == 0)
            return milliseconds / 1000 + " с";
        return (milliseconds / 1000.0).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " с";
    }

    private void DrawDropMark(Graphics graphics)
    {
        if (_dropParent is null)
            return;

        NodeBox[] siblings = _nodes
            .Where(node => ReferenceEquals(node.Parent, _dropParent) && !ReferenceEquals(node.Block, _dragBlock))
            .OrderBy(node => node.Index)
            .ToArray();
        float x;
        float y;
        float height;
        if (siblings.Length == 0)
        {
            NodeBox? frame = _nodes.FirstOrDefault(node => node.IsRepeat && node.Block is RepeatBlock repeat && ReferenceEquals(repeat.Children, _dropParent));
            x = frame is null ? 24f : frame.Frame.Left + 20f;
            y = frame is null ? 24f : frame.Frame.Top + 78f;
            height = 70f;
        }
        else if (_dropIndex <= siblings[0].Index && _dropIndex <= siblings.Min(node => node.Index))
        {
            x = siblings[0].Frame.Left - Gap / 2f;
            y = siblings[0].Frame.Top;
            height = siblings[0].IsRepeat ? 62f : siblings[0].Frame.Height;
        }
        else
        {
            NodeBox anchor = siblings.LastOrDefault(node => node.Index < _dropIndex) ?? siblings[^1];
            bool after = anchor.Index < _dropIndex;
            x = after ? anchor.Frame.Right + Gap / 2f : anchor.Frame.Left - Gap / 2f;
            y = anchor.Frame.Top;
            height = anchor.IsRepeat ? 62f : anchor.Frame.Height;
        }

        using var brush = new SolidBrush(Color.FromArgb(220, 120, 186, 255));
        graphics.FillRectangle(brush, x - 1.5f, y + 8f, 3f, Math.Max(36f, height - 16f));
    }

    private void DrawChainDropMark(Graphics graphics)
    {
        StepBox[] siblings = _steps
            .Where(step => !ReferenceEquals(step.Step, _dragStep))
            .OrderBy(step => step.Index)
            .ToArray();
        float x;
        float y;
        float height;
        if (siblings.Length == 0)
        {
            x = 24f;
            y = 24f;
            height = 70f;
        }
        else if (_chainDropIndex <= siblings[0].Index)
        {
            x = siblings[0].Frame.Left - Gap / 2f;
            y = siblings[0].Frame.Top;
            height = siblings[0].Frame.Height;
        }
        else
        {
            StepBox anchor = siblings.LastOrDefault(step => step.Index < _chainDropIndex) ?? siblings[^1];
            bool after = anchor.Index < _chainDropIndex;
            x = after ? anchor.Frame.Right + Gap / 2f : anchor.Frame.Left - Gap / 2f;
            y = anchor.Frame.Top;
            height = anchor.Frame.Height;
        }

        using var brush = new SolidBrush(Color.FromArgb(220, 120, 186, 255));
        graphics.FillRectangle(brush, x - 1.5f, y + 8f, 3f, Math.Max(36f, height - 16f));
    }

    private void DrawNode(Graphics graphics, NodeBox node)
    {
        bool selected = _picked.Any(block => ReferenceEquals(block, node.Block));
        bool running = ReferenceEquals(node.Block, _runningBlock) || (_selectionRunning && selected);
        Color accent = BlockAppearance.Accent(node.Block);
        RectangleF frame = node.IsRepeat ? node.Frame : node.Header;

        if (node.IsRepeat)
        {
            using var frameBrush = new SolidBrush(Color.FromArgb(selected ? 46 : 32, accent));
            using var framePen = new Pen(Color.FromArgb(selected ? 210 : 120, accent), selected ? 1.8f : 1.2f);
            FillRound(graphics, frameBrush, frame, 16f);
            DrawRound(graphics, framePen, frame, 16f);
        }

        RectangleF card = node.Header;
        using var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
        FillRound(graphics, shadow, new RectangleF(card.X + 2f, card.Y + 5f, card.Width, card.Height), 14f);
        using var fill = new SolidBrush(selected ? Color.FromArgb(46, 50, 58) : Color.FromArgb(34, 37, 43));
        FillRound(graphics, fill, card, 14f);

        using var accentBrush = new SolidBrush(accent);
        graphics.SetClip(Round(card, 14f));
        graphics.FillRectangle(accentBrush, card.X, card.Y, 5f, card.Height);
        graphics.ResetClip();

        using var border = new Pen(running ? Color.FromArgb(255, 196, 92) : Color.FromArgb(selected ? 230 : 70, accent), running ? 2.2f : selected ? 1.6f : 1f);
        DrawRound(graphics, border, card, 14f);

        using var numberFill = new SolidBrush(Color.FromArgb(48, accent));
        using var numberText = new SolidBrush(accent);
        var numberBox = new RectangleF(card.X + 16f, card.Y + 14f, Math.Max(28f, node.Number.Length * 9f + 12f), 20f);
        FillRound(graphics, numberFill, numberBox, 8f);
        using var numberFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        graphics.DrawString(node.Number, numberFont, numberText, numberBox, Centered);

        using var titleBrush = new SolidBrush(Color.FromArgb(236, 238, 242));
        using var detailBrush = new SolidBrush(Color.FromArgb(164, 170, 180));
        using var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var detailFont = new Font("Segoe UI", 8.25f);
        float textX = card.X + 16f;
        graphics.DrawString(ShortTitle(node.Block), titleFont, titleBrush, new RectangleF(textX, card.Y + 38f, card.Width - 28f, 22f));
        graphics.DrawString(Detail(node.Block), detailFont, detailBrush, new RectangleF(textX, card.Y + 58f, card.Width - 28f, 20f));

        if (running)
        {
            using var runFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var runBrush = new SolidBrush(Color.FromArgb(255, 196, 92));
            graphics.DrawString("идёт", runFont, runBrush, card.Right - 46f, card.Y + 12f);
        }
    }

    private void DrawStep(Graphics graphics, StepBox box)
    {
        bool selected = ReferenceEquals(box.Step, _pickedStep);
        bool running = ReferenceEquals(box.Step, _runningStep);
        ChainStepInfo info = _stepInfo?.Invoke(box.Step) ?? new ChainStepInfo("Сценарий", "", true);
        Color accent = info.Missing ? Color.FromArgb(214, 120, 120) : ChainAccent;
        RectangleF card = box.Frame;

        using var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
        FillRound(graphics, shadow, new RectangleF(card.X + 2f, card.Y + 5f, card.Width, card.Height), 14f);
        using var fill = new SolidBrush(selected ? Color.FromArgb(46, 50, 58) : Color.FromArgb(34, 37, 43));
        FillRound(graphics, fill, card, 14f);

        using var accentBrush = new SolidBrush(accent);
        graphics.SetClip(Round(card, 14f));
        graphics.FillRectangle(accentBrush, card.X, card.Y, 5f, card.Height);
        graphics.ResetClip();

        using var border = new Pen(running ? Color.FromArgb(255, 196, 92) : Color.FromArgb(selected ? 230 : 70, accent), running ? 2.2f : selected ? 1.6f : 1f);
        DrawRound(graphics, border, card, 14f);

        using var numberFill = new SolidBrush(Color.FromArgb(48, accent));
        using var numberText = new SolidBrush(accent);
        var numberBox = new RectangleF(card.X + 16f, card.Y + 14f, Math.Max(28f, box.Number.Length * 9f + 12f), 20f);
        FillRound(graphics, numberFill, numberBox, 8f);
        using var numberFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        graphics.DrawString(box.Number, numberFont, numberText, numberBox, Centered);

        using var titleBrush = new SolidBrush(Color.FromArgb(236, 238, 242));
        using var detailBrush = new SolidBrush(info.Missing ? Color.FromArgb(214, 140, 140) : Color.FromArgb(164, 170, 180));
        using var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var detailFont = new Font("Segoe UI", 8.25f);
        float textX = card.X + 16f;
        graphics.DrawString(info.Title, titleFont, titleBrush, new RectangleF(textX, card.Y + 38f, card.Width - 28f, 22f));
        graphics.DrawString(info.Detail, detailFont, detailBrush, new RectangleF(textX, card.Y + 58f, card.Width - 28f, 20f));

        if (running)
        {
            using var runFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var runBrush = new SolidBrush(Color.FromArgb(255, 196, 92));
            graphics.DrawString("идёт", runFont, runBrush, card.Right - 46f, card.Y + 12f);
        }
    }

    private void DrawGrid(Graphics graphics)
    {
        float step = 26f * _zoom;
        if (step < 8f)
            return;
        float originX = _panX % step;
        float originY = _panY % step;
        using var brush = new SolidBrush(Color.FromArgb(38, 255, 255, 255));
        for (float x = originX; x < Width; x += step)
        {
            for (float y = originY; y < Height; y += step)
                graphics.FillEllipse(brush, x, y, 1.6f, 1.6f);
        }
    }

    private void DrawChrome(Graphics graphics)
    {
        using var title = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(230, 232, 236));
        string name = string.IsNullOrWhiteSpace(_caption)
            ? (_chain is null ? "Схема сценария" : "Схема цепочки")
            : _caption;
        if (_scenarioRunning || _runningStep is not null)
            name += "   ·   выполняется";
        graphics.DrawString(name, title, titleBrush, 18f, 10f);
    }

    private static void DrawMessage(Graphics graphics, string title, string detail)
    {
        using var titleFont = new Font("Segoe UI", 16f, FontStyle.Bold);
        using var detailFont = new Font("Segoe UI", 10f);
        using var titleBrush = new SolidBrush(Color.FromArgb(210, 214, 220));
        using var detailBrush = new SolidBrush(Color.FromArgb(140, 146, 156));
        var titleSize = graphics.MeasureString(title, titleFont);
        var detailSize = graphics.MeasureString(detail, detailFont);
        float x = Math.Max(24f, (graphics.VisibleClipBounds.Width - Math.Max(titleSize.Width, detailSize.Width)) / 2f);
        float y = graphics.VisibleClipBounds.Height / 2f - 28f;
        graphics.DrawString(title, titleFont, titleBrush, x, y);
        graphics.DrawString(detail, detailFont, detailBrush, x, y + 34f);
    }

    private NodeBox? Hit(Point location)
    {
        PointF world = ToWorld(location);
        for (int i = _nodes.Count - 1; i >= 0; i--)
        {
            if (_nodes[i].Header.Contains(world))
                return _nodes[i];
        }

        return null;
    }

    private StepBox? HitStep(Point location)
    {
        PointF world = ToWorld(location);
        for (int i = _steps.Count - 1; i >= 0; i--)
        {
            if (_steps[i].Frame.Contains(world))
                return _steps[i];
        }

        return null;
    }

    private PointF ToWorld(Point location)
        => new((location.X - _panX) / _zoom, (location.Y - _panY) / _zoom);

    private static bool HasBlockKind(IDataObject? data)
        => data?.GetDataPresent(DataFormats.Text) == true || data?.GetDataPresent(DataFormats.UnicodeText) == true;

    private static string? ReadKind(IDataObject? data)
        => data?.GetData(DataFormats.UnicodeText) as string ?? data?.GetData(DataFormats.Text) as string;

    public static string? ReadScenarioRef(IDataObject? data)
    {
        string? text = ReadKind(data);
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith(ScenarioRefPrefix, StringComparison.Ordinal))
            return null;
        string path = text[ScenarioRefPrefix.Length..].Trim();
        return path.Length == 0 ? null : path;
    }

    private static bool ContainsList(ScenarioBlock block, List<ScenarioBlock> list)
    {
        if (block is not RepeatBlock repeat)
            return false;
        if (ReferenceEquals(repeat.Children, list))
            return true;
        foreach (ScenarioBlock child in repeat.Children)
        {
            if (ContainsList(child, list))
                return true;
        }

        return false;
    }

    private static string RepeatCaption(RepeatCondition condition)
        => condition switch
        {
            RepeatCondition.WhileRed => "пока есть красный",
            _ => "пока есть красный",
        };

    private static string ShortTitle(ScenarioBlock block)
        => block switch
        {
            FollowBlock => "Следовать маршруту",
            LockBlock => "Зафиксироваться",
            DoorBlock => "Войти в дверь",
            OpenChestBlock => "Открыть сундук",
            ClickQuestMarkBlock => "Клик по знаку",
            AcceptDialogBlock => "Принять диалог",
            MountHorsesBlock => "По кОням!",
            UseMapBlock => "Сменить карту",
            SequenceBlock => "Нажатия",
            RepeatBlock => "Повтор",
            _ => block.Title,
        };

    private static string FollowDetail(FollowBlock follow)
    {
        string mode = follow.Travel == TravelMode.Shortest
            ? $"Кратчайший путь  ·  {follow.Anchors.Count} точек"
            : $"По векторам  ·  {follow.Anchors.Count} точек";
        string lost = follow.ChaseLostMilliseconds <= 0
            ? "пропажа выкл"
            : $"пропажа {follow.ChaseLostMilliseconds} мс";
        mode += "  ·  " + lost;
        if (string.IsNullOrWhiteSpace(follow.MapFile))
            return mode;
        return mode + "  ·  " + Path.GetFileNameWithoutExtension(follow.MapFile);
    }

    private static string Detail(ScenarioBlock block)
        => block switch
        {
            FollowBlock follow => FollowDetail(follow),
            LockBlock locked => $"{(locked.Target == AnchorSource.Chest ? "Сундук" : "Красный")}  ·  радиус {locked.RadiusPixels} px  ·  ожидание {locked.WaitMilliseconds} мс  ·  {locked.Keys.Count} кл.",
            DoorBlock door => $"Ближайшая дверь  ·  {door.DifficultyName}",
            OpenChestBlock chest => $"Ближайший сундук  ·  ожидание {chest.WaitMilliseconds} мс",
            ClickQuestMarkBlock quest => quest.Mark == AnchorSource.QuestExclaim ? "Ближайший знак !" : "Ближайший знак ?",
            AcceptDialogBlock => "Зелёная галочка диалога",
            MountHorsesBlock mount => $"Клавиша O каждые {mount.KeyIntervalMilliseconds} мс",
            UseMapBlock useMap => string.IsNullOrWhiteSpace(useMap.MapFile)
                ? "Выберите карту сценария"
                : Path.GetFileNameWithoutExtension(useMap.MapFile),
            SequenceBlock sequence => sequence.Clicks.Count == 0 ? "Клики в окне игры" : $"{sequence.Clicks.Count} нажатий",
            RepeatBlock repeat => repeat.Children.Count == 0
                ? "Перетащите блоки внутрь"
                : $"{repeat.Children.Count} внутри, {RepeatCaption(repeat.Condition)}, ждать {repeat.WaitMilliseconds} мс",
            _ => "",
        };

    private static readonly StringFormat Centered = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
    };

    private static GraphicsPath Round(RectangleF rectangle, float radius)
    {
        float diameter = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void FillRound(Graphics graphics, Brush brush, RectangleF rectangle, float radius)
    {
        using GraphicsPath path = Round(rectangle, radius);
        graphics.FillPath(brush, path);
    }

    private static void DrawRound(Graphics graphics, Pen pen, RectangleF rectangle, float radius)
    {
        using GraphicsPath path = Round(rectangle, radius);
        graphics.DrawPath(pen, path);
    }

    private sealed class NodeBox
    {
        public ScenarioBlock Block { get; init; } = null!;

        public List<ScenarioBlock> Parent { get; init; } = null!;

        public int Index { get; init; }

        public int Depth { get; init; }

        public string Number { get; init; } = "";

        public bool IsRepeat { get; set; }

        public RectangleF Header { get; set; }

        public RectangleF Frame { get; set; }
    }

    private sealed class StepBox
    {
        public ChainStep Step { get; init; } = null!;

        public int Index { get; init; }

        public string Number { get; init; } = "";

        public RectangleF Frame { get; set; }
    }

    private sealed class PauseBadge
    {
        public ScenarioBlock Block { get; init; } = null!;

        public RectangleF Bounds { get; init; }

        public PointF Start { get; init; }

        public PointF Control1 { get; init; }

        public PointF Control2 { get; init; }

        public PointF End { get; init; }
    }

    private sealed class StepPauseBadge
    {
        public ChainStep Step { get; init; } = null!;

        public RectangleF Bounds { get; init; }

        public PointF Start { get; init; }

        public PointF Control1 { get; init; }

        public PointF Control2 { get; init; }

        public PointF End { get; init; }
    }
}

internal static class BlockAppearance
{
    public static Color Accent(ScenarioBlock block)
        => block switch
        {
            FollowBlock => Color.FromArgb(72, 214, 154),
            LockBlock => Color.FromArgb(232, 104, 104),
            DoorBlock => Color.FromArgb(64, 196, 214),
            OpenChestBlock => Color.FromArgb(214, 168, 72),
            ClickQuestMarkBlock => Color.FromArgb(120, 214, 120),
            AcceptDialogBlock => Color.FromArgb(96, 196, 96),
            MountHorsesBlock => Color.FromArgb(72, 184, 232),
            UseMapBlock => Color.FromArgb(160, 140, 220),
            SequenceBlock => Color.FromArgb(96, 164, 255),
            RepeatBlock => Color.FromArgb(232, 176, 84),
            _ => Color.White,
        };

    public static Color Accent(string kind)
        => kind switch
        {
            "follow" => Color.FromArgb(72, 214, 154),
            "lock" => Color.FromArgb(232, 104, 104),
            "door" => Color.FromArgb(64, 196, 214),
            "openChest" => Color.FromArgb(214, 168, 72),
            "clickQuestMark" => Color.FromArgb(120, 214, 120),
            "acceptDialog" => Color.FromArgb(96, 196, 96),
            "mountHorses" => Color.FromArgb(72, 184, 232),
            "useMap" => Color.FromArgb(160, 140, 220),
            "sequence" => Color.FromArgb(96, 164, 255),
            "repeat" => Color.FromArgb(232, 176, 84),
            _ => Color.White,
        };
}
