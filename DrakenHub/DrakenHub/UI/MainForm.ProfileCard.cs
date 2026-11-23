namespace DrakenHub.UI;

internal sealed partial class MainForm
{
    /// <summary>Collapsible header+body block that always calls UiFactory.Relayout on the scroll host.</summary>
    private static Panel BuildCollapsibleBlock(
        string title,
        Control body,
        Control layoutHost,
        Control? trailingHeaderControl = null)
    {
        var startExpanded = body.Visible;
        const int headerH = 38;
        var headerBtn = UiFactory.Btn((startExpanded ? "▾ " : "▸ ") + title, BtnKind.Neutral, headerH);
        headerBtn.TextAlign = ContentAlignment.MiddleLeft;

        Control headerRow = trailingHeaderControl == null
            ? headerBtn
            : UiFactory.EqualRow(headerBtn, trailingHeaderControl);

        // EqualRow forces every button to RowH, and the hotkey used to be clamped back to 30
        // afterwards — the two halves of one header ended up different heights.
        void FitHeaderButton(Control button, Padding margin)
        {
            button.Height = headerH;
            button.MinimumSize = new Size(48, headerH);
            button.MaximumSize = new Size(0, headerH);
            button.Margin = margin;
        }

        FitHeaderButton(headerBtn, trailingHeaderControl == null ? Padding.Empty : new Padding(0, 0, 2, 0));
        if (trailingHeaderControl != null)
            FitHeaderButton(trailingHeaderControl, new Padding(2, 0, 0, 0));

        if (headerRow is TableLayoutPanel headerTable)
        {
            headerTable.Height = headerH;
            if (headerTable.RowStyles.Count > 0)
                headerTable.RowStyles[0].Height = headerH;
        }

        var wrap = new Panel
        {
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8),
            Padding = Padding.Empty
        };
        wrap.Controls.Add(headerRow);
        wrap.Controls.Add(body);

        var currentTitle = title;

        void RelayoutBlock()
        {
            LayoutPlainChildren(body);
            var bodyH = body.Visible ? Math.Max(UiFactory.RowH, body.Height) : 0;
            wrap.Height = headerRow.Height + (body.Visible ? bodyH + 4 : 0) + 4;
            headerRow.Top = 0;
            body.Top = headerRow.Height + 4;
            body.Left = 0;
            body.Width = wrap.Width;
            UiFactory.Relayout(layoutHost);
        }

        wrap.Resize += (_, _) =>
        {
            headerRow.Width = wrap.Width;
            body.Width = wrap.Width;
            RelayoutBlock();
        };

        headerBtn.Click += (_, _) =>
        {
            body.Visible = !body.Visible;
            headerBtn.Text = (body.Visible ? "▾ " : "▸ ") + currentTitle;
            RelayoutBlock();
        };

        wrap.Tag = new CollapsibleTag(headerBtn, RelayoutBlock, name =>
        {
            currentTitle = name;
            headerBtn.Text = (body.Visible ? "▾ " : "▸ ") + currentTitle;
        });
        RelayoutBlock();
        return wrap;
    }

    private static void SetCollapsibleTitle(Control block, string name)
    {
        if (block.Tag is CollapsibleTag tag)
            tag.SetTitle(name);
    }

    private static void LayoutPlainChildren(Control body)
    {
        if (body is FlowLayoutPanel { FlowDirection: FlowDirection.TopDown } flp)
        {
            var y = flp.Padding.Top;
            var w = Math.Max(1, flp.Width - flp.Padding.Horizontal);
            foreach (Control c in flp.Controls)
            {
                if (!c.Visible) continue;
                c.Width = w;
                c.Left = flp.Padding.Left;
                c.Top = y;
                y += c.Height + Math.Max(4, c.Margin.Bottom);
            }
            flp.Height = Math.Max(UiFactory.RowH, y + flp.Padding.Bottom);
            return;
        }

        if (body is not Panel panel || body is TableLayoutPanel) return;
        var py = panel.Padding.Top;
        var pw = Math.Max(1, panel.Width - panel.Padding.Horizontal);
        foreach (Control c in panel.Controls)
        {
            if (!c.Visible) continue;
            c.Left = panel.Padding.Left;
            c.Width = pw;
            c.Top = py;
            py += c.Height + 4;
        }
        panel.Height = Math.Max(UiFactory.RowH, py + panel.Padding.Bottom);
    }

    private sealed class CollapsibleTag(Button header, Action relayout, Action<string> setTitle)
    {
        public Button Header { get; } = header;
        public Action Relayout { get; } = relayout;
        public Action<string> SetTitle { get; } = setTitle;
    }
}
