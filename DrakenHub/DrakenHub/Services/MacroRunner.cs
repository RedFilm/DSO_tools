using DrakenHub.Core;
using DrakenHub.Models;

namespace DrakenHub.Services;

internal sealed class MacroRunner
{
    private readonly SettingsService _settings;
    private readonly InputService _input;
    private readonly ColorScanService _scan;
    private CancellationTokenSource? _cts;
    private readonly object _gate = new();

    public bool IsRunning
    {
        get { lock (_gate) return _cts is { IsCancellationRequested: false }; }
    }

    public MacroRunner(SettingsService settings, InputService input)
    {
        _settings = settings;
        _input = input;
        _scan = new ColorScanService(input);
    }

    public void EmergencyStop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts = null;
        }
    }

    public void Run(Func<CancellationToken, Task> work)
    {
        CancellationToken token;
        lock (_gate)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        _ = Task.Run(async () =>
        {
            try { await work(token); }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_gate)
                {
                    if (_cts?.Token == token)
                        _cts = null;
                }
            }
        }, token);
    }

    public void SellAll()
    {
        Run(async token =>
        {
            var inv = _settings.Current.Inventory;
            OpenShop(inv);
            await Delay(inv.Grid.SellClickDelayMs * 4, token);
            await SellSelectedTabs(inv, includeOther: true, token);
            if (inv.CloseInventory != null)
            {
                _input.LeftClick(inv.CloseInventory.X, inv.CloseInventory.Y);
            }
        });
    }

    public void SellCurrent()
    {
        Run(async token =>
        {
            var inv = _settings.Current.Inventory;
            await ScanAndSellTab(inv, includeOther: true, token);
        });
    }

    public void Smelt()
    {
        Run(async token =>
        {
            var inv = _settings.Current.Inventory;
            var delay = Math.Max(20, inv.Grid.SellClickDelayMs);
            Press(inv.InventoryHotkey);
            await Delay(delay * 3, token);
            ClickTab(inv, 0);
            await Delay(delay, token);
            if (inv.SmelterItem != null)
            {
                _input.RightClick(inv.SmelterItem.X, inv.SmelterItem.Y);
                await Delay(delay * 4, token);
            }

            await SellSelectedTabs(inv, includeOther: false, token, clickInsteadOfSell: true);

            if (inv.SmeltButton != null)
            {
                _input.LeftClick(inv.SmeltButton.X, inv.SmeltButton.Y);
                await Delay(delay * 2, token);
            }

            if (inv.CloseInventory != null)
                _input.LeftClick(inv.CloseInventory.X, inv.CloseInventory.Y);
        });
    }

    public void FastMount()
    {
        Run(async token =>
        {
            var s = _settings.Current;
            var fm = s.FastMount;
            var inv = s.Inventory;
            var delay = Math.Max(10, fm.ClickDelayMs);
            Press(inv.InventoryHotkey ?? s.GearSwap.InventoryHotkey);
            await Delay(delay * 2, token);
            await ClickCape(fm, inv, delay, token);
            Press(fm.MountHotkey);
            await Delay(delay * 2, token);
            Press(inv.InventoryHotkey ?? s.GearSwap.InventoryHotkey);
            await Delay(delay * 2, token);
            await ClickCape(fm, inv, delay, token);
        });
    }

    public void JumpMount()
    {
        Run(async token =>
        {
            var s = _settings.Current;
            var fm = s.FastMount;
            var inv = s.Inventory;
            var delay = Math.Max(10, fm.ClickDelayMs);
            if (!string.IsNullOrEmpty(fm.JumpTargetPanelHotkey))
            {
                Press(fm.JumpTargetPanelHotkey);
                await Delay(delay, token);
            }
            Press(fm.JumpSkillHotkey);
            await Delay(delay, token);
            Press(fm.MountHotkey);
            await Delay(delay, token);
            Press(fm.MainPanelHotkey);
            await Delay(delay, token);
            Press(inv.InventoryHotkey);
            await Delay(delay * 2, token);
            await ClickCape(fm, inv, delay, token);
        });
    }

    public void ExtraSkill(ExtraSkillProfile profile)
    {
        Run(async token =>
        {
            var delay = 25;
            var main = _settings.Current.ExtraSkill.MainPanelHotkey;
            Press(profile.TargetPanelHotkey);
            await Delay(delay, token);
            Press(profile.SkillSlotHotkey);
            await Delay(delay, token);
            Press(main);
        });
    }

    public void GearSwap(GearSwapProfile profile)
    {
        Run(async token =>
        {
            var s = _settings.Current;
            var delay = Math.Max(10, s.GearSwap.ClickDelayMs);
            var inv = s.Inventory;
            Press(s.GearSwap.InventoryHotkey ?? inv.InventoryHotkey);
            await Delay(delay * 2, token);
            ClickTab(inv, Math.Max(0, profile.InventoryTab - 1));
            await Delay(delay, token);
            foreach (var item in profile.Items)
            {
                var center = InventoryLayout.CellCenter(inv, item.Row - 1, item.Col - 1);
                if (center != null)
                {
                    _input.RightClick(center.Value.X, center.Value.Y);
                    await Delay(delay, token);
                }
            }
            Press(s.GearSwap.InventoryHotkey ?? inv.InventoryHotkey);
            await Delay(delay, token);
            if (!string.IsNullOrEmpty(profile.TargetPanelHotkey))
            {
                Press(profile.TargetPanelHotkey);
                await Delay(delay, token);
            }
            Press(profile.SkillSlotHotkey);
            await Delay(delay, token);
            Press(s.GearSwap.InventoryHotkey ?? inv.InventoryHotkey);
            await Delay(delay * 2, token);
            ClickTab(inv, Math.Max(0, profile.InventoryTab - 1));
            await Delay(delay, token);
            foreach (var item in profile.Items)
            {
                var center = InventoryLayout.CellCenter(inv, item.Row - 1, item.Col - 1);
                if (center != null)
                {
                    _input.RightClick(center.Value.X, center.Value.Y);
                    await Delay(delay, token);
                }
            }
            Press(s.GearSwap.MainPanelHotkey);
        });
    }

    public void Teleport(TeleportLocation loc)
    {
        Run(async token =>
        {
            var tp = _settings.Current.FastTeleport;
            var inv = _settings.Current.Inventory;
            var delay = Math.Max(20, tp.ClickDelayMs);
            Press(inv.InventoryHotkey);
            await Delay(delay * 2, token);
            if (tp.MapItem != null)
            {
                _input.RightClick(tp.MapItem.X, tp.MapItem.Y);
                await Delay(delay * 4, token);
            }
            if (loc.Region != null)
            {
                _input.LeftClick(loc.Region.X, loc.Region.Y);
                await Delay(delay * 2, token);
            }
            if (loc.Location != null)
            {
                _input.LeftClick(loc.Location.X, loc.Location.Y);
                await Delay(delay * 2, token);
            }
            if (loc.Difficulty != null)
            {
                _input.LeftClick(loc.Difficulty.X, loc.Difficulty.Y);
                await Delay(delay * 2, token);
            }
            if (tp.EnterButton != null)
            {
                _input.LeftClick(tp.EnterButton.X, tp.EnterButton.Y);
            }
        });
    }

    private async Task SellSelectedTabs(InventorySettings inv, bool includeOther, CancellationToken token, bool clickInsteadOfSell = false)
    {
        for (var i = 0; i < inv.SelectedTabs.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!inv.SelectedTabs[i]) continue;
            ClickTab(inv, i);
            await Delay(inv.Grid.SellClickDelayMs * 2, token);
            await ScanAndSellTab(inv, includeOther, token, clickInsteadOfSell);
        }
    }

    private async Task ScanAndSellTab(InventorySettings inv, bool includeOther, CancellationToken token, bool clickInsteadOfSell = false)
    {
        var passes = Math.Clamp(inv.Grid.ScanLevel, 1, 2);
        var delay = Math.Max(10, inv.Grid.SellClickDelayMs);
        for (var pass = 0; pass < passes; pass++)
        {
            var step = pass == 0 && inv.Grid.FastFirstScan ? 1 : 1;
            for (var row = 0; row < inv.Grid.Rows; row += step)
            for (var col = 0; col < inv.Grid.Columns; col += step)
            {
                token.ThrowIfCancellationRequested();
                if (!_scan.CellMatchesAny(inv, row, col, includeOther)) continue;
                var center = InventoryLayout.CellCenter(inv, row, col);
                if (center == null) continue;
                if (clickInsteadOfSell)
                    _input.LeftClick(center.Value.X, center.Value.Y);
                else
                    _input.RightClick(center.Value.X, center.Value.Y);
                await Delay(delay, token);
            }
        }
    }

    private void OpenShop(InventorySettings inv)
    {
        Press(inv.SkillsWindowHotkey);
        Thread.Sleep(80);
        if (inv.WisdomTab != null)
        {
            _input.LeftClick(inv.WisdomTab.X, inv.WisdomTab.Y);
            Thread.Sleep(80);
        }
        if (inv.ShopButton != null)
        {
            _input.LeftClick(inv.ShopButton.X, inv.ShopButton.Y);
            Thread.Sleep(120);
        }
    }

    private void ClickTab(InventorySettings inv, int index)
    {
        var p = InventoryLayout.TabCenter(inv, index);
        if (p != null) _input.LeftClick(p.Value.X, p.Value.Y);
    }

    private async Task ClickCape(FastMountSettings fm, InventorySettings inv, int delay, CancellationToken token)
    {
        if (fm.CapeCell == null) return;
        var center = InventoryLayout.CellCenter(inv, fm.CapeCell.Row - 1, fm.CapeCell.Col - 1);
        if (center == null) return;
        _input.RightClick(center.Value.X, center.Value.Y);
        await Delay(delay, token);
    }

    private void Press(string? keyName)
    {
        var key = KeyNames.Parse(keyName);
        if (key != Keys.None) _input.KeyPress(key);
    }

    private static Task Delay(int ms, CancellationToken token) => Task.Delay(Math.Max(1, ms), token);
}
