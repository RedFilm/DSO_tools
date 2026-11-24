using System.ComponentModel;
using System.Drawing.Drawing2D;
using DrakenHub.Core;
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

    public ItemIconPreview(string? pngBase64)
    {
        _bmp = ItemIconMatcher.DecodePreview(pngBase64);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(36, 32);
        Margin = Padding.Empty;
        BackColor = Theme.InputBg;
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
        using (var pen = new Pen(Theme.Border))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        if (_bmp == null) return;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_bmp, new Rectangle(2, 2, Width - 4, Height - 4));
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
