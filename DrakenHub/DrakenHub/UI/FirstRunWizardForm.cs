using DrakenHub.Core;
using DrakenHub.Models;
using DrakenHub.Services;

namespace DrakenHub.UI;

internal sealed class FirstRunWizardForm : Form
{
    private readonly Localization _loc;
    private readonly SettingsService _settings;
    private readonly Func<int, bool, ScreenPoint?> _pickCoord;
    private readonly Action _onChanged;
    private int _step;
    private bool _captureHotkey;
    private readonly Label _title = new();
    private readonly Label _body = new();
    private readonly Panel _actions = new();
    private readonly Button _primary;
    private readonly Button _secondary;
    private readonly Button _skip;

    public FirstRunWizardForm(
        Localization loc,
        SettingsService settings,
        Func<int, bool, ScreenPoint?> pickCoord,
        Action onChanged)
    {
        _loc = loc;
        _settings = settings;
        _pickCoord = pickCoord;
        _onChanged = onChanged;

        Text = _loc.T("wizard.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;
        ClientSize = new Size(480, 260);
        KeyPreview = true;

        _title.Font = Theme.UiFontTitle;
        _title.ForeColor = Theme.Accent;
        _title.Dock = DockStyle.Top;
        _title.Height = 40;
        _title.Padding = new Padding(16, 12, 16, 0);

        _body.ForeColor = Theme.Text;
        _body.Dock = DockStyle.Fill;
        _body.Padding = new Padding(16, 8, 16, 8);

        _actions.Dock = DockStyle.Bottom;
        _actions.Height = 56;
        _actions.Padding = new Padding(12, 8, 12, 12);
        _actions.BackColor = Theme.Panel;

        _skip = UiFactory.Btn(_loc.T("wizard.skip"), BtnKind.Neutral);
        _secondary = UiFactory.Btn("", BtnKind.Warn);
        _primary = UiFactory.Btn("", BtnKind.Primary);
        _skip.Width = 110;
        _secondary.Width = 160;
        _primary.Width = 160;
        _skip.Click += (_, _) => Close();
        _secondary.Click += (_, _) => OnSecondary();
        _primary.Click += (_, _) => OnPrimary();

        _actions.Controls.Add(_primary);
        _actions.Controls.Add(_secondary);
        _actions.Controls.Add(_skip);
        _actions.Resize += (_, _) => LayoutActions();

        Controls.Add(_body);
        Controls.Add(_actions);
        Controls.Add(_title);
        KeyDown += OnWizardKeyDown;
        ShowStep(0);
    }

    private void LayoutActions()
    {
        var y = 10;
        _skip.SetBounds(12, y, 110, 34);
        _primary.SetBounds(_actions.ClientSize.Width - 172, y, 160, 34);
        _secondary.SetBounds(_primary.Left - 168, y, 160, 34);
    }

    private void ShowStep(int step)
    {
        _step = step;
        _captureHotkey = false;
        _title.Text = _loc.T("wizard.title") + $" ({step + 1}/3)";
        switch (step)
        {
            case 0:
                _body.Text = _loc.T("wizard.step1");
                _secondary.Text = _loc.T("wizard.pickCell");
                _secondary.Visible = true;
                _primary.Text = _loc.T("wizard.next");
                break;
            case 1:
                _body.Text = _loc.T("wizard.step2");
                _secondary.Text = _loc.T("wizard.toggleGrid");
                _secondary.Visible = true;
                _primary.Text = _loc.T("wizard.next");
                break;
            default:
                _body.Text = _loc.T("wizard.step3");
                _secondary.Text = _loc.T("wizard.assignHotkey");
                _secondary.Visible = true;
                _primary.Text = _loc.T("wizard.done");
                break;
        }
        LayoutActions();
    }

    private void OnSecondary()
    {
        var inv = _settings.Current.Inventory;
        switch (_step)
        {
            case 0:
            {
                var p = _pickCoord(Math.Max(16, inv.Grid.CellSize), true);
                if (p != null)
                {
                    inv.FirstCell = p;
                    _onChanged();
                }
                break;
            }
            case 1:
                inv.Grid.ShowGrid = !inv.Grid.ShowGrid;
                _onChanged();
                break;
            default:
                _captureHotkey = true;
                _secondary.Text = _loc.T("pressAny");
                break;
        }
    }

    private void OnWizardKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_captureHotkey) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        _captureHotkey = false;
        if (e.KeyCode == Keys.Escape)
            _settings.Current.Inventory.SellAllHotkey = null;
        else
            _settings.Current.Inventory.SellAllHotkey = KeyNames.Format(e.KeyCode);
        _onChanged();
        _secondary.Text = _loc.T("wizard.assignHotkey");
    }

    private void OnPrimary()
    {
        if (_step < 2)
            ShowStep(_step + 1);
        else
            Close();
    }
}
