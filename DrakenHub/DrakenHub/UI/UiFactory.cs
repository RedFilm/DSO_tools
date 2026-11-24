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
    public const int RowH = 36;
    public const int Gap = 10;

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
            flp.SuspendLayout();
            foreach (Control c in flp.Controls)
            {
                // Layout nested content first so Height is known before the parent flows children.
                if (c is Panel p && p.Tag is SectionTag)
                    LayoutSectionInner(p, width);
                else if (c is FlowLayoutPanel { FlowDirection: FlowDirection.TopDown } nested)
                    LayoutStackChild(nested, width);
                else if (c is SettingRow settingRow)
                {
                    settingRow.Width = width;
                    settingRow.Height = 38;
                }
                else if (c is Panel actionRow && actionRow.Tag is ActionRowTag)
                {
                    // Horizontal action rows manage their own children — only stretch the row.
                    actionRow.Width = width;
                    ActionRowLayout.Apply(actionRow);
                }
                else if (c is Panel plainRow && c is not FlowLayoutPanel && c is not TableLayoutPanel and not SettingRow)
                {
                    LayoutPlainPanel(plainRow, width);
                }

                if (c is WrapGrid grid)
                    grid.ApplyOuterSize(width, MeasureWrap(c, width));
                else
                    c.Width = width;
            }
            flp.Height = MeasureHeight(flp);
            flp.ResumeLayout(true); // reflow children with final heights — prevents overlap
            return;
        }

        if (child is FlowLayoutPanel { FlowDirection: FlowDirection.LeftToRight } rowFlp)
        {
            // Keep natural checkbox/button widths so items flow horizontally (e.g. inventory tabs).
            rowFlp.Width = width;
            rowFlp.WrapContents = true;
            rowFlp.PerformLayout();
            rowFlp.Height = Math.Max(RowH + 4, rowFlp.PreferredSize.Height);
            return;
        }

        if (child is Panel plain && child is not FlowLayoutPanel && child is not TableLayoutPanel && plain.Tag is not SectionTag)
        {
            LayoutPlainPanel(plain, width);
            return;
        }

        if (child is TableLayoutPanel)
            return;
    }

    /// <summary>Top-down absolute stack inside a plain Panel (used by Extra Skill / profile cards).</summary>
    private static void LayoutPlainPanel(Panel panel, int width)
    {
        panel.Width = width;
        panel.SuspendLayout();
        var y = panel.Padding.Top;
        var innerW = Math.Max(1, width - panel.Padding.Horizontal);
        foreach (Control c in panel.Controls)
        {
            if (!c.Visible) continue;
            if (c is FlowLayoutPanel { FlowDirection: FlowDirection.TopDown } nested)
                LayoutStackChild(nested, innerW);
            else if (c is Panel p && p.Tag is SectionTag)
                LayoutSectionInner(p, innerW);
            else if (c is SettingRow sr)
            {
                sr.Width = innerW;
                sr.Height = 38;
            }
            else if (c is Panel action && action.Tag is ActionRowTag)
            {
                action.Width = innerW;
                ActionRowLayout.Apply(action);
            }
            else if (c is Panel row && c is not TableLayoutPanel and not SettingRow && row.Tag is not SectionTag and not ActionRowTag)
                LayoutPlainPanel(row, innerW);

            c.Left = panel.Padding.Left;
            c.Width = innerW;
            c.Top = y;
            y += c.Height + Math.Max(c.Margin.Bottom, 4);
        }
        panel.Height = Math.Max(RowH, y + panel.Padding.Bottom);
        panel.ResumeLayout(false);
    }

    private static void LayoutSectionInner(Panel panel, int width)
    {
        if (panel.Tag is not SectionTag tag) return;

        LayoutSectionHeader(panel, width);
        var innerW = Math.Max(100, width - 24);
        tag.Inner.Left = 12;
        var top = Math.Max(28, tag.HeaderBottom);
        tag.Inner.Top = top;
        tag.Inner.Width = innerW;
        LayoutStackChild(tag.Inner, innerW);

        var contentH = MeasureHeight(tag.Inner);
        tag.Inner.Height = Math.Max(RowH, contentH);
        panel.Height = top + tag.Inner.Height + 12;
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

    /// <summary>Row-wrap height from child sizes. PreferredSize lags until the handle exists.</summary>
    private static int MeasureWrap(Control grid, int width)
    {
        var inner = Math.Max(1, width - grid.Padding.Horizontal);
        var x = 0;
        var rowH = 0;
        var h = grid.Padding.Vertical;
        var any = false;
        foreach (Control c in grid.Controls)
        {
            if (!c.Visible) continue;
            any = true;
            var w = Math.Max(1, c.Width + c.Margin.Horizontal);
            var ch = c.Height + c.Margin.Vertical;
            if (x > 0 && x + w > inner)
            {
                h += rowH;
                x = 0;
                rowH = 0;
            }

            x += w;
            rowH = Math.Max(rowH, ch);
        }

        if (any) h += rowH;
        return Math.Max(1, h);
    }

    private static void LayoutSectionHeader(Panel panel, int width)
    {
        if (panel.Tag is not SectionTag tag) return;
        var textW = Math.Max(80, width - 28);
        var titleH = MeasureWrapped(tag.Title.Text, tag.Title.Font, textW);
        tag.Title.SetBounds(14, 10, textW, titleH);
        var y = 10 + titleH + 6;
        if (tag.Subtitle != null)
        {
            var subH = MeasureWrapped(tag.Subtitle.Text, tag.Subtitle.Font, textW);
            tag.Subtitle.SetBounds(14, y, textW, subH);
            y += subH + 8;
        }

        tag.HeaderBottom = y;
    }

    private static int MeasureWrapped(string text, Font font, int width)
    {
        if (string.IsNullOrEmpty(text)) return font.Height;
        var size = TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
        return Math.Max(font.Height, size.Height);
    }

    private sealed class SectionTag
    {
        public required Control Inner { get; init; }
        public required Label Title { get; init; }
        public Label? Subtitle { get; set; }
        public int HeaderBottom { get; set; }
    }

    /// <summary>Marks a horizontal ActionRow so stack layout does not reflow its children vertically.</summary>
    private sealed class ActionRowTag
    {
        public required (Control Control, int Width)[] Cells { get; init; }
    }

    internal static class ActionRowLayout
    {
        public static void Apply(Panel row)
        {
            if (row.Tag is not ActionRowTag tag) return;
            var cells = tag.Cells;
            var fixedSum = 0;
            var flex = 0;
            foreach (var (_, w) in cells)
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
            row.Height = 38;
        }
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
        if (left is Label label)
        {
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
        }
        if (right is TextBox box)
            box.AutoSize = false;
        left.Dock = DockStyle.Fill;
        left.Margin = new Padding(2, 4, 8, 4);
        right.Dock = DockStyle.Fill;
        right.Margin = new Padding(2, 4, 2, 4);
        if (right is Button b)
        {
            b.Height = RowH;
            b.MaximumSize = new Size(0, RowH);
        }
        row.Controls.Add(left, 0, 0);
        row.Controls.Add(right, 1, 0);
        return row;
    }

    /// <summary>
    /// Left-to-right gallery that wraps. The stack assigns the outer size.
    /// FlowLayoutPanel would otherwise shrink the gallery to the first child, clipping the rest.
    /// </summary>
    internal sealed class WrapGrid : FlowLayoutPanel
    {
        private Size _outer;
        private bool _placing;

        public WrapGrid()
        {
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = true;
            AutoSize = false;
            AutoScroll = false;
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            Padding = new Padding(0, 6, 0, 0);
        }

        public void ApplyOuterSize(int width, int height)
        {
            _outer = new Size(Math.Max(1, width), Math.Max(1, height));
            SuspendLayout();
            try
            {
                SetBounds(Left, Top, _outer.Width, _outer.Height, BoundsSpecified.Size);
                PlaceChildren();
            }
            finally
            {
                ResumeLayout(false);
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
            => _outer.Width > 0 ? _outer : base.GetPreferredSize(proposedSize);

        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (_outer.Width > 0)
            {
                width = _outer.Width;
                height = _outer.Height;
            }
            base.SetBoundsCore(x, y, width, height, specified);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            PlaceChildren();
        }

        private void PlaceChildren()
        {
            if (_placing) return;
            _placing = true;
            try
            {
                var inner = Math.Max(1, ClientSize.Width - Padding.Horizontal);
                var x = Padding.Left;
                var y = Padding.Top;
                var rowH = 0;
                foreach (Control c in Controls)
                {
                    if (!c.Visible) continue;
                    var w = Math.Max(1, c.Width) + c.Margin.Horizontal;
                    var h = Math.Max(1, c.Height) + c.Margin.Vertical;
                    if (x > Padding.Left && x + w > Padding.Left + inner)
                    {
                        x = Padding.Left;
                        y += rowH;
                        rowH = 0;
                    }

                    c.Location = new Point(x + c.Margin.Left, y + c.Margin.Top);
                    x += w;
                    rowH = Math.Max(rowH, h);
                }
            }
            finally
            {
                _placing = false;
            }
        }
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
        var panel = new BufferedPanel
        {
            BackColor = Theme.Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Height = 80
        };

        titleLabel = new Label
        {
            Text = title,
            AutoSize = false,
            Location = new Point(14, 10),
            ForeColor = Theme.Text,
            Font = Theme.UiFontValue,
            BackColor = Theme.Panel,
            AutoEllipsis = true
        };

        inner.Location = new Point(12, 36);
        inner.BackColor = Theme.Panel;
        panel.Controls.Add(inner);
        panel.Controls.Add(titleLabel);
        titleLabel.BringToFront();
        panel.Tag = new SectionTag { Inner = inner, Title = titleLabel, HeaderBottom = 36 };

        panel.Paint += (_, e) =>
        {
            using (var bg = new SolidBrush(Theme.Panel))
                e.Graphics.FillRectangle(bg, panel.ClientRectangle);
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
        // v2: one accent for CTA, danger for destructive, everything else neutral.
        switch (kind)
        {
            case BtnKind.Primary:
                b.BackColor = Theme.AccentDim;
                b.FlatAppearance.BorderColor = Theme.Accent;
                b.ForeColor = Theme.Text;
                break;
            case BtnKind.Danger:
                b.BackColor = Theme.DangerDim;
                b.FlatAppearance.BorderColor = Theme.Danger;
                b.ForeColor = Theme.Text;
                break;
            case BtnKind.Accent:
            case BtnKind.Warn:
            default:
                b.BackColor = Theme.Button;
                b.FlatAppearance.BorderColor = Theme.ButtonBorder;
                b.ForeColor = Theme.Text;
                break;
        }
    }

    public static Panel SectionCard(string title, string? subtitle, Control inner, out Label titleLabel, out Label? subtitleLabel)
    {
        var panel = Section(title, inner, out titleLabel);
        subtitleLabel = null;
        if (string.IsNullOrEmpty(subtitle)) return panel;

        subtitleLabel = new Label
        {
            Text = subtitle,
            AutoSize = false,
            ForeColor = Theme.TextMuted,
            Font = Theme.UiFontSmall,
            BackColor = Theme.Panel,
            AutoEllipsis = false
        };
        if (panel.Tag is SectionTag tag)
        {
            tag.Subtitle = subtitleLabel;
            panel.Controls.Add(subtitleLabel);
            subtitleLabel.BringToFront();
        }
        return panel;
    }

    public static SettingRow CreateSettingRow(SettingRowKind kind, string label, string value)
        => new(kind, label, value);

    public static Label Lbl(string text, bool muted = false) => new()
    {
        Text = text,
        // Muted = secondary info, still clearly readable on Panel/Bg.
        ForeColor = muted ? Theme.TextMuted : Theme.Text,
        Font = muted ? Theme.UiFontSmall : Theme.UiFont,
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
        AutoSize = false,
        Margin = new Padding(2)
    };

    public static DarkCombo Combo(params string[] items)
    {
        var c = new DarkCombo { Margin = new Padding(2), Height = 28, Width = 220 };
        c.Items.AddRange(items);
        if (items.Length > 0) c.SelectedIndex = 0;
        return c;
    }

    public static DarkCheckBox Check(string text, bool value = false) => new()
    {
        Text = text,
        Checked = value,
        Margin = new Padding(4, 6, 8, 4)
    };

    /// <summary>Label | slider | value row (same layout as Fast Mount delay rows).</summary>
    public static TableLayoutPanel DelaySliderRow(
        string labelText,
        int min,
        int max,
        int value,
        Action<int> onChanged,
        Action? onDragEnded = null,
        int labelWidth = 210,
        bool editableValue = false)
    {
        var delayLabel = new Label
        {
            Text = labelText,
            ForeColor = Theme.Text,
            Font = Theme.UiFont,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(2, 0, 8, 0),
            BackColor = Color.Transparent,
            AutoEllipsis = true
        };
        var track = Slider(min, max, value);
        track.Margin = new Padding(2, 4, 2, 4);

        Control valueControl;
        var valueWidth = 56;
        if (!editableValue)
        {
            var val = new Label
            {
                Text = track.Value.ToString(),
                ForeColor = Theme.TextMuted,
                Font = Theme.UiFontValue,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(4, 0, 2, 0),
                BackColor = Color.Transparent
            };
            track.ValueChanged += (_, _) =>
            {
                onChanged(track.Value);
                val.Text = track.Value.ToString();
            };
            valueControl = val;
        }
        else
        {
            var box = new TextBox
            {
                Text = track.Value.ToString(),
                BackColor = Theme.InputBg,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.UiFontValue,
                TextAlign = HorizontalAlignment.Right,
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 6, 2, 6),
                AccessibleName = labelText
            };
            var sync = false;
            track.ValueChanged += (_, _) =>
            {
                onChanged(track.Value);
                if (sync || box.Focused) return;
                box.Text = track.Value.ToString();
            };
            void Commit()
            {
                if (sync) return;
                if (!int.TryParse(box.Text.Trim(), out var typed))
                {
                    box.Text = track.Value.ToString();
                    return;
                }

                var clamped = Math.Clamp(typed, min, max);
                box.Text = clamped.ToString();
                if (clamped == track.Value) return;
                sync = true;
                try { track.Value = clamped; }
                finally { sync = false; }
                onDragEnded?.Invoke();
            }
            box.KeyPress += (_, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                    e.Handled = true;
            };
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    Commit();
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    box.Text = track.Value.ToString();
                    e.SuppressKeyPress = true;
                }
            };
            box.Leave += (_, _) => Commit();
            box.Enter += (_, _) => box.SelectAll();
            valueControl = box;
            valueWidth = Math.Max(
                TextRenderer.MeasureText("200", Theme.UiFontValue).Width + 22,
                Theme.UiFont.Height * 4);
        }

        if (onDragEnded != null)
            track.DragEnded += (_, _) => onDragEnded();

        var row = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Height = RowH + 4,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = Color.Transparent,
            Padding = Padding.Empty
        };
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, RowH + 4));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, valueWidth));
        row.Controls.Add(delayLabel, 0, 0);
        row.Controls.Add(track, 1, 0);
        row.Controls.Add(valueControl, 2, 0);
        return row;
    }

    public static NavSectionHeader NavGroupLabel(string text) => new(text);

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

    public static NavItemButton NavBtn(string text) => new(text);

    public static Button Chip(string text, bool active)
    {
        var b = Btn(text, active ? BtnKind.Primary : BtnKind.Neutral, 28);
        b.AutoSize = true;
        b.MaximumSize = new Size(0, 28);
        b.Padding = new Padding(14, 0, 14, 0);
        return b;
    }

    /// <summary>Lightweight fixed-height row; items with Tag="fix" share remaining width.</summary>
    /// <summary>Lightweight fixed-height row; width &lt;= 0 cells share remaining width.</summary>
    public static Panel ActionRow(params (Control Control, int Width)[] cells)
    {
        var row = new Panel
        {
            Height = 36,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = Color.Transparent,
            Tag = new ActionRowTag { Cells = cells }
        };

        row.Resize += (_, _) => ActionRowLayout.Apply(row);

        foreach (var (c, _) in cells)
        {
            if (c is DarkCheckBox cb)
                cb.AutoSize = false;
            row.Controls.Add(c);
        }

        return row;
    }
}
