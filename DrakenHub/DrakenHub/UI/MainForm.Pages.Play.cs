using DrakenHub.Core;
using DrakenHub.Models;

namespace DrakenHub.UI;

internal sealed partial class MainForm
{
    private FlowLayoutPanel? _playBindingList;
    private Panel? _playPageScroll;

    private Panel BuildPlayPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        _playPageScroll = scroll;

        var inner = UiFactory.VStack();
        var card = UiFactory.SectionCard(
            _loc.T("play.title"),
            _loc.T("play.subtitle"),
            inner,
            out var titleLabel,
            out var subtitleLabel);
        UiFactory.Add(stack, card);

        _playBindingList = UiFactory.VStack();
        UiFactory.Add(inner, _playBindingList);

        void RebuildList()
        {
            if (_playBindingList == null) return;
            _playBindingList.Controls.Clear();

            var entries = BindingMapCollector.Collect(_settings.Current, includeMuted: false);
            var groupOrder = new List<string>();
            foreach (var e in entries)
            {
                if (!groupOrder.Contains(e.GroupKey))
                    groupOrder.Add(e.GroupKey);
            }

            foreach (var groupKey in groupOrder)
            {
                var header = new Label
                {
                    Text = _loc.T(groupKey),
                    AutoSize = false,
                    Height = 26,
                    ForeColor = Theme.TextSubtle,
                    Font = Theme.UiFontSmall,
                    TextAlign = ContentAlignment.BottomLeft,
                    Padding = new Padding(36, 0, 0, 2),
                    Margin = new Padding(0, 8, 0, 2),
                    BackColor = Color.Transparent
                };
                _playBindingList.Controls.Add(header);

                foreach (var entry in entries.Where(e => e.GroupKey == groupKey))
                {
                    var label = string.IsNullOrWhiteSpace(entry.LabelOverride)
                        ? _loc.T(entry.LabelKey)
                        : entry.LabelOverride;
                    var key = KeyNames.Parse(entry.KeyName);
                    var display = key == Keys.None ? _loc.T("not.set") : KeyNames.Display(entry.KeyName);
                    var conflict = entry.IsConflict;
                    if (conflict)
                        display += " · " + _loc.T("bind.conflict");

                    var row = new SettingRow(SettingRowKind.Hotkey, label, display);
                    row.LabelText = label;
                    row.ValueDanger = conflict;
                    row.ValueText = display;

                    var captured = entry;
                    row.Activated += (_, _) =>
                    {
                        row.ValueText = _loc.T("pressAny");
                        row.ValueDanger = false;
                        _hotkeys.BeginCapture(keyPressed =>
                        {
                            TryAssignHotkey(keyPressed, captured.Assign, captured.Id);
                            _refreshBindingMap?.Invoke();
                        });
                    };

                    _playBindingList.Controls.Add(row);
                }
            }
        }

        _refreshBindingMap = () =>
        {
            RebuildList();
            if (_playPageScroll != null)
                UiFactory.Relayout(_playPageScroll);
        };

        void RefreshChrome()
        {
            titleLabel.Text = _loc.T("play.title");
            if (subtitleLabel != null)
                subtitleLabel.Text = _loc.T("play.subtitle");
        }

        _refreshAllLabels += () =>
        {
            RefreshChrome();
            _refreshBindingMap?.Invoke();
        };

        RefreshChrome();
        RebuildList();
        return scroll;
    }
}