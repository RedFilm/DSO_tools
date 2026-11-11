namespace WalkerRouteBuilder;

/// <summary>Время застревания и клавиши, если обход кругом не сдвинул точку.</summary>
internal sealed class StuckRecoveryForm : Form
{
    private readonly NumericUpDown _time = new()
    {
        Minimum = 0,
        Maximum = 120_000,
        Increment = 100,
        Width = 90,
    };

    private readonly NumericUpDown _shift = new()
    {
        Minimum = 1,
        Maximum = 80,
        Increment = 1,
        Width = 90,
    };

    private readonly TextBox _keys = new()
    {
        Width = 280,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.FromArgb(22, 22, 22),
        ForeColor = Color.White,
    };

    public StuckRecoveryForm(int milliseconds, int shiftPixels, IReadOnlyList<string> keys)
    {
        Text = "Застревание";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(460, 254);
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F);

        var timeLabel = Label("Ждать без сдвига, мс");
        timeLabel.Location = new Point(16, 18);
        _time.Location = new Point(190, 16);
        _time.Value = Math.Clamp(milliseconds, (int)_time.Minimum, (int)_time.Maximum);
        _time.BackColor = Color.FromArgb(22, 22, 22);
        _time.ForeColor = Color.White;

        var timeHint = Label("0 — не выводить из застревания. Иначе обычные клики по кругу, как при движении.");
        timeHint.Location = new Point(16, 46);
        timeHint.Size = new Size(428, 32);

        var shiftLabel = Label("Сдвиг перед перестройкой, px");
        shiftLabel.Location = new Point(16, 84);
        shiftLabel.Size = new Size(210, 18);
        _shift.Location = new Point(230, 82);
        _shift.Value = Math.Clamp(shiftPixels, (int)_shift.Minimum, (int)_shift.Maximum);
        _shift.BackColor = Color.FromArgb(22, 22, 22);
        _shift.ForeColor = Color.White;

        var shiftHint = Label("Пиксели карты. Меньший сдвиг не считается, маршрут не перестраивается.");
        shiftHint.Location = new Point(16, 110);
        shiftHint.Size = new Size(428, 18);

        var keysLabel = Label("Клавиши, если не сдвинулся");
        keysLabel.Location = new Point(16, 136);
        _keys.Location = new Point(16, 160);
        _keys.Text = string.Join(", ", keys);

        var keysHint = Label("По порядку, через запятую: Space, D1. Цифра 1 — клавиша 1. Пусто — ничего не жать.");
        keysHint.Location = new Point(16, 188);
        keysHint.Size = new Size(428, 18);

        var ok = Button("Сохранить", 250, DialogResult.OK);
        var cancel = Button("Отмена", 348, DialogResult.Cancel);
        ok.Location = new Point(250, 214);
        cancel.Location = new Point(348, 214);
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, _) =>
        {
            if (FollowConfig.TryParseRecoveryKeys(_keys.Text, out _, out string? error))
                return;
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        };

        Controls.Add(timeLabel);
        Controls.Add(_time);
        Controls.Add(timeHint);
        Controls.Add(shiftLabel);
        Controls.Add(_shift);
        Controls.Add(shiftHint);
        Controls.Add(keysLabel);
        Controls.Add(_keys);
        Controls.Add(keysHint);
        Controls.Add(ok);
        Controls.Add(cancel);
    }

    public int Milliseconds => (int)_time.Value;

    public int ShiftPixels => (int)_shift.Value;

    public string KeysText => _keys.Text;

    private Label Label(string text) => new()
    {
        AutoSize = false,
        Text = text,
        ForeColor = Color.FromArgb(210, 210, 210),
        Size = new Size(170, 18),
    };

    private Button Button(string text, int x, DialogResult result) => new()
    {
        Text = text,
        DialogResult = result,
        Location = new Point(x, 170),
        Size = new Size(96, 28),
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(48, 48, 48),
        ForeColor = Color.White,
    };
}
