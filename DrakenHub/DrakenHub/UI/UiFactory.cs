using DrakenHub.Core;

namespace DrakenHub.UI;

internal enum BtnKind
{
    Neutral,
    Primary,
    Accent,
    Danger,
    Warn
}

internal static class UiFactory
{
    public const int RowH = 34;
    public const int Gap = 8;

    /// <summary>
    /// Scroll host + absolute stack. Width/heights applied in one Fit pass — no AutoSize feedback loops.
    /// </summary>
    public static Panel ScrollPage(out Panel stack)
    {
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Bg,
            Padding = Padding.Empty
        };

        stack = new Panel
        {
            Location = new Point(0, 0),
            Width = 480,
            Height = 100,
            BackColor = Theme.Bg,
            Margin = Padding.Empty,
            Padding = new Padding(16, 12, 16, 16)
        };

        scroll.Controls.Add(stack);
        var stackRef = stack;
        var fitting = false;
        var lastWidth = -1;

        void Fit(bool force)
        {
            if (fitting || !scroll.IsHandleCreated) return;
            var w = Math.Max(400, scroll.ClientSize.Width);
            if (w <= 0) return;
            if (!force && w == lastWidth) return;

            fitting = true;
            try
            {
                stackRef.SuspendLayout();
                var innerW = Math.Max(360, w - stackRef.Padding.Horizontal);
                stackRef.Width = w;
                lastWidth = w;

                var y = stackRef.Padding.Top;
                foreach (Control child in stackRef.Controls)
                {
                    child.Left = stackRef.Padding.Left;
                    child.Width = innerW;
                    LayoutStackChild(child, innerW);
                    if (!child.Visible) continue;
                    child.Top = y;
                    y += child.Height + Gap;
                }

                stackRef.Height = Math.Max(y + stackRef.Padding.Bottom, 1);
                stackRef.ResumeLayout(false);
            }
            finally
            {
                fitting = false;
            }
        }

        scroll.Tag = (Action)(() => Fit(force: true));
        scroll.Resize += (_, _) => Fit(force: false);
        scroll.HandleCreated += (_, _) => Fit(force: true);
        scroll.VisibleChanged += (_, _) =>
        {
            if (scroll.Visible) Fit(force: true);
        };
        return scroll;
    }

    public static void Relayout(Control scrollOrStack)
    {
        var host = scrollOrStack;
        while (host != null && host.Tag is not Action)
            host = host.Parent;
        if (host?.Tag is Action fit)
            fit();
    }

    public static void Add(Panel stack, Control child)
    {
        child.Margin = Padding.Empty;
        stack.Controls.Add(child);
    }

    private static void LayoutStackChild(Control child, int width)
    {
        if (child is Panel panel && panel.Tag is SectionTag)
        {
            LayoutSectionInner(panel, width);
            return;
        }

        if (child is FlowLayoutPanel { FlowDirection: FlowDirection.TopDown } flp)
        {
            foreach (Control c in flp.Controls)
            {
                c.Width = width;
                if (c is Panel p && p.Tag is SectionTag)
                    LayoutSectionInner(p, width);
                else if (c is FlowLayoutPanel nested)
                    LayoutStackChild(nested, width);
                else if (c is Panel row)
                    row.Width = width;
            }
            flp.Height = MeasureHeight(flp);
            return;
        }

        if (child is TableLayoutPanel)
            return;
    }

    private static void LayoutSectionInner(Panel panel, int width)
    {
        if (panel.Tag is not SectionTag tag) return;

        var innerW = Math.Max(100, width - 24);
        tag.Inner.Left = 12;
        tag.Inner.Top = 28;
        tag.Inner.Width = innerW;
        LayoutStackChild(tag.Inner, innerW);

        var contentH = MeasureHeight(tag.Inner);
        tag.Inner.Height = Math.Max(RowH, contentH);
        panel.Height = 28 + tag.Inner.Height + 12;
    }

    private static int MeasureHeight(Control c)
    {
        if (!c.Visible) return 0;
        if (c is FlowLayoutPanel { FlowDirection: FlowDirection.TopDown } flp)
        {
            var h = flp.Padding.Vertical;
            foreach (Control child in flp.Controls)
            {
                if (!child.Visible) continue;
                h += child.Height + child.Margin.Vertical;
            }
            return Math.Max(RowH, h);
        }

        if (c is FlowLayoutPanel { FlowDirection: FlowDirection.LeftToRight } row)
            return Math.Max(RowH + 4, row.Height > 0 ? row.Height : RowH + 8);

        return Math.Max(RowH, c.PreferredSize.Height > 0 ? c.PreferredSize.Height : c.Height);
    }

    private sealed class SectionTag
    {
        public required Control Inner { get; init; }
        public required Label Title { get; init; }
    }

    public static TableLayoutPanel EqualRow(params Control[] controls)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = controls.Length,
            RowCount = 1,
            Height = RowH + 4,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = Color.Transparent,
            Padding = Padding.Empty
        };
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, RowH + 4));
        var pct = 100f / Math.Max(1, controls.Length);
        for (var i = 0; i < controls.Length; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, pct));
            var c = controls[i];
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(2);
            if (c is Button b)
            {
                b.Height = RowH;
                b.MaximumSize = new Size(0, RowH);
            }
            row.Controls.Add(c, i, 0);
        }
        return row;
    }

    public static TableLayoutPanel FieldRow(Control left, Control right, int leftWidth = 180)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Height = RowH + 4,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = Color.Transparent,
            Padding = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, leftWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, RowH + 4));
        left.Dock = DockStyle.Fill;
        left.Margin = new Padding(2);
        right.Dock = DockStyle.Fill;
        right.Margin = new Padding(2);
        if (right is Button b)
        {
            b.Height = RowH;
            b.MaximumSize = new Size(0, RowH);
        }
        row.Controls.Add(left, 0, 0);
        row.Controls.Add(right, 1, 0);
        return row;
    }

    public static FlowLayoutPanel VStack()
    {
        return new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
    }

    public static Panel Section(string title, Control inner, out Label titleLabel)
    {
        var panel = new Panel
        {
            BackColor = Theme.Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Height = 80
        };

        titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Location = new Point(12, 6),
            ForeColor = Theme.TextMuted,
            Font = Theme.UiFontSmall,
            BackColor = Theme.Panel
        };

        inner.Location = new Point(12, 28);
        inner.BackColor = Theme.Panel;
        panel.Controls.Add(inner);
        panel.Controls.Add(titleLabel);
        titleLabel.BringToFront();
        panel.Tag = new SectionTag { Inner = inner, Title = titleLabel };

        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, panel.ClientSize.Width - 1, panel.ClientSize.Height - 1);
        };

        return panel;
    }

    public static Panel Section(string title, Control inner)
        => Section(title, inner, out _);

    public static void SetSectionTitle(Panel section, string title)
    {
        if (section.Tag is SectionTag tag)
        {
            tag.Title.Text = title;
            return;
        }

        foreach (Control c in section.Controls)
        {
            if (c is Label label)
            {
                label.Text = title;
                return;
            }
        }
    }

    public static Button Btn(string text, BtnKind kind = BtnKind.Neutral, int height = RowH)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Font = Theme.UiFont,
            Height = height,
            MinimumSize = new Size(48, height),
            MaximumSize = new Size(0, height),
            AutoSize = false,
            Cursor = Cursors.Hand,
            Margin = new Padding(2),
            Padding = new Padding(10, 0, 10, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false,
            ForeColor = Theme.Text
        };
        b.FlatAppearance.BorderSize = 1;
        ApplyKind(b, kind);
        return b;
    }

    public static Button Btn(string text, Color accent, int height = RowH)
    {
        var kind = accent.ToArgb() switch
        {
            _ when accent.ToArgb() == Theme.Green.ToArgb() || accent.ToArgb() == Theme.Accent.ToArgb() => BtnKind.Primary,
            _ when accent.ToArgb() == Theme.Cyan.ToArgb() || accent.ToArgb() == Theme.AccentBlue.ToArgb() => BtnKind.Accent,
            _ when accent.ToArgb() == Theme.Red.ToArgb() || accent.ToArgb() == Theme.AccentRed.ToArgb() => BtnKind.Danger,
            _ when accent.ToArgb() == Theme.Orange.ToArgb() || accent.ToArgb() == Theme.AccentOrange.ToArgb() => BtnKind.Warn,
            _ => BtnKind.Neutral
        };
        return Btn(text, kind, height);
    }

    public static void ApplyKind(Button b, BtnKind kind)
    {
        switch (kind)
        {
            case BtnKind.Primary:
                b.BackColor = Color.FromArgb(28, 64, 52);
                b.FlatAppearance.BorderColor = Theme.Accent;
                break;
            case BtnKind.Accent:
                b.BackColor = Color.FromArgb(28, 44, 72);
                b.FlatAppearance.BorderColor = Theme.AccentBlue;
                break;
            case BtnKind.Danger:
                b.BackColor = Color.FromArgb(64, 32, 36);
                b.FlatAppearance.BorderColor = Theme.AccentRed;
                break;
            case BtnKind.Warn:
                b.BackColor = Color.FromArgb(64, 48, 28);
                b.FlatAppearance.BorderColor = Theme.AccentOrange;
                break;
            default:
                b.BackColor = Theme.Button;
                b.FlatAppearance.BorderColor = Theme.ButtonBorder;
                break;
        }
    }

    public static Label Lbl(string text, bool muted = false) => new()
    {
        Text = text,
        ForeColor = muted ? Theme.TextMuted : Theme.Text,
        Font = Theme.UiFont,
        AutoSize = true,
        Margin = new Padding(2, 8, 2, 2),
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = Color.Transparent
    };

    public static TextBox Tb(string text = "") => new()
    {
        Text = text,
        BackColor = Theme.InputBg,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.UiFont,
        Height = 28,
        Margin = new Padding(2)
    };

    public static DarkCombo Combo(params string[] items)
    {
        var c = new DarkCombo { Margin = new Padding(2), Height = 28, Width = 220 };
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
        Margin = new Padding(4, 6, 8, 4),
        BackColor = Color.Transparent
    };

    public static DarkSlider Slider(int min, int max, int value) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Height = 28,
        Margin = new Padding(2),
        Dock = DockStyle.Fill,
        BackColor = Theme.Panel
    };

    public static NumericUpDown Num(int min, int max, int value) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        BackColor = Theme.InputBg,
        ForeColor = Theme.Text,
        Font = Theme.UiFont,
        Width = 90,
        Margin = new Padding(2),
        BorderStyle = BorderStyle.FixedSingle
    };

    public static Button NavBtn(string text)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Top,
            Height = 40,
            Font = Theme.UiFontNav,
            ForeColor = Theme.Text,
            BackColor = Theme.NavIdle,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 8, 0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Margin = Padding.Empty
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Theme.NavHover;
        return b;
    }

    public static Button Chip(string text, bool active)
    {
        var b = Btn(text, active ? BtnKind.Primary : BtnKind.Neutral, 28);
        b.AutoSize = true;
        b.MaximumSize = new Size(0, 28);
        b.Padding = new Padding(14, 0, 14, 0);
        return b;
    }

    /// <summary>Lightweight fixed-height row; items with Tag="fix" share remaining width.</summary>
    public static Panel ActionRow(params (Control Control, int Width)[] cells)
    {
        var row = new Panel
        {
            Height = 36,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = Color.Transparent
        };

        row.Resize += (_, _) =>
        {
            var fixedSum = 0;
            var flex = 0;
            foreach (var (c, w) in cells)
            {
                if (w <= 0) flex++;
                else fixedSum += w;
            }

            var gaps = (cells.Length + 1) * 4;
            var flexW = flex > 0
                ? Math.Max(64, (row.ClientSize.Width - gaps - fixedSum) / flex)
                : 64;
            var x = 4;
            foreach (var (c, w) in cells)
            {
                var width = w <= 0 ? flexW : w;
                c.SetBounds(x, 2, width, 32);
                x += width + 4;
            }
        };

        foreach (var (c, _) in cells)
            row.Controls.Add(c);
        return row;
    }
}
