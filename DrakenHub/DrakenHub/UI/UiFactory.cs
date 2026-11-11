using DrakenHub.Core;

namespace DrakenHub.UI;

internal static class UiFactory
{
    public const int BtnH = 34;
    public const int Gap = 8;
    private static readonly Size BtnMax = new(4096, BtnH);
    private static readonly Size BtnMin = new(40, BtnH);

    public static Button Btn(string text, Color back, int height = BtnH)
    {
        var b = new Button
        {
            Text = text,
            BackColor = back,
            ForeColor = Color.Black,
            FlatStyle = FlatStyle.Flat,
            Font = Theme.UiFont,
            Height = height,
            MinimumSize = new Size(40, height),
            MaximumSize = new Size(4096, height),
            AutoSize = false,
            Cursor = Cursors.Hand,
            Margin = new Padding(3),
            Padding = new Padding(8, 0, 8, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    public static Label Lbl(string text, bool muted = false) => new()
    {
        Text = text,
        ForeColor = muted ? Theme.TextMuted : Theme.Text,
        Font = Theme.UiFont,
        AutoSize = true,
        Margin = new Padding(3, 8, 3, 3),
        TextAlign = ContentAlignment.MiddleLeft
    };

    public static TextBox Tb(string text = "") => new()
    {
        Text = text,
        BackColor = Theme.InputBg,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.UiFont,
        Height = 28,
        Margin = new Padding(3)
    };

    public static DarkCombo Combo(params string[] items)
    {
        var c = new DarkCombo { Margin = new Padding(3), Height = 30, Width = 200 };
        c.Items.AddRange(items);
        if (items.Length > 0) c.SelectedIndex = 0;
        return c;
    }

    public static CheckBox Check(string text, bool value = false) => new()
    {
        Text = text,
        Checked = value,
        ForeColor = Theme.Text,
        Font = Theme.UiFont,
        AutoSize = true,
        Margin = new Padding(6, 8, 6, 4)
    };

    public static DarkSlider Slider(int min, int max, int value, int width = 360) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = width,
        Height = 28,
        Margin = new Padding(3)
    };

    public static NumericUpDown Num(int min, int max, int value) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        BackColor = Theme.InputBg,
        ForeColor = Theme.Text,
        Font = Theme.UiFont,
        Width = 80,
        Margin = new Padding(3)
    };

    public static FlowLayoutPanel Col(int width) => new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Width = width,
        BackColor = Theme.Bg,
        Margin = new Padding(0),
        Padding = new Padding(0, 0, 0, 4)
    };

    public static FlowLayoutPanel Row(int height = BtnH + 10) => new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        AutoSize = false,
        Height = height,
        MinimumSize = new Size(0, height),
        MaximumSize = new Size(4096, height),
        BackColor = Theme.Bg,
        Margin = new Padding(0, 0, 0, Gap),
        Padding = new Padding(0)
    };

    public static void AddEqual(FlowLayoutPanel row, int totalWidth, params Control[] controls)
    {
        row.Width = totalWidth;
        row.Height = BtnH + 10;
        var gaps = controls.Length * 6;
        var w = Math.Max(40, (totalWidth - gaps) / Math.Max(1, controls.Length));
        foreach (var c in controls)
        {
            c.Width = w;
            c.Height = BtnH;
            c.Margin = new Padding(3);
            if (c is Button b)
            {
                b.MaximumSize = BtnMax;
                b.MinimumSize = BtnMin;
                b.Height = BtnH;
            }
            row.Controls.Add(c);
        }
    }

    public static void AddSized(FlowLayoutPanel row, int totalWidth, params (Control C, int W)[] cells)
    {
        row.Width = totalWidth;
        row.Height = BtnH + 10;
        foreach (var (c, w) in cells)
        {
            c.Width = w;
            c.Margin = new Padding(3);
            if (c is Button b)
            {
                b.Height = BtnH;
                b.MaximumSize = BtnMax;
                b.MinimumSize = BtnMin;
            }
            else if (c is not Label and not CheckBox and not DarkCombo and not TextBox and not DarkSlider and not NumericUpDown)
            {
                c.Height = BtnH;
            }
            row.Controls.Add(c);
        }
    }

    public static Panel Section(string title, Control inner, int width)
    {
        var panel = new Panel
        {
            Width = width,
            AutoSize = false,
            BackColor = Theme.Bg,
            Margin = new Padding(0, 10, 0, 6),
            Padding = new Padding(0)
        };

        var titleLabel = new Label
        {
            Text = " " + title + " ",
            ForeColor = Theme.TextMuted,
            Font = Theme.UiFontSmall,
            AutoSize = true,
            Location = new Point(14, 0),
            BackColor = Theme.Bg
        };

        var contentWidth = Math.Max(100, width - 24);
        inner.Location = new Point(12, 26);
        SizeInner(inner, contentWidth);

        panel.Controls.Add(inner);
        panel.Controls.Add(titleLabel);
        titleLabel.BringToFront();
        panel.Height = 26 + inner.Height + 14;
        panel.Tag = titleLabel;

        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 8, panel.ClientSize.Width - 1, panel.ClientSize.Height - 9);
        };

        return panel;
    }

    public static void SetSectionTitle(Panel section, string title)
    {
        if (section.Tag is Label label)
            label.Text = " " + title + " ";
    }

    private static void SizeInner(Control inner, int width)
    {
        inner.Width = width;
        if (inner is FlowLayoutPanel flp)
        {
            flp.Width = width;
            flp.PerformLayout();
            // PreferredSize can be unreliable before handle creation; measure children.
            var h = 0;
            foreach (Control child in flp.Controls)
            {
                if (!child.Visible) continue;
                h += child.Height + child.Margin.Vertical;
            }
            inner.Height = Math.Max(BtnH + 10, h + flp.Padding.Vertical + 4);
        }
        else
        {
            inner.PerformLayout();
            inner.Height = Math.Max(BtnH + 10, inner.PreferredSize.Height);
        }
    }

    public static void RefreshSectionHeight(Panel section)
    {
        Control? inner = null;
        foreach (Control c in section.Controls)
        {
            if (c is Label) continue;
            inner = c;
            break;
        }
        if (inner is null) return;
        SizeInner(inner, Math.Max(100, section.Width - 24));
        section.Height = 26 + inner.Height + 14;
    }

    public static Panel ScrollHost(Control content, int contentWidth)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Bg,
            Padding = new Padding(12)
        };
        content.Location = new Point(12, 12);
        content.Width = contentWidth;
        host.Controls.Add(content);

        void Fit()
        {
            if (host.Controls.Count == 0) return;
            var current = host.Controls[0];
            var w = Math.Max(700, host.ClientSize.Width - 36);
            current.Width = w;
            ResizeTree(current, w);
        }

        host.Resize += (_, _) => Fit();
        host.HandleCreated += (_, _) => Fit();
        host.ControlAdded += (_, _) => Fit();
        host.VisibleChanged += (_, _) => { if (host.Visible) Fit(); };
        return host;
    }

    private static void ResizeTree(Control root, int width)
    {
        root.Width = width;
        if (root is not FlowLayoutPanel { FlowDirection: FlowDirection.TopDown } col)
            return;

        col.Width = width;
        foreach (Control row in col.Controls)
        {
            if (row is FlowLayoutPanel { FlowDirection: FlowDirection.LeftToRight } r)
            {
                StretchRow(r, width);
            }
            else if (row is Panel section && section.Tag is Label)
            {
                section.Width = width;
                RefreshSectionHeight(section);
                foreach (Control inner in section.Controls)
                {
                    if (inner is Label) continue;
                    var iw = width - 24;
                    if (inner is FlowLayoutPanel { FlowDirection: FlowDirection.TopDown } ic)
                    {
                        ic.Width = iw;
                        foreach (Control ir in ic.Controls)
                        {
                            if (ir is FlowLayoutPanel { FlowDirection: FlowDirection.LeftToRight } irr)
                                StretchRow(irr, iw);
                        }
                        SizeInner(ic, iw);
                    }
                }
                RefreshSectionHeight(section);
            }
            else if (row is FlowLayoutPanel { FlowDirection: FlowDirection.TopDown } nested)
            {
                nested.Width = width;
                ResizeTree(nested, width);
            }
            else
            {
                row.Width = width;
            }
        }
    }

    private static void StretchRow(FlowLayoutPanel row, int width)
    {
        row.Width = width;
        row.Height = BtnH + 10;
        var buttons = row.Controls.OfType<Button>().ToList();
        if (buttons.Count == 0) return;

        var nonButtons = row.Controls.Cast<Control>().Where(c => c is not Button).ToList();
        if (nonButtons.Count == 0)
        {
            var gaps = buttons.Count * 6;
            var bw = Math.Max(40, (width - gaps) / buttons.Count);
            foreach (var b in buttons)
            {
                b.Width = bw;
                b.Height = BtnH;
                b.MaximumSize = BtnMax;
                b.MinimumSize = BtnMin;
            }
            return;
        }

        var flex = buttons.LastOrDefault(b => Equals(b.Tag, "flex")) ?? buttons.Last();
        var used = row.Controls.Cast<Control>()
            .Where(c => !ReferenceEquals(c, flex))
            .Sum(c => c.Width + c.Margin.Horizontal);
        flex.Width = Math.Max(80, width - used - flex.Margin.Horizontal);
        flex.Height = BtnH;
        flex.MaximumSize = BtnMax;
        flex.MinimumSize = BtnMin;
    }
}
