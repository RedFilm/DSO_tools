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
        ItemHeight = 26;
        IntegralHeight = false;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var bg = new SolidBrush(selected ? Theme.TabActive : Theme.InputBg);
        e.Graphics.FillRectangle(bg, e.Bounds);
        var text = Items[e.Index]?.ToString() ?? "";
        TextRenderer.DrawText(e.Graphics, text, Font, e.Bounds, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x000F) // WM_PAINT
            DrawChrome();
    }

    private void DrawChrome()
    {
        using var g = CreateGraphics();
        using var border = new Pen(Theme.Border);
        using var arrowBg = new SolidBrush(Theme.Orange);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var arrowRect = new Rectangle(Width - 28, 1, 27, Height - 2);
        g.FillRectangle(arrowBg, arrowRect);
        using var arrow = new Pen(Color.White, 2);
        var cx = arrowRect.Left + arrowRect.Width / 2;
        var cy = arrowRect.Top + arrowRect.Height / 2;
        g.DrawLines(arrow, new[]
        {
            new Point(cx - 5, cy - 2),
            new Point(cx, cy + 3),
            new Point(cx + 5, cy - 2)
        });
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

    public DarkSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Height = 28;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Bg);
        var track = new Rectangle(4, Height / 2 - 4, Width - 8, 8);
        using (var trackBrush = new SolidBrush(Theme.InputBg))
            g.FillRectangle(trackBrush, track);
        using (var border = new Pen(Theme.Border))
            g.DrawRectangle(border, track);

        var ratio = (_value - _min) / (float)(_max - _min);
        var thumbX = track.Left + (int)((track.Width - 10) * ratio);
        var thumb = new Rectangle(thumbX, 4, 10, Height - 8);
        using (var thumbBrush = new SolidBrush(Theme.Orange))
            g.FillRectangle(thumbBrush, thumb);

        var text = _value.ToString();
        TextRenderer.DrawText(g, text, Theme.UiFont, ClientRectangle, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
        _dragging = false;
        Capture = false;
    }

    private void SetFromMouse(int x)
    {
        var trackLeft = 4;
        var trackWidth = Math.Max(1, Width - 8);
        var ratio = Math.Clamp((x - trackLeft) / (float)trackWidth, 0f, 1f);
        Value = _min + (int)Math.Round((_max - _min) * ratio);
    }
}
