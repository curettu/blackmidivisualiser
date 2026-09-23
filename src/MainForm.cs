using System.Diagnostics;
using BlackMidiVisualizer.Midi;
using BlackMidiVisualizer.Render;

namespace BlackMidiVisualizer;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings = new();
    private readonly MidiPlayer _player = new();
    private readonly HudInfo _hud = new();
    private D3DRenderer? _renderer;
    private MidiSong? _song;
    private bool _ready;
    private string _status = "Choose a MIDI file";
    private readonly Stopwatch _fpsClock = Stopwatch.StartNew();
    private int _fpsFrames;
    private int _fps;
    private int _npsWindow;
    private int _lastNotesPlayed;
    private long _npsStamp = Stopwatch.GetTimestamp();
    private string? _drag;
    private Point _lastMouse;

    public MainForm()
    {
        Text = "Black MIDI Visualizer — Own Edition";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1600, 900);
        MinimumSize = new Size(960, 540);
        BackColor = Color.Black;
        DoubleBuffered = false;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
        KeyPreview = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            _renderer = new D3DRenderer(Handle, ClientSize.Width, ClientSize.Height);
            try { _player.SetDevice(_settings.MidiDeviceIndex); }
            catch (Exception ex) { _status = ex.Message; }
            _ready = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "Direct3D 11 init failed");
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_ready && WindowState != FormWindowState.Minimized)
            _renderer?.Resize(ClientSize.Width, ClientSize.Height);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _player.Dispose();
        _renderer?.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _lastMouse = e.Location;
        if (_renderer is null) return;

        if (_drag is "speed" or "speedThumb")
        {
            _settings.PlaybackSpeed = (float)Math.Round(_renderer.Chrome.HitToSpeed(e.X) * 100) / 100f;
            _player.SetRate(_settings.PlaybackSpeed);
        }
        else if (_drag is "size" or "sizeThumb")
        {
            _settings.ViewSize = (float)Math.Round(_renderer.Chrome.HitToSize(e.X) * 100) / 100f;
        }
        else if (_drag == "seek")
        {
            SeekFromX(e.X);
        }
        else
        {
            string hover = "";
            foreach (var hit in _renderer.Hits)
            {
                if (hit.Rect.Contains(e.Location)) { hover = hit.Id; break; }
            }
            _renderer.HoverId = hover;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_renderer is null || e.Button != MouseButtons.Left) return;
        foreach (var hit in _renderer.Hits)
        {
            if (!hit.Rect.Contains(e.Location)) continue;
            switch (hit.Id)
            {
                case "open": ChooseFile(); break;
                case "play": if (!_player.Playing) TogglePlay(); break;
                case "pause": if (_player.Playing) _player.Pause(); else _player.Stop(); break;
                case "settings": OpenSettings(); break;
                case "speed":
                case "speedThumb":
                    _drag = hit.Id;
                    _settings.PlaybackSpeed = (float)Math.Round(_renderer.Chrome.HitToSpeed(e.X) * 100) / 100f;
                    _player.SetRate(_settings.PlaybackSpeed);
                    break;
                case "size":
                case "sizeThumb":
                    _drag = hit.Id;
                    _settings.ViewSize = (float)Math.Round(_renderer.Chrome.HitToSize(e.X) * 100) / 100f;
                    break;
                case "speedUp": NudgeSpeed(0.05f); break;
                case "speedDown": NudgeSpeed(-0.05f); break;
                case "sizeUp": NudgeSize(0.02f); break;
                case "sizeDown": NudgeSize(-0.02f); break;
                case "speedHelp":
                    MessageBox.Show(this, "Playback speed. 1 is real time. Does not change pitch of a hardware/GS synth the same way a sampler would — it simply runs the MIDI clock faster or slower.", "Speed");
                    break;
                case "sizeHelp":
                    MessageBox.Show(this, "Note size / how much MIDI time fits on screen. Smaller size = taller notes (less time visible), like Kiva.", "Size");
                    break;
                case "seek":
                    _drag = "seek";
                    SeekFromX(e.X);
                    break;
            }
            break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = null;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_renderer is null) return;
        if (_renderer.HoverId is "speed" or "speedThumb" or "speedUp" or "speedDown")
            NudgeSpeed(e.Delta > 0 ? 0.05f : -0.05f);
        else if (_renderer.HoverId is "size" or "sizeThumb" or "sizeUp" or "sizeDown")
            NudgeSize(e.Delta > 0 ? 0.02f : -0.02f);
    }

    private void NudgeSpeed(float d)
    {
        _settings.PlaybackSpeed = Math.Clamp((float)Math.Round((_settings.PlaybackSpeed + d) * 100) / 100f, 0.25f, 4f);
        _player.SetRate(_settings.PlaybackSpeed);
    }

    private void NudgeSize(float d)
        => _settings.ViewSize = Math.Clamp((float)Math.Round((_settings.ViewSize + d) * 100) / 100f, 0.08f, 2f);

    private void SeekFromX(int x)
    {
        if (_song is null) return;
        float t = Math.Clamp(x / (float)Math.Max(ClientSize.Width, 1), 0, 1);
        _player.Seek(t * _song.Duration);
        _renderer?.ResetCollector();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space) TogglePlay();
        else if (e.KeyCode == Keys.Escape) _player.Stop();
        else if (e.KeyCode == Keys.O && e.Control) ChooseFile();
        else if (e.KeyCode == Keys.S && e.Control) OpenSettings();
        else if (e.KeyCode == Keys.Left) _player.Seek(_player.SongTime - 5);
        else if (e.KeyCode == Keys.Right) _player.Seek(_player.SongTime + 5);
        else if (e.KeyCode == Keys.OemOpenBrackets) NudgeSize(-0.02f);
        else if (e.KeyCode == Keys.OemCloseBrackets) NudgeSize(0.02f);
        else if (e.KeyCode == Keys.F11)
        {
            if (FormBorderStyle == FormBorderStyle.None)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
        }
    }

    public void Frame()
    {
        if (!_ready || _renderer is null || WindowState == FormWindowState.Minimized)
        {
            Thread.Sleep(15);
            return;
        }

        _fpsFrames++;
        if (_fpsClock.ElapsedMilliseconds >= 500)
        {
            _fps = (int)(_fpsFrames * 1000.0 / _fpsClock.ElapsedMilliseconds);
            _fpsFrames = 0;
            _fpsClock.Restart();
        }

        long now = Stopwatch.GetTimestamp();
        double dt = (now - _npsStamp) / (double)Stopwatch.Frequency;
        if (dt >= 0.25)
        {
            int played = _player.NotesPlayed;
            _npsWindow = (int)((played - _lastNotesPlayed) / dt);
            _lastNotesPlayed = played;
            _npsStamp = now;
        }

        double t = _player.SongTime;
        _hud.FileName = _song?.FileName ?? _status;
        _hud.Time = t;
        _hud.Duration = _song?.Duration ?? 0;
        _hud.NoteCount = _song?.UniqueNoteCount ?? 0;
        _hud.Nps = Math.Max(0, _npsWindow);
        _hud.Fps = _fps;
        _hud.Polyphony = _player.Polyphony;
        _hud.Tracks = _song?.TrackCount ?? 0;
        _hud.Passed = _player.NotesPlayed;
        _hud.Playing = _player.Playing;

        _renderer.Render(_song, _settings, t, _hud, _settings.VSync);
        _ = _lastMouse;
    }

    private void TogglePlay()
    {
        if (_song is null) { ChooseFile(); return; }
        if (_player.Playing) _player.Pause();
        else _player.Play();
    }

    private void ChooseFile()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "MIDI files (*.mid;*.midi)|*.mid;*.midi|All files (*.*)|*.*",
            Title = "Choose MIDI File"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        LoadMidi(dlg.FileName);
    }

    private void LoadMidi(string path)
    {
        _status = "Parsing…";
        _player.Stop();
        try
        {
            var song = MidiParser.Parse(path);
            _song = song;
            _player.Load(song);
            _renderer?.ResetCollector();
            _status = song.FileName;
        }
        catch (Exception ex)
        {
            _song = null;
            _status = "Failed to parse MIDI";
            MessageBox.Show(this, ex.Message, "MIDI parse error");
        }
    }

    private void OpenSettings()
    {
        using var f = new SettingsForm(_settings);
        if (f.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                _player.SetDevice(_settings.MidiDeviceIndex);
                _player.SetFloodCap(_settings.MidiFloodCapPerMs);
                _player.SetRate(_settings.PlaybackSpeed);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "MIDI device");
            }
        }
    }
}
