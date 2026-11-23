using System.ComponentModel;
using DrakenHub.Core;

namespace DrakenHub.UI;

internal enum SettingRowKind
{
    Hotkey,
    Coord,
    Cell,
    Action
}

/// <summary>
/// Compact setting line: label ........ value. Click to edit.
/// Soft fill + subtle hover — no harsh mono or bright borders.
/// </summary>
internal sealed class SettingRow : Panel
{
    private readonly Label _label;
    private readonly Label _value;
    private Control? _trailing;
    private int _trailingWidth;
    private bool _hover;

    public SettingRowKind Kind { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string LabelText
    {
        get => _label.Text;
        set { _label.Text = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool ValueDanger { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string ValueText
    {
        get => _value.Text;
        set
        {
            _value.Text = value;
            _value.ForeColor = ValueDanger ? Theme.Danger
                : IsEmptyValue(value) ? Theme.TextMuted : Theme.Text;
            LayoutChildren();
            Invalidate();
        }
    }

    public event EventHandler? Activated;

    public SettingRow(SettingRowKind kind, string label, string value)
    {
        Kind = kind;
        Height = 38;
        Margin = new Padding(0, 0, 0, 4);
        Cursor = Cursors.Hand;
        BackColor = Theme.PanelAlt;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        _label = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = Theme.Text,
            Font = Theme.UiFont,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Theme.PanelAlt,
            Text = label
        };
        _value = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = IsEmptyValue(value) ? Theme.TextMuted : Theme.Text,
            Font = Theme.UiFontValue,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Theme.PanelAlt,
            Text = value
        };

        Controls.Add(_label);
        Controls.Add(_value);
        WireActivate(_label);
        WireActivate(_value);

        Resize += (_, _) =>
        {
            LayoutChildren();
            Invalidate();
        };
        MouseEnter += (_, _) => SetHover(true);
        MouseLeave += (_, _) =>
        {
            if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
                SetHover(false);
        };
        Click += (_, _) => Activated?.Invoke(this, EventArgs.Empty);

        LayoutChildren();
    }

    private void SetHover(bool hover)
    {
        if (_hover == hover) return;
        _hover = hover;
        var fill = Theme.PanelAlt;
        _label.BackColor = fill;
        _value.BackColor = fill;
        if (_trailing != null)
            _trailing.BackColor = fill;
        Invalidate();
    }

    /// <summary>Puts an extra control on the right (e.g. tab combo) without a second bordered row.</summary>
    public void SetTrailing(Control trailing, int width)
    {
        if (_trailing != null && _trailing != trailing)
            Controls.Remove(_trailing);
        _trailing = trailing;
        _trailingWidth = Math.Max(40, width);
        trailing.Margin = Padding.Empty;
        trailing.BackColor = Theme.PanelAlt;
        if (!Controls.Contains(trailing))
            Controls.Add(trailing);
        LayoutChildren();
        Invalidate();
    }

    private void WireActivate(Control c)
    {
        c.Click += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        c.MouseEnter += (_, _) => SetHover(true);
        c.Cursor = Cursors.Hand;
    }

    private void LayoutChildren()
    {
        const int iconSlot = 36;
        var valueW = Math.Max(72, TextRenderer.MeasureText(_value.Text, _value.Font).Width + 10);
        if (_trailing != null)
            valueW = Math.Min(valueW, 72);
        var trail = _trailing != null ? _trailingWidth + 12 : 0;
        var rightEdge = Width - 12 - trail;
        var labelW = Math.Max(40, rightEdge - valueW - iconSlot - 8);

        _value.SetBounds(rightEdge - valueW, 5, valueW, Height - 10);
        _label.SetBounds(iconSlot, 5, labelW, Height - 10);

        if (_trailing != null)
            _trailing.SetBounds(Width - 10 - _trailingWidth, 5, _trailingWidth, 28);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var bg = new SolidBrush(Theme.PanelAlt))
            e.Graphics.FillRectangle(bg, ClientRectangle);
        var border = _hover ? Theme.BorderHover : Theme.Border;
        using var pen = new Pen(border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        DrawKindIcon(e.Graphics);
        base.OnPaint(e);
    }

    private void DrawKindIcon(Graphics g)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var box = new Rectangle(10, (Height - 16) / 2, 16, 16);
        using var pen = new Pen(Theme.Accent, 1.4f);
        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
        pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
        switch (Kind)
        {
            case SettingRowKind.Hotkey:
                g.DrawRectangle(pen, box.X + 1, box.Y + 3, 14, 10);
                g.DrawLine(pen, box.X + 4, box.Y + 8, box.X + 7, box.Y + 8);
                g.DrawLine(pen, box.X + 9, box.Y + 8, box.X + 12, box.Y + 8);
                g.DrawLine(pen, box.X + 6, box.Y + 11, box.X + 10, box.Y + 11);
                break;
            case SettingRowKind.Coord:
                var c = new Point(box.X + 8, box.Y + 8);
                g.DrawEllipse(pen, c.X - 4, c.Y - 4, 8, 8);
                g.DrawLine(pen, c.X, box.Y + 1, c.X, box.Y + 4);
                g.DrawLine(pen, c.X, box.Y + 12, c.X, box.Y + 15);
                g.DrawLine(pen, box.X + 1, c.Y, box.X + 4, c.Y);
                g.DrawLine(pen, box.X + 12, c.Y, box.X + 15, c.Y);
                break;
            case SettingRowKind.Cell:
                g.DrawRectangle(pen, box.X + 1, box.Y + 1, 6, 6);
                g.DrawRectangle(pen, box.X + 9, box.Y + 1, 6, 6);
                g.DrawRectangle(pen, box.X + 1, box.Y + 9, 6, 6);
                g.DrawRectangle(pen, box.X + 9, box.Y + 9, 6, 6);
                break;
            default:
                using (var brush = new SolidBrush(Theme.Accent))
                    g.FillEllipse(brush, box.X + 5, box.Y + 5, 6, 6);
                break;
        }
    }

    private static bool IsEmptyValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value.Trim();
        return v is "—" or "-" or "None" or "Нет" || v.Contains("не задано", StringComparison.OrdinalIgnoreCase)
               || v.Contains("none", StringComparison.OrdinalIgnoreCase)
               || v.Contains("not set", StringComparison.OrdinalIgnoreCase);
    }
}
