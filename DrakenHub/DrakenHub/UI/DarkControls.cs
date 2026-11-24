using System.ComponentModel;
using System.Drawing.Drawing2D;
using DrakenHub.Core;
using DrakenHub.Models;
using DrakenHub.Services;

namespace DrakenHub.UI;

/// <summary>Tiny tile showing the rarity "?" glyph used by color scan matching.</summary>
internal sealed class QuestionMarkPreview : Control
{
    private readonly Color _color;
    private readonly bool _available;

    public QuestionMarkPreview(Color color, bool available = true)
    {
        _color = color;
        _available = available;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(28, 28);
        Margin = Padding.Empty;
        BackColor = Theme.Panel;
    }

    protected override void OnPaint(PaintEventArgs e)
        => QuestionMarkTemplates.DrawPreview(e.Graphics, ClientRectangle, _color, _available);
}

/// <summary>Thumbnail of a captured gem. Transparent pixels stay dark.</summary>
internal sealed class ItemIconPreview : Control
{
    private readonly Bitmap? _bmp;
    private bool _dim;
    private bool _marked;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dim
    {
        get => _dim;
        set { if (_dim == value) return; _dim = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Marked
    {
        get => _marked;
        set { if (_marked == value) return; _marked = value; Invalidate(); }
    }

    public ItemIconPreview(string? pngBase64)
    {
        _bmp = ItemIconMatcher.DecodePreview(pngBase64);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(36, 32);
        Margin = Padding.Empty;
        BackColor = Theme.InputBg;
        Cursor = Cursors.Hand;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bmp?.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(Theme.InputBg))
            g.FillRectangle(bg, ClientRectangle);
        if (_bmp != null)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_bmp, new Rectangle(2, 2, Width - 4, Height - 4));
        }

        if (_dim)
        {
            using var wash = new SolidBrush(Color.FromArgb(150, Theme.InputBg));
            g.FillRectangle(wash, ClientRectangle);
        }

        using (var pen = new Pen(_marked ? Theme.Accent : Theme.Border))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        if (_marked)
            DrawCheck(g, ClientRectangle);
    }

    /// <summary>Selection mark in the empty bottom-right corner of an icon.</summary>
    public static void DrawCheck(Graphics g, Rectangle host)
    {
        const int mark = 15;
        var box = new Rectangle(host.Right - mark - 3, host.Bottom - mark - 3, mark, mark);
        using (var markBg = new SolidBrush(Theme.Accent))
            g.FillRectangle(markBg, box);
        var smooth = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var tick = new Pen(Theme.Bg, 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        g.DrawLines(tick,
        [
            new Point(box.X + 3, box.Y + 8),
            new Point(box.X + 6, box.Y + 11),
            new Point(box.X + 12, box.Y + 4)
        ]);
        g.SmoothingMode = smooth;
    }
}

/// <summary>Flat button that draws its own caption. A system Button shows an overflow chevron when the text is tight.</summary>
internal sealed class MiniButton : Control
{
    private readonly BtnKind _kind;
    private bool _hover;
    private Size _locked;

    public MiniButton(string text, BtnKind kind)
    {
        Text = text;
        _kind = kind;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        Font = Theme.UiFont;
        Margin = Padding.Empty;
    }

    public void SetSize(int width, int height)
    {
        _locked = new Size(Math.Max(1, width), Math.Max(1, height));
        MinimumSize = Size.Empty;
        MaximumSize = Size.Empty;
        MinimumSize = _locked;
        MaximumSize = _locked;
        Size = _locked;
    }

    public override Size GetPreferredSize(Size proposedSize)
        => _locked.Width > 0 ? _locked : Size;

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (_locked.Width > 0)
        {
            width = _locked.Width;
            height = _locked.Height;
        }
        base.SetBoundsCore(x, y, width, height, specified);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var back = _kind == BtnKind.Danger ? Theme.DangerDim : Theme.Button;
        var border = _kind == BtnKind.Danger ? Theme.Danger : Theme.ButtonBorder;
        var fore = _kind == BtnKind.Danger ? Theme.Danger : Theme.Text;
        if (_hover && _kind != BtnKind.Danger)
            back = Theme.NavHover;
        using (var brush = new SolidBrush(back))
            g.FillRectangle(brush, ClientRectangle);
        using (var pen = new Pen(border))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
}

/// <summary>One sellable item: icon toggles it, the name and the capture stay on the card.</summary>
internal sealed class ItemTile : Panel
{
    public const string DragFormat = "DrakenHub.CustomItem";

    private readonly CustomItemProfile _item;
    private readonly ItemIconPreview _icon;
    private readonly TextBox _name;
    private readonly MiniButton _capture;
    private readonly MiniButton _remove;
    private Size _size;
    private Point? _down;
    private bool _dragged;

    public ItemTile(CustomItemProfile item, Localization loc, Action toggle, Action capture, Action delete, Action<string> nameChanged, Action nameCommit)
    {
        _item = item;
        Font = Theme.UiFont;
        Margin = new Padding(0, 0, 6, 6);
        BackColor = Theme.PanelAlt;

        _icon = new ItemIconPreview(item.IconPng);
        ArmDrag(_icon, toggle);

        _name = UiFactory.Tb(item.Name);
        _name.TextChanged += (_, _) => nameChanged(_name.Text);
        _name.Leave += (_, _) => nameCommit();

        _capture = new MiniButton(loc.T("btn.captureShort"), BtnKind.Neutral);
        _capture.Click += (_, _) => capture();
        _remove = new MiniButton("×", BtnKind.Danger);
        _remove.Click += (_, _) => delete();

        Controls.Add(_icon);
        Controls.Add(_name);
        Controls.Add(_capture);
        Controls.Add(_remove);

        Place(loc.T("btn.captureShort"));
        ApplyState();
        Paint += (_, e) =>
        {
            using var pen = new Pen(_item.Enabled ? Theme.Accent : Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };
    }

    /// <summary>A short move starts a drag onto another group. A click still toggles the item.</summary>
    private void ArmDrag(Control source, Action toggle)
    {
        source.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _down = e.Location;
            _dragged = false;
        };
        source.MouseMove += (_, e) =>
        {
            if (_dragged || _down == null || e.Button != MouseButtons.Left) return;
            var dx = Math.Abs(e.X - _down.Value.X);
            var dy = Math.Abs(e.Y - _down.Value.Y);
            if (dx < SystemInformation.DragSize.Width && dy < SystemInformation.DragSize.Height) return;
            _dragged = true;
            _down = null;
            DoDragDrop(new DataObject(DragFormat, _item.Id), DragDropEffects.Move);
        };
        source.MouseUp += (_, e) =>
        {
            var dragged = _dragged;
            _dragged = false;
            _down = null;
            if (!dragged && e.Button == MouseButtons.Left)
                toggle();
        };
    }

    public void ApplyLanguage(Localization loc, ToolTip tips)
    {
        var caption = loc.T("btn.captureShort");
        _capture.Text = caption;
        Place(caption);
        tips.SetToolTip(_capture, loc.T("item.captureHint"));
        tips.SetToolTip(_icon, loc.T("item.iconHint"));
        tips.SetToolTip(_remove, loc.T("delete"));
    }

    /// <summary>Size the card from the caption so the word is fully visible at the current DPI.</summary>
    private void Place(string captureText)
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var capW = TextRenderer.MeasureText(captureText, Theme.UiFont, Size.Empty, flags).Width + 22;
        var delW = TextRenderer.MeasureText("×", Theme.UiFont, Size.Empty, flags).Width + 18;
        var btnH = Math.Max(26, Theme.UiFont.Height + 10);
        const int pad = 8;
        const int gap = 6;
        var width = Math.Max(104, pad + capW + gap + delW + pad);
        const int icon = 56;
        var nameH = Theme.UiFont.Height + 8;
        var y = pad;
        _icon.SetBounds((width - icon) / 2, y, icon, icon);
        y += icon + 6;
        _name.SetBounds(pad, y, width - pad * 2, nameH);
        y += nameH + 6;
        _capture.SetSize(capW, btnH);
        _capture.Location = new Point(pad, y);
        _remove.SetSize(delW, btnH);
        _remove.Location = new Point(pad + capW + gap, y);
        y += btnH + pad;
        _size = new Size(width, y);
        MinimumSize = Size.Empty;
        MaximumSize = Size.Empty;
        MinimumSize = _size;
        MaximumSize = _size;
        Size = _size;
    }

    public override Size GetPreferredSize(Size proposedSize)
        => _size.Width > 0 ? _size : Size;

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (_size.Width > 0)
        {
            width = _size.Width;
            height = _size.Height;
        }
        base.SetBoundsCore(x, y, width, height, specified);
    }

    public void ApplyState()
    {
        _icon.Marked = _item.Enabled;
        _icon.Dim = !_item.Enabled;
        Invalidate();
    }
}

/// <summary>Built-in rarity template. The icon toggles it; the name cannot be edited.</summary>
internal sealed class SystemTile : Panel
{
    private readonly RarityProfile _rarity;
    private readonly Func<string> _title;
    private readonly Label _name;
    private Rectangle _icon;
    private Size _size;

    public SystemTile(RarityProfile rarity, Func<string> title, Action toggle)
    {
        _rarity = rarity;
        _title = title;
        Font = Theme.UiFont;
        Margin = new Padding(0, 0, 6, 6);
        BackColor = Theme.PanelAlt;
        Cursor = Cursors.Hand;

        _name = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Text,
            BackColor = Theme.PanelAlt,
            Font = Theme.UiFont,
            Cursor = Cursors.Hand,
            Text = title()
        };
        _name.Click += (_, _) => toggle();
        Click += (_, _) => toggle();
        Controls.Add(_name);
        Place();
    }

    public void ApplyLanguage()
    {
        _name.Text = _title();
        Place();
    }

    public void ApplyState() => Invalidate();

    private void Place()
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var textW = TextRenderer.MeasureText(_name.Text, Theme.UiFont, Size.Empty, flags).Width + 8;
        const int pad = 8;
        const int icon = 56;
        var width = Math.Max(icon + pad * 2, textW + pad * 2);
        var nameH = Theme.UiFont.Height + 8;
        var y = pad;
        _icon = new Rectangle((width - icon) / 2, y, icon, icon);
        y += icon + 6;
        _name.SetBounds(pad, y, width - pad * 2, nameH);
        y += nameH + pad;
        _size = new Size(width, y);
        MinimumSize = Size.Empty;
        MaximumSize = Size.Empty;
        MinimumSize = _size;
        MaximumSize = _size;
        Size = _size;
    }

    public override Size GetPreferredSize(Size proposedSize)
        => _size.Width > 0 ? _size : Size;

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (_size.Width > 0)
        {
            width = _size.Width;
            height = _size.Height;
        }
        base.SetBoundsCore(x, y, width, height, specified);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var template = QuestionMarkTemplates.Find(_rarity.Id);
        var color = template?.DisplayColor ?? Color.FromArgb(_rarity.DisplayColorArgb);
        QuestionMarkTemplates.DrawPreview(g, _icon, color, _rarity.Enabled);
        if (!_rarity.Enabled)
        {
            using var wash = new SolidBrush(Color.FromArgb(150, Theme.PanelAlt));
            g.FillRectangle(wash, _icon);
        }

        using (var pen = new Pen(_rarity.Enabled ? Theme.Accent : Theme.Border))
        {
            if (_icon.Width > 0)
                g.DrawRectangle(pen, _icon.X, _icon.Y, _icon.Width - 1, _icon.Height - 1);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        if (_rarity.Enabled && _icon.Width > 0)
            ItemIconPreview.DrawCheck(g, _icon);
    }
}

/// <summary>
/// One group on the strip above the item grid. Click selects it, double-click renames it,
/// the cross on the selected chip deletes it when more than one group exists.
/// </summary>
internal sealed class GroupChip : Control
{
    private static int ChipHeight => Math.Max(28, Theme.UiFont.Height + 12);
    private static int DeleteWidth => Math.Max(16, Theme.UiFont.Height);

    private readonly TextBox _edit;
    private string _name = "";
    private int _width;
    private bool _selected;
    private bool _hover;
    private bool _allowDelete;
    private bool _allowRename = true;
    private bool _allowDrop;
    private bool _dropHot;
    private bool _deleteArmed;
    private bool _committing;

    public event EventHandler? Chosen;
    public event EventHandler? DeleteRequested;
    public event EventHandler<string>? Renamed;
    public event EventHandler<string>? ItemDropped;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string GroupId { get; set; } = "";

    public GroupChip()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Height = ChipHeight;
        MinimumSize = new Size(36, ChipHeight);
        Margin = new Padding(0, 0, 6, 6);
        Cursor = Cursors.Hand;
        Font = Theme.UiFont;
        BackColor = Theme.InputBg;

        _edit = new TextBox
        {
            Visible = false,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.InputBg,
            ForeColor = Theme.Text,
            Font = Theme.UiFont,
            TabStop = false
        };
        _edit.Leave += (_, _) => CommitEdit();
        _edit.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                CommitEdit();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                CancelEdit();
            }
        };
        Controls.Add(_edit);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsSelected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowDelete
    {
        get => _allowDelete;
        set
        {
            if (_allowDelete == value) return;
            _allowDelete = value;
            FitWidth();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowRename
    {
        get => _allowRename;
        set => _allowRename = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowItemDrop
    {
        get => _allowDrop;
        set
        {
            _allowDrop = value;
            AllowDrop = value;
        }
    }

    public void SetTitle(string name)
    {
        _name = name;
        if (!_edit.Visible) _edit.Text = name;
        FitWidth();
    }

    public override Size GetPreferredSize(Size proposedSize)
        => new(_width > 0 ? _width : Math.Max(Width, 56), ChipHeight);

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (_width > 0) width = _width;
        base.SetBoundsCore(x, y, Math.Max(56, width), ChipHeight, BoundsSpecified.All);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var bg = _dropHot ? Theme.AccentDim : _selected ? Theme.NavActive : (_hover ? Theme.NavHover : Theme.InputBg);
        using (var brush = new SolidBrush(bg))
            g.FillRectangle(brush, ClientRectangle);
        using (var pen = new Pen(_dropHot || _selected || _hover ? Theme.Accent : Theme.Border))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        if (_edit.Visible) return;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        var textRect = new Rectangle(8, 0, Math.Max(8, Width - 16 - (_allowDelete ? DeleteWidth + 2 : 0)), Height);
        TextRenderer.DrawText(g, _name, Font, textRect,
            _selected ? Theme.Accent : Theme.Text,
            flags | TextFormatFlags.HorizontalCenter);

        if (!_allowDelete || !_selected) return;
        TextRenderer.DrawText(g, "×", Font, DeleteBounds, Theme.Danger,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_allowDelete && _selected && DeleteBounds.Contains(e.Location))
        {
            _deleteArmed = true;
            DeleteRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _deleteArmed = false;
        Chosen?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        if (_deleteArmed || !_allowRename) return;
        BeginEdit();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        AcceptDrop(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        AcceptDrop(e);
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        if (!_dropHot) return;
        _dropHot = false;
        Invalidate();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        _dropHot = false;
        Invalidate();
        if (e.Data?.GetData(ItemTile.DragFormat) is string id && id.Length > 0)
            ItemDropped?.Invoke(this, id);
    }

    private void AcceptDrop(DragEventArgs e)
    {
        var ok = _allowDrop && e.Data?.GetDataPresent(ItemTile.DragFormat) == true;
        e.Effect = ok ? DragDropEffects.Move : DragDropEffects.None;
        if (_dropHot == ok) return;
        _dropHot = ok;
        Invalidate();
    }

    private Rectangle DeleteBounds => new(Width - DeleteWidth - 5, 4, DeleteWidth, Height - 8);

    private void FitWidth()
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var text = TextRenderer.MeasureText(_name, Font, Size.Empty, flags).Width + 22;
        if (_allowDelete) text += DeleteWidth + 6;
        _width = Math.Clamp(text, 72, 360);
        MinimumSize = Size.Empty;
        MaximumSize = Size.Empty;
        MinimumSize = new Size(_width, ChipHeight);
        MaximumSize = new Size(_width, ChipHeight);
        Size = new Size(_width, ChipHeight);
        Invalidate();
    }

    private void BeginEdit()
    {
        var right = _allowDelete ? DeleteWidth + 8 : 8;
        _edit.SetBounds(8, 4, Math.Max(48, Width - 8 - right), Math.Max(16, ChipHeight - 8));
        _edit.Text = _name;
        _edit.Visible = true;
        _edit.Focus();
        _edit.SelectAll();
        Invalidate();
    }

    private void CommitEdit()
    {
        if (_committing || !_edit.Visible) return;
        _committing = true;
        try
        {
            _edit.Visible = false;
            Renamed?.Invoke(this, _edit.Text.Trim());
        }
        finally
        {
            _committing = false;
        }
    }

    private void CancelEdit()
    {
        _committing = true;
        try
        {
            _edit.Text = _name;
            _edit.Visible = false;
            Invalidate();
        }
        finally
        {
            _committing = false;
        }
    }
}

/// <summary>Compact digit toggle for inventory tabs 1–9.</summary>
internal sealed class TabDigitToggle : Control
{
    private bool _on;
    private bool _hover;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool IsOn
    {
        get => _on;
        set { if (_on == value) return; _on = value; Invalidate(); }
    }

    public event EventHandler? IsOnChanged;

    public TabDigitToggle(int digit)
    {
        Text = digit.ToString();
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable |
            ControlStyles.FixedWidth | ControlStyles.FixedHeight, true);
        Size = new Size(30, 30);
        MinimumSize = new Size(30, 30);
        MaximumSize = new Size(30, 30);
        Cursor = Cursors.Hand;
        Font = Theme.UiFontValue;
        BackColor = Theme.InputBg;
        TabStop = true;
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        base.SetBoundsCore(x, y, 30, 30, specified);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var bg = _on ? Theme.NavActive : (_hover ? Theme.NavHover : Theme.InputBg);
        using (var brush = new SolidBrush(bg))
            g.FillRectangle(brush, ClientRectangle);
        using (var pen = new Pen(_on || _hover ? Theme.Accent : Theme.Border))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle,
            _on ? Theme.Accent : Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        IsOn = !IsOn;
        IsOnChanged?.Invoke(this, EventArgs.Empty);
        Focus();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
}

/// <summary>Full-width rarity pick row: ? preview + name + on/off accent.</summary>
internal sealed class RarityPickRow : Control
{
    private readonly QuestionMarkTemplate? _template;
    private readonly Func<string> _title;
    private bool _on;
    private bool _hover;
    private bool _enabled = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool IsOn
    {
        get => _on;
        set { if (_on == value) return; _on = value; Invalidate(); }
    }

    public event EventHandler? IsOnChanged;

    public RarityPickRow(QuestionMarkTemplate? template, Func<string> title, bool on)
    {
        _template = template;
        _title = title;
        _on = on;
        _enabled = template != null;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Height = 36;
        Cursor = _enabled ? Cursors.Hand : Cursors.Default;
        BackColor = Theme.PanelAlt;
        Margin = new Padding(0, 0, 0, 4);
    }

    public void RefreshTitle() => Invalidate();

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var fill = _hover && _enabled ? Theme.NavHover : Theme.PanelAlt;
        using (var bg = new SolidBrush(fill))
            g.FillRectangle(bg, ClientRectangle);
        using (var pen = new Pen(_on && _enabled ? Theme.Accent : Theme.Border))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        var preview = new Rectangle(8, 4, 28, 28);
        var color = _template?.DisplayColor ?? Theme.TextMuted;
        QuestionMarkTemplates.DrawPreview(g, preview, color, _enabled);

        var titleRect = new Rectangle(44, 0, Math.Max(40, Width - 88), Height);
        TextRenderer.DrawText(g, _title(), Theme.UiFont, titleRect,
            _enabled ? Theme.Text : Theme.TextSubtle,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var mark = _on && _enabled ? "✓" : "";
        var markRect = new Rectangle(Width - 36, 0, 28, Height);
        TextRenderer.DrawText(g, mark, Theme.UiFontValue, markRect, Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (!_enabled) return;
        IsOn = !IsOn;
        IsOnChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
}

/// <summary>Sidebar section header with a hairline separator above the title.</summary>
internal sealed class NavSectionHeader : Control
{
    private string _title = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string Title
    {
        get => _title;
        set { _title = value ?? ""; Invalidate(); }
    }

    public NavSectionHeader(string title)
    {
        _title = title;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Height = 34;
        Margin = new Padding(0, 12, 0, 4);
        BackColor = Theme.Panel;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(Theme.Panel))
            g.FillRectangle(bg, ClientRectangle);

        using (var pen = new Pen(Theme.Border))
            g.DrawLine(pen, 12, 6, Width - 12, 6);

        var text = _title.ToUpperInvariant();
        var rect = new Rectangle(14, 10, Math.Max(0, Width - 28), Height - 12);
        TextRenderer.DrawText(g, text, Theme.UiFontSmall, rect, Theme.TextSubtle,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Sidebar nav item with full-width hit area and accent rail when active.</summary>
internal sealed class NavItemButton : Button
{
    private bool _active;
    private bool _muted;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool IsActive
    {
        get => _active;
        set { _active = value; ApplyChrome(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool IsMuted
    {
        get => _muted;
        set { _muted = value; ApplyChrome(); }
    }

    public NavItemButton(string text)
    {
        Text = text;
        FlatStyle = FlatStyle.Flat;
        Height = 34;
        Font = Theme.UiFontNav;
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(16, 0, 10, 0);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        Margin = new Padding(0, 0, 0, 1);
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Theme.NavHover;
        ApplyChrome();
        Paint += OnPaintAccent;
    }

    private void ApplyChrome()
    {
        BackColor = _active ? Theme.NavActive : Theme.NavIdle;
        ForeColor = _muted ? Theme.TextSubtle : (_active ? Theme.Accent : Theme.Text);
        Cursor = _muted ? Cursors.Default : Cursors.Hand;
        Enabled = !_muted;
        Invalidate();
    }

    private void OnPaintAccent(object? sender, PaintEventArgs e)
    {
        if (!_active) return;
        using var accent = new SolidBrush(Theme.Accent);
        e.Graphics.FillRectangle(accent, 0, 4, 3, Height - 8);
    }
}

/// <summary>Panel that redraws cleanly on resize (no smear / barcode artifacts).</summary>
internal class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        UpdateStyles();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var bg = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(bg, ClientRectangle);
    }
}

internal sealed class DarkCheckBox : CheckBox
{
    private const int BoxSize = 16;
    private const int BoxGap = 8;

    public DarkCheckBox()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;
        AutoSize = true;
        Cursor = Cursors.Hand;
        TabStop = true;
        // WinForms CheckBox + UserPaint often reports PreferredSize=0; force a real size.
        Size = MeasurePreferred();
    }

    public override Size GetPreferredSize(Size proposedSize) => MeasurePreferred();

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (AutoSize)
            Size = MeasurePreferred();
        else
            Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (AutoSize)
            Size = MeasurePreferred();
    }

    private Size MeasurePreferred()
    {
        var text = string.IsNullOrEmpty(Text) ? " " : Text;
        var textSize = TextRenderer.MeasureText(text, Font ?? Theme.UiFont, new Size(int.MaxValue, 28),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var w = 2 + BoxSize + BoxGap + textSize.Width + 8;
        return new Size(Math.Max(40, w), 28);
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (AutoSize)
        {
            var pref = MeasurePreferred();
            width = pref.Width;
            height = pref.Height;
        }
        else
        {
            height = Math.Max(28, height);
            width = Math.Max(40, width);
        }
        base.SetBoundsCore(x, y, width, height, specified);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        var bg = Theme.Panel;
        if (Parent is { BackColor.A: 255 } p)
            bg = p.BackColor;
        using (var brush = new SolidBrush(bg))
            g.FillRectangle(brush, ClientRectangle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var boxY = (Height - BoxSize) / 2;
        var box = new Rectangle(2, boxY, BoxSize, BoxSize);

        using (var boxBg = new SolidBrush(Theme.InputBg))
            g.FillRectangle(boxBg, box);
        using (var border = new Pen(Checked ? Theme.Accent : Theme.Border))
            g.DrawRectangle(border, box.Left, box.Top, box.Width - 1, box.Height - 1);

        if (Checked)
        {
            using var accent = new Pen(Theme.Accent, 2f);
            accent.StartCap = LineCap.Round;
            accent.EndCap = LineCap.Round;
            g.DrawLines(accent,
            [
                new Point(box.Left + 4, box.Top + 8),
                new Point(box.Left + 7, box.Top + 11),
                new Point(box.Left + 13, box.Top + 5)
            ]);
        }

        var textX = box.Right + BoxGap;
        var textBounds = new Rectangle(textX, 0, Math.Max(0, Width - textX - 2), Height);
        var textColor = Enabled ? ForeColor : Theme.TextMuted;
        TextRenderer.DrawText(g, Text, Font, textBounds, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused)
        {
            var focus = new Rectangle(0, 0, Width - 1, Height - 1);
            ControlPaint.DrawFocusRectangle(g, focus);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Invalidate();
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }
}

internal sealed class DarkCombo : ComboBox
{
    private const int WmPaint = 0x000F;
    private bool _painting;

    public DarkCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.InputBg;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;
        ItemHeight = 22;
        IntegralHeight = false;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WmPaint || _painting || DroppedDown) return;
        _painting = true;
        try { PaintClosedFace(); }
        finally { _painting = false; }
    }

    private void PaintClosedFace()
    {
        using var g = Graphics.FromHwnd(Handle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        using (var bg = new SolidBrush(Theme.InputBg))
            g.FillRectangle(bg, rect);
        using (var pen = new Pen(Focused ? Theme.Accent : Theme.Border))
            g.DrawRectangle(pen, 0, 0, rect.Width - 1, rect.Height - 1);

        var text = SelectedItem?.ToString() ?? Text ?? "";
        var textRect = new Rectangle(6, 0, Math.Max(0, rect.Width - 22), rect.Height);
        TextRenderer.DrawText(g, text, Font, textRect, Enabled ? Theme.Text : Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        var cx = rect.Right - 11;
        var cy = rect.Height / 2;
        using var arrow = new Pen(Theme.TextMuted, 1.6f);
        arrow.StartCap = LineCap.Round;
        arrow.EndCap = LineCap.Round;
        g.DrawLines(arrow,
        [
            new Point(cx - 4, cy - 2),
            new Point(cx, cy + 2),
            new Point(cx + 4, cy - 2)
        ]);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            e.DrawBackground();
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        using var bg = new SolidBrush(selected ? Theme.NavActive : Theme.InputBg);
        e.Graphics.FillRectangle(bg, e.Bounds);
        var text = Items[e.Index]?.ToString() ?? "";
        var pad = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, Font, pad, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }
}

/// <summary>Compact themed drop-down (no system ComboBox chrome) for short option lists.</summary>
internal sealed class DarkSelect : Control
{
    private const int BoxW = 48;
    private readonly string[] _items;
    private int _selectedIndex;
    private string _caption = "";
    private bool _hover;
    private ContextMenuStrip? _menu;

    public event EventHandler? SelectedIndexChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var v = Math.Clamp(value, 0, Math.Max(0, _items.Length - 1));
            if (v == _selectedIndex) return;
            _selectedIndex = v;
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string Caption
    {
        get => _caption;
        set
        {
            _caption = value ?? "";
            Width = MeasurePreferredWidth();
            Invalidate();
        }
    }

    public string SelectedText =>
        _selectedIndex >= 0 && _selectedIndex < _items.Length ? _items[_selectedIndex] : "";

    public DarkSelect(params string[] items)
    {
        _items = items.Length > 0 ? items : [""];
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        Height = 28;
        Font = Theme.UiFont;
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Theme.PanelAlt;
        Width = MeasurePreferredWidth();
    }

    public int MeasurePreferredWidth()
    {
        if (string.IsNullOrEmpty(_caption))
            return BoxW;
        var capW = TextRenderer.MeasureText(_caption, Theme.UiFont).Width;
        return capW + 8 + BoxW;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.PanelAlt))
            g.FillRectangle(bg, ClientRectangle);

        var boxX = Math.Max(0, Width - BoxW);
        if (!string.IsNullOrEmpty(_caption))
        {
            var capRect = new Rectangle(0, 0, boxX - 6, Height);
            TextRenderer.DrawText(g, _caption, Theme.UiFont, capRect, Theme.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        var box = new Rectangle(boxX, 1, BoxW - 1, Height - 2);
        using (var inputBg = new SolidBrush(Theme.InputBg))
            g.FillRectangle(inputBg, box);
        using (var pen = new Pen(Focused || _hover ? Theme.Accent : Theme.Border))
            g.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1);

        var textRect = new Rectangle(box.X + 6, box.Y, Math.Max(0, box.Width - 22), box.Height);
        TextRenderer.DrawText(g, SelectedText, Font, textRect, Enabled ? Theme.Text : Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        var cx = box.Right - 11;
        var cy = box.Y + box.Height / 2;
        using var arrow = new Pen(Theme.TextMuted, 1.6f);
        arrow.StartCap = LineCap.Round;
        arrow.EndCap = LineCap.Round;
        g.DrawLines(arrow,
        [
            new Point(cx - 4, cy - 2),
            new Point(cx, cy + 2),
            new Point(cx + 4, cy - 2)
        ]);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        ShowMenu();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter or Keys.F4)
        {
            ShowMenu();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Up && _selectedIndex > 0)
        {
            SelectedIndex--;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Down && _selectedIndex < _items.Length - 1)
        {
            SelectedIndex++;
            e.Handled = true;
        }
    }

    private void ShowMenu()
    {
        _menu?.Dispose();
        _menu = new ContextMenuStrip
        {
            BackColor = Theme.InputBg,
            ForeColor = Theme.Text,
            Font = Theme.UiFont,
            ShowImageMargin = false,
            Renderer = new DarkMenuRenderer()
        };
        for (var i = 0; i < _items.Length; i++)
        {
            var index = i;
            var item = new ToolStripMenuItem(_items[i])
            {
                Checked = i == _selectedIndex,
                BackColor = Theme.InputBg,
                ForeColor = Theme.Text
            };
            item.Click += (_, _) => SelectedIndex = index;
            _menu.Items.Add(item);
        }
        _menu.Show(this, new Point(0, Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _menu?.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Theme.Text;
        base.OnRenderItemText(e);
    }
}

internal sealed class DarkColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => Theme.NavActive;
    public override Color MenuItemSelectedGradientBegin => Theme.NavActive;
    public override Color MenuItemSelectedGradientEnd => Theme.NavActive;
    public override Color MenuItemBorder => Theme.Border;
    public override Color MenuBorder => Theme.Border;
    public override Color ToolStripDropDownBackground => Theme.InputBg;
    public override Color ImageMarginGradientBegin => Theme.InputBg;
    public override Color ImageMarginGradientMiddle => Theme.InputBg;
    public override Color ImageMarginGradientEnd => Theme.InputBg;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Border;
}

internal sealed class DarkSlider : Control
{
    private int _min = 1;
    private int _max = 200;
    private int _value = 30;
    private bool _dragging;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public int Minimum
    {
        get => _min;
        set { _min = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public int Maximum
    {
        get => _max;
        set { _max = Math.Max(_min + 1, value); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public event EventHandler? ValueChanged;
    public event EventHandler? DragEnded;

    public DarkSlider()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
            ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Height = 28;
        Cursor = Cursors.Hand;
        BackColor = Theme.Bg;
        TabStop = true;
        AccessibleRole = AccessibleRole.Slider;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var track = new Rectangle(4, Height / 2 - 3, Width - 8, 6);
        using (var trackBrush = new SolidBrush(Theme.InputBg))
            g.FillRectangle(trackBrush, track);
        using (var border = new Pen(Theme.Border))
            g.DrawRectangle(border, track);

        var ratio = (_value - _min) / (float)(_max - _min);
        var fillW = (int)((track.Width - 1) * ratio);
        if (fillW > 0)
        {
            using var fill = new SolidBrush(Theme.AccentDim);
            g.FillRectangle(fill, track.Left + 1, track.Top + 1, fillW, track.Height - 1);
        }

        var thumbX = track.Left + (int)((track.Width - 10) * ratio);
        var thumb = new Rectangle(thumbX, 5, 10, Height - 10);
        using (var thumbBrush = new SolidBrush(Theme.Accent))
            g.FillRectangle(thumbBrush, thumb);

        if (Focused)
            ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width - 1, Height - 1));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _dragging = true;
        Capture = true;
        SetFromMouse(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        Capture = false;
        DragEnded?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.Control ? 10 : 1;
        var before = _value;
        switch (e.KeyCode)
        {
            case Keys.Left:
            case Keys.Down:
                Value = _value - step;
                break;
            case Keys.Right:
            case Keys.Up:
                Value = _value + step;
                break;
            case Keys.PageDown:
                Value = _value - 10;
                break;
            case Keys.PageUp:
                Value = _value + 10;
                break;
            case Keys.Home:
                Value = _min;
                break;
            case Keys.End:
                Value = _max;
                break;
            default:
                return;
        }
        e.Handled = true;
        if (_value != before)
            DragEnded?.Invoke(this, EventArgs.Empty);
    }

    private void SetFromMouse(int x)
    {
        var trackLeft = 4;
        var trackWidth = Math.Max(1, Width - 8);
        var ratio = Math.Clamp((x - trackLeft) / (float)trackWidth, 0f, 1f);
        Value = _min + (int)Math.Round((_max - _min) * ratio);
    }
}
