using System.Diagnostics;

namespace BlackMidiVisualizer.Midi;

public sealed class MidiPlayer : IDisposable
{
    private readonly MidiOutput _output = new();
    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _run = true;
    private volatile bool _playing;
    private MidiSong? _song;
    private int _eventIndex;
    private double _songTime;
    private long _hostStartStamp;
    private double _hostStartSongTime;
    private int _floodCap = 192;
    private float _rate = 1f;
    public int Polyphony { get; private set; }
    public int NotesPlayed { get; private set; }
    public bool Playing => _playing;
    public float Rate => _rate;
    public double SongTime
    {
        get
        {
            lock (_gate) return CurrentTimeUnlocked();
        }
    }

    public MidiPlayer()
    {
        _thread = new Thread(AudioLoop)
        {
            Name = "BMV-MIDI-Out",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
    }

    public void SetDevice(int index)
    {
        lock (_gate)
        {
            _output.Open(index);
        }
    }

    public void SetFloodCap(int cap) => _floodCap = Math.Max(16, cap);

    public void SetRate(float rate)
    {
        lock (_gate)
        {
            rate = Math.Clamp(rate, 0.1f, 8f);
            if (_playing)
            {
                _songTime = CurrentTimeUnlocked();
                _hostStartSongTime = _songTime;
                _hostStartStamp = Stopwatch.GetTimestamp();
            }
            _rate = rate;
        }
    }

    public void Load(MidiSong song)
    {
        lock (_gate)
        {
            _playing = false;
            _output.Reset();
            _song = song;
            _eventIndex = 0;
            _songTime = 0;
            _hostStartSongTime = 0;
            _hostStartStamp = Stopwatch.GetTimestamp();
            Polyphony = 0;
            NotesPlayed = 0;
        }
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_song is null) return;
            _hostStartSongTime = _songTime;
            _hostStartStamp = Stopwatch.GetTimestamp();
            _playing = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _playing = false;
            _songTime = 0;
            _hostStartSongTime = 0;
            _eventIndex = 0;
            _hostStartStamp = Stopwatch.GetTimestamp();
            _output.Reset();
            Polyphony = 0;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_playing) return;
            _songTime = CurrentTimeUnlocked();
            _playing = false;
            _output.Reset();
            Polyphony = 0;
        }
    }

    public void Seek(double time)
    {
        lock (_gate)
        {
            if (_song is null) return;
            time = Math.Clamp(time, 0, _song.Duration);
            _songTime = time;
            _hostStartSongTime = time;
            _hostStartStamp = Stopwatch.GetTimestamp();
            _eventIndex = LowerBound(_song.PlayEvents, (float)time);
            _output.Reset();
            Polyphony = 0;
        }
    }

    private double CurrentTimeUnlocked()
    {
        if (!_playing) return _songTime;
        double elapsed = (Stopwatch.GetTimestamp() - _hostStartStamp) / (double)Stopwatch.Frequency;
        return _hostStartSongTime + elapsed * _rate;
    }

    private void AudioLoop()
    {
        while (_run)
        {
            MidiPlayEvent[]? events = null;
            int i = 0;
            double now = 0;
            bool playing;
            int cap;
            lock (_gate)
            {
                playing = _playing;
                cap = _floodCap;
                if (playing && _song is not null)
                {
                    now = CurrentTimeUnlocked();
                    _songTime = now;
                    events = _song.PlayEvents;
                    i = _eventIndex;
                    if (now >= _song.Duration && i >= events.Length)
                    {
                        _playing = false;
                        _songTime = _song.Duration;
                        _output.Reset();
                        playing = false;
                    }
                }
            }

            if (!playing || events is null)
            {
                Thread.Sleep(4);
                continue;
            }

            int sentThisMs = 0;
            int poly = Polyphony;
            while (i < events.Length && events[i].Time <= now)
            {
                uint msg = events[i].Message;
                byte cmd = (byte)(msg & 0xF0);
                bool noteOn = cmd == 0x90 && ((msg >> 16) & 0xFF) != 0;
                bool noteOff = cmd == 0x80 || (cmd == 0x90 && ((msg >> 16) & 0xFF) == 0);

                if (noteOn && sentThisMs >= cap)
                {
                    i++;
                    continue;
                }

                _output.ShortMsg(msg);
                sentThisMs++;
                if (noteOn)
                {
                    poly++;
                    NotesPlayed++;
                }
                else if (noteOff && poly > 0) poly--;
                i++;
            }
            Polyphony = poly;

            lock (_gate)
            {
                if (_playing)
                    _eventIndex = i;
            }

            double nextT = i < events.Length ? events[i].Time : now + 0.05;
            double wait = nextT - now;
            if (wait > 0.004)
                Thread.Sleep(1);
            else
                Thread.SpinWait(200);
        }
    }

    private static int LowerBound(MidiPlayEvent[] arr, float time)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (arr[mid].Time < time) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    public void Dispose()
    {
        _run = false;
        _thread?.Join(500);
        _output.Dispose();
    }
}
