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
    private readonly Label _kindBadge;
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
            Invalidate();
        }
    }

    public event EventHandler? Activated;

    public SettingRow(SettingRowKind kind, string label, string value)
    {
        Kind = kind;
        Height = 38;
        Margin = new Padding(0, 0, 0, 5);
        Cursor = Cursors.Hand;
        BackColor = Theme.PanelAlt;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        _kindBadge = new Label
        {
            AutoSize = false,
            Width = 28,
            Height = 22,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = Theme.UiFontSmall,
            ForeColor = Theme.Accent,
            BackColor = Theme.PanelAlt,
            Text = kind switch
            {
                SettingRowKind.Hotkey => "⌨",
                SettingRowKind.Coord => "⌖",
                SettingRowKind.Cell => "▣",
                _ => "•"
            }
        };
        _label = new Label
        {
            AutoSize = false,
            ForeColor = Theme.Text,
            Font = Theme.UiFont,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Theme.PanelAlt,
            Text = label
        };
        _value = new Label
        {
            AutoSize = false,
            ForeColor = IsEmptyValue(value) ? Theme.TextMuted : Theme.Text,
            Font = Theme.UiFontValue,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Theme.PanelAlt,
            Text = value
        };

        Controls.Add(_kindBadge);
        Controls.Add(_label);
        Controls.Add(_value);
        WireActivate(_kindBadge);
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
        _kindBadge.BackColor = fill;
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
        const int badgeX = 10;
        const int badgeW = 28;
        var valueW = _trailing != null ? 64 : 156;
        var trail = _trailing != null ? _trailingWidth + 12 : 0;
        var rightEdge = Width - 12 - trail;

        _kindBadge.SetBounds(badgeX, 8, badgeW, 22);
        _value.SetBounds(rightEdge - valueW, 5, valueW, 28);
        _label.SetBounds(42, 5, Math.Max(40, rightEdge - valueW - 50), 28);

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
        base.OnPaint(e);
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
