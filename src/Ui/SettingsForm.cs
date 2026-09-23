using BlackMidiVisualizer.Midi;

namespace BlackMidiVisualizer;

public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly ComboBox _devices = new();
    private readonly TrackBar _speed = new();
    private readonly TrackBar _maxNotes = new();
    private readonly TrackBar _flood = new();
    private readonly ComboBox _keys = new();
    private readonly CheckBox _hud = new();
    private readonly CheckBox _vsync = new();
    private readonly Label _speedVal = new();
    private readonly Label _maxVal = new();
    private readonly Label _floodVal = new();

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Text = "Settings — Black MIDI Visualizer";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 420);
        BackColor = Color.FromArgb(18, 18, 20);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9.5f);

        int y = 18;
        AddLabel("MIDI output", 20, y);
        _devices.DropDownStyle = ComboBoxStyle.DropDownList;
        _devices.SetBounds(20, y + 22, 420, 28);
        _devices.Items.AddRange(MidiOutput.ListDevices());
        if (_devices.Items.Count == 0) _devices.Items.Add("(no devices)");
        _devices.SelectedIndex = Math.Clamp(settings.MidiDeviceIndex, 0, _devices.Items.Count - 1);
        Controls.Add(_devices);
        y += 64;

        AddLabel("Keyboard", 20, y);
        _keys.DropDownStyle = ComboBoxStyle.DropDownList;
        _keys.Items.AddRange(new object[] { "88 keys (A0–C8)", "128 keys (full MIDI)" });
        _keys.SelectedIndex = settings.KeyboardMode >= 128 ? 1 : 0;
        _keys.SetBounds(20, y + 22, 420, 28);
        Controls.Add(_keys);
        y += 64;

        AddLabel("Note size (toolbar Size, 0.08–2.00)", 20, y);
        _speed.SetBounds(20, y + 22, 340, 30);
        _speed.Minimum = 8; _speed.Maximum = 200; _speed.TickFrequency = 10;
        _speed.Value = (int)Math.Clamp(settings.ViewSize * 100, 8, 200);
        _speed.ValueChanged += (_, _) => _speedVal.Text = $"{_speed.Value / 100.0:0.00}";
        _speedVal.SetBounds(370, y + 24, 70, 24);
        _speedVal.Text = $"{_speed.Value / 100.0:0.00}";
        Controls.Add(_speed); Controls.Add(_speedVal);
        y += 70;

        AddLabel("Max visible notes uploaded per frame", 20, y);
        _maxNotes.SetBounds(20, y + 22, 340, 30);
        _maxNotes.Minimum = 20; _maxNotes.Maximum = 500; _maxNotes.TickFrequency = 20;
        _maxNotes.Value = Math.Clamp(settings.MaxVisibleNotes / 1000, 20, 500);
        _maxNotes.ValueChanged += (_, _) => _maxVal.Text = $"{_maxNotes.Value}k";
        _maxVal.SetBounds(370, y + 24, 70, 24);
        _maxVal.Text = $"{_maxNotes.Value}k";
        Controls.Add(_maxNotes); Controls.Add(_maxVal);
        y += 70;

        AddLabel("MIDI flood cap (events / ms, protects audio thread)", 20, y);
        _flood.SetBounds(20, y + 22, 340, 30);
        _flood.Minimum = 16; _flood.Maximum = 512;
        _flood.Value = Math.Clamp(settings.MidiFloodCapPerMs, 16, 512);
        _flood.ValueChanged += (_, _) => _floodVal.Text = _flood.Value.ToString();
        _floodVal.SetBounds(370, y + 24, 70, 24);
        _floodVal.Text = _flood.Value.ToString();
        Controls.Add(_flood); Controls.Add(_floodVal);
        y += 64;

        _hud.Text = "Show HUD";
        _hud.Checked = settings.ShowHud;
        _hud.SetBounds(20, y, 160, 24);
        _hud.ForeColor = ForeColor;
        _vsync.Text = "VSync";
        _vsync.Checked = settings.VSync;
        _vsync.SetBounds(200, y, 120, 24);
        _vsync.ForeColor = ForeColor;
        Controls.Add(_hud); Controls.Add(_vsync);

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK };
        ok.SetBounds(250, 370, 90, 32);
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        cancel.SetBounds(350, 370, 90, 32);
        ok.Click += (_, _) => Apply();
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void AddLabel(string text, int x, int y)
    {
        var l = new Label { Text = text, AutoSize = true, ForeColor = Color.Silver };
        l.Location = new Point(x, y);
        Controls.Add(l);
    }

    private void Apply()
    {
        _settings.MidiDeviceIndex = Math.Max(0, _devices.SelectedIndex);
        _settings.KeyboardMode = _keys.SelectedIndex == 1 ? 128 : 88;
        _settings.ViewSize = _speed.Value / 100f;
        _settings.MaxVisibleNotes = _maxNotes.Value * 1000;
        _settings.MidiFloodCapPerMs = _flood.Value;
        _settings.ShowHud = _hud.Checked;
        _settings.VSync = _vsync.Checked;
    }
}
