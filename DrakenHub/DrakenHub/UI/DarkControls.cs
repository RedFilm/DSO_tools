using System.ComponentModel;
using DrakenHub.Core;

namespace DrakenHub.UI;

internal sealed class DarkCombo : ComboBox
{
    public DarkCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.InputBg;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;
        ItemHeight = 24;
        IntegralHeight = false;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var bg = new SolidBrush(selected ? Theme.NavActive : Theme.InputBg);
        e.Graphics.FillRectangle(bg, e.Bounds);
        var text = Items[e.Index]?.ToString() ?? "";
        TextRenderer.DrawText(e.Graphics, text, Font, e.Bounds, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
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
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Height = 28;
        Cursor = Cursors.Hand;
        BackColor = Theme.Bg;
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
            using var fill = new SolidBrush(Theme.NavActive);
            g.FillRectangle(fill, track.Left + 1, track.Top + 1, fillW, track.Height - 1);
        }

        var thumbX = track.Left + (int)((track.Width - 10) * ratio);
        var thumb = new Rectangle(thumbX, 5, 10, Height - 10);
        using (var thumbBrush = new SolidBrush(Theme.Accent))
            g.FillRectangle(thumbBrush, thumb);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
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

    private void SetFromMouse(int x)
    {
        var trackLeft = 4;
        var trackWidth = Math.Max(1, Width - 8);
        var ratio = Math.Clamp((x - trackLeft) / (float)trackWidth, 0f, 1f);
        Value = _min + (int)Math.Round((_max - _min) * ratio);
    }
}
