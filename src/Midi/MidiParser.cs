using System.Buffers.Binary;

namespace BlackMidiVisualizer.Midi;

public static class MidiParser
{
    public static MidiSong Parse(string path)
        => Parse(File.ReadAllBytes(path), path);

    public static MidiSong Parse(ReadOnlySpan<byte> data, string path)
    {
        if (data.Length < 14 || !Eq(data, "MThd"))
            throw new InvalidDataException("Not a Standard MIDI File (missing MThd).");

        int o = 4;
        int headerLen = ReadU32(data, ref o);
        int format = ReadU16(data, ref o);
        int ntrks = ReadU16(data, ref o);
        int division = ReadU16(data, ref o);
        o = 8 + headerLen;
        if (division <= 0)
            throw new InvalidDataException("SMPTE MIDI timing is not supported.");
        if (format > 2)
            throw new InvalidDataException($"Unsupported MIDI format {format}.");

        var all = new List<RawEvent>[ntrks];
        var tempos = new List<(long tick, int uspq)>(32);

        int parsedTracks = 0;
        for (int t = 0; t < ntrks && o + 8 <= data.Length; t++)
        {
            if (!Eq(data.Slice(o), "MTrk")) break;
            o += 4;
            int len = ReadU32(data, ref o);
            int end = Math.Min(o + len, data.Length);
            all[t] = ParseTrack(data.Slice(o, end - o), t, tempos);
            o = end;
            parsedTracks++;
        }

        tempos.Sort((a, b) => a.tick.CompareTo(b.tick));
        var tempoMap = BuildTempoMap(tempos, division);

        var notes = new List<MidiNote>(65536);
        var play = new List<MidiPlayEvent>(65536);
        var ons = new Stack<(float time, byte vel, byte track)>[16 * 128];
        for (int i = 0; i < ons.Length; i++)
            ons[i] = new Stack<(float, byte, byte)>(2);

        for (int t = 0; t < all.Length; t++)
        {
            var evs = all[t];
            if (evs is null) continue;
            long tick = 0;
            foreach (var ev in evs)
            {
                tick += ev.Delta;
                float time = ToSeconds(tick, tempoMap);
                byte status = ev.Status;
                if (status >= 0xF0) continue;

                byte cmd = (byte)(status & 0xF0);
                byte ch = (byte)(status & 0x0F);
                play.Add(new MidiPlayEvent { Time = time, Message = status | ((uint)ev.Data1 << 8) | ((uint)ev.Data2 << 16) });

                if (cmd == 0x90 && ev.Data2 > 0)
                {
                    ons[ch * 128 + ev.Data1].Push((time, ev.Data2, (byte)Math.Min(t, 255)));
                }
                else if (cmd == 0x80 || (cmd == 0x90 && ev.Data2 == 0))
                {
                    var stack = ons[ch * 128 + ev.Data1];
                    if (stack.Count == 0) continue;
                    var on = stack.Pop();
                    notes.Add(new MidiNote
                    {
                        Start = on.time,
                        End = time <= on.time ? on.time + 0.03f : time,
                        Key = ev.Data1,
                        Velocity = on.vel,
                        Track = on.track,
                        Channel = ch
                    });
                }
            }
        }

        float duration = 0;
        for (int slot = 0; slot < ons.Length; slot++)
        {
            while (ons[slot].Count > 0)
            {
                var on = ons[slot].Pop();
                float end = on.time + 0.5f;
                notes.Add(new MidiNote
                {
                    Start = on.time,
                    End = end,
                    Key = (byte)(slot % 128),
                    Velocity = on.vel,
                    Track = on.track,
                    Channel = (byte)(slot / 128)
                });
            }
        }

        var noteArr = notes.ToArray();
        Array.Sort(noteArr, static (a, b) => a.Start.CompareTo(b.Start));

        var byKeyLists = new List<MidiNote>[128];
        var maxDur = new float[128];
        for (int k = 0; k < 128; k++)
            byKeyLists[k] = new List<MidiNote>();

        foreach (var n in noteArr)
        {
            byKeyLists[n.Key].Add(n);
            float d = n.End - n.Start;
            if (d > maxDur[n.Key]) maxDur[n.Key] = d;
            if (n.End > duration) duration = n.End;
        }

        var byKey = new MidiNote[128][];
        for (int k = 0; k < 128; k++)
            byKey[k] = byKeyLists[k].ToArray();

        var playArr = play.ToArray();
        Array.Sort(playArr, static (a, b) => a.Time.CompareTo(b.Time));
        if (playArr.Length > 0)
            duration = Math.Max(duration, playArr[^1].Time);

        return new MidiSong
        {
            FileName = Path.GetFileName(path),
            FilePath = path,
            Notes = noteArr,
            NotesByKey = byKey,
            PlayEvents = playArr,
            MaxDurationByKey = maxDur,
            Duration = Math.Max(duration, 0.01f),
            TrackCount = parsedTracks
        };
    }

    private readonly struct RawEvent
    {
        public readonly int Delta;
        public readonly byte Status;
        public readonly byte Data1;
        public readonly byte Data2;
        public RawEvent(int delta, byte status, byte d1, byte d2)
        {
            Delta = delta; Status = status; Data1 = d1; Data2 = d2;
        }
    }

    private readonly struct TempoPoint
    {
        public readonly long Tick;
        public readonly double SecondsAtTick;
        public readonly double SecondsPerTick;
        public TempoPoint(long tick, double seconds, double spt)
        {
            Tick = tick; SecondsAtTick = seconds; SecondsPerTick = spt;
        }
    }

    private static List<RawEvent> ParseTrack(ReadOnlySpan<byte> track, int trackIndex, List<(long tick, int uspq)> tempos)
    {
        var list = new List<RawEvent>(512);
        int i = 0;
        byte running = 0;
        long tick = 0;
        while (i < track.Length)
        {
            int delta = ReadVar(track, ref i);
            tick += delta;
            if (i >= track.Length) break;
            byte b = track[i];
            byte status;
            if (b >= 0x80)
            {
                status = b;
                i++;
                if (b < 0xF0) running = status;
            }
            else
            {
                if (running == 0) break;
                status = running;
            }

            if (status == 0xFF)
            {
                if (i >= track.Length) break;
                byte type = track[i++];
                int len = ReadVar(track, ref i);
                if (type == 0x51 && len >= 3 && i + 3 <= track.Length)
                {
                    int uspq = (track[i] << 16) | (track[i + 1] << 8) | track[i + 2];
                    tempos.Add((tick, uspq));
                }
                i = Math.Min(track.Length, i + len);
                list.Add(new RawEvent(delta, 0xFF, type, 0));
                continue;
            }

            if (status is 0xF0 or 0xF7)
            {
                int len = ReadVar(track, ref i);
                i = Math.Min(track.Length, i + len);
                continue;
            }

            int need = (status & 0xF0) is 0xC0 or 0xD0 ? 1 : 2;
            if (status >= 0xF0)
            {
                need = status switch { 0xF1 or 0xF3 => 1, 0xF2 => 2, _ => 0 };
            }
            byte d1 = 0, d2 = 0;
            if (need >= 1 && i < track.Length) d1 = track[i++];
            if (need >= 2 && i < track.Length) d2 = track[i++];
            list.Add(new RawEvent(delta, status, d1, d2));
        }
        _ = trackIndex;
        return list;
    }

    private static List<TempoPoint> BuildTempoMap(List<(long tick, int uspq)> tempos, int division)
    {
        var map = new List<TempoPoint>(tempos.Count + 1);
        double seconds = 0;
        long last = 0;
        double spt = (500_000.0 / division) / 1_000_000.0;
        map.Add(new TempoPoint(0, 0, spt));
        foreach (var (tick, uspq) in tempos)
        {
            if (tick < last) continue;
            seconds += (tick - last) * spt;
            spt = (Math.Max(uspq, 1) / (double)division) / 1_000_000.0;
            last = tick;
            map.Add(new TempoPoint(tick, seconds, spt));
        }
        return map;
    }

    private static float ToSeconds(long tick, List<TempoPoint> map)
    {
        int lo = 0, hi = map.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (map[mid].Tick <= tick) lo = mid;
            else hi = mid - 1;
        }
        var p = map[lo];
        return (float)(p.SecondsAtTick + (tick - p.Tick) * p.SecondsPerTick);
    }

    private static bool Eq(ReadOnlySpan<byte> d, string s)
        => d.Length >= 4 && d[0] == s[0] && d[1] == s[1] && d[2] == s[2] && d[3] == s[3];

    private static int ReadU32(ReadOnlySpan<byte> d, ref int o)
    {
        int v = BinaryPrimitives.ReadInt32BigEndian(d.Slice(o, 4));
        o += 4;
        return v;
    }

    private static int ReadU16(ReadOnlySpan<byte> d, ref int o)
    {
        int v = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(o, 2));
        o += 2;
        return v;
    }

    private static int ReadVar(ReadOnlySpan<byte> d, ref int o)
    {
        int v = 0;
        for (int n = 0; n < 4 && o < d.Length; n++)
        {
            byte b = d[o++];
            v = (v << 7) | (b & 0x7F);
            if ((b & 0x80) == 0) break;
        }
        return v;
    }
}
