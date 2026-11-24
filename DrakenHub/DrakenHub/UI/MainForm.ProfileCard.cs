using DrakenHub.Core;

namespace DrakenHub.UI;

internal sealed partial class MainForm
{
    /// <summary>Collapsible header+body block that always calls UiFactory.Relayout on the scroll host.</summary>
    private Button MakeDeleteButton(Action remove)
    {
        var btn = UiFactory.Btn(_loc.T("delete"), BtnKind.Danger, 32);
        btn.Width = WidestClickerLabel("delete") + 20;
        btn.Click += (_, _) =>
        {
            if (!ConfirmAction("confirm.delete")) return;
            remove();
        };
        _refreshAllLabels += () =>
        {
            if (!btn.IsDisposed)
                btn.Text = _loc.T("delete");
        };
        return btn;
    }

    private static Panel BuildCollapsibleBlock(
        string title,
        Control body,
        Control layoutHost,
        Control? trailingHeaderControl = null,
        Control? deleteControl = null)
    {
        var startExpanded = body.Visible;
        var headerH = Math.Max(36, Theme.UiFont.Height + 14);
        var headerBtn = UiFactory.Btn((startExpanded ? "▾ " : "▸ ") + title, BtnKind.Neutral, headerH);
        headerBtn.Font = Theme.UiFontValue;
        headerBtn.BackColor = Theme.Bg;
        headerBtn.ForeColor = Theme.Text;
        headerBtn.FlatAppearance.BorderColor = Theme.HeaderLine;
        headerBtn.TextAlign = ContentAlignment.MiddleLeft;

        Control headerRow;
        if (trailingHeaderControl == null && deleteControl == null)
        {
            headerBtn.Margin = Padding.Empty;
            headerRow = headerBtn;
        }
        else
        {
            var cells = new List<(Control Control, int Width)> { (headerBtn, 0) };
            if (trailingHeaderControl != null)
                cells.Add((trailingHeaderControl, 0));
            if (deleteControl != null)
                cells.Add((deleteControl, Math.Max(96, deleteControl.Width)));
            headerRow = UiFactory.ActionRow(cells.ToArray());
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
