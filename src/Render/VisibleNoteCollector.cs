using BlackMidiVisualizer.Midi;

namespace BlackMidiVisualizer.Render;

public sealed class VisibleNoteCollector
{
    private readonly int[] _cursors = new int[128];
    private double _lastTime = -1;

    public int Collect(MidiSong song, float time, float span, int firstKey, int lastKey, int maxNotes, GpuNote[] dest, int[] pressedTrack)
    {
        if (time + 0.25f < _lastTime)
            Array.Clear(_cursors);
        _lastTime = time;

        float t0 = time;
        float t1 = time + span;
        int written = 0;
        Array.Fill(pressedTrack, -1);

        for (int k = firstKey; k <= lastKey && written < maxNotes; k++)
        {
            MidiNote[] arr = song.NotesByKey[k];
            if (arr.Length == 0) continue;

            float lookback = t0 - song.MaxDurationByKey[k] - 0.01f;
            int i = _cursors[k];
            if (i > arr.Length) i = 0;
            if (i < arr.Length && arr[i].Start < lookback)
            {
                i = LowerBoundStart(arr, lookback);
            }

            while (i < arr.Length && arr[i].End <= t0) i++;
            _cursors[k] = i;

            int j = i;
            while (j < arr.Length && arr[j].Start < t1 && written < maxNotes)
            {
                ref readonly var n = ref arr[j];
                if (n.End > t0)
                {
                    dest[written++] = new GpuNote
                    {
                        Start = n.Start,
                        End = n.End,
                        Key = n.Key,
                        Color = ColorPack.FromTrack(n.Track, n.Velocity)
                    };
                    if (n.Start <= t0 && n.End > t0)
                        pressedTrack[k] = n.Track;
                }
                j++;
            }
        }
        return written;
    }

    public void Reset()
    {
        Array.Clear(_cursors);
        _lastTime = -1;
    }

    private static int LowerBoundStart(MidiNote[] arr, float start)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (arr[mid].Start < start) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
