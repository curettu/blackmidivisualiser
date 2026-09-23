using System.Runtime.InteropServices;

namespace BlackMidiVisualizer.Midi;

[StructLayout(LayoutKind.Sequential)]
public struct MidiNote
{
    public float Start;
    public float End;
    public byte Key;
    public byte Velocity;
    public byte Track;
    public byte Channel;
}

[StructLayout(LayoutKind.Sequential)]
public struct MidiPlayEvent
{
    public float Time;
    public uint Message;
}

public sealed class MidiSong
{
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public required MidiNote[] Notes { get; init; }
    public required MidiNote[][] NotesByKey { get; init; }
    public required MidiPlayEvent[] PlayEvents { get; init; }
    public required float[] MaxDurationByKey { get; init; }
    public float Duration { get; init; }
    public int TrackCount { get; init; }
    public int UniqueNoteCount => Notes.Length;
}
