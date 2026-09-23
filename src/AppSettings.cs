namespace BlackMidiVisualizer;

public sealed class AppSettings
{
    public float PlaybackSpeed { get; set; } = 1.0f;
    public float ViewSize { get; set; } = 0.25f;
    public int KeyboardMode { get; set; } = 88;
    public int MaxVisibleNotes { get; set; } = 400_000;
    public int MidiDeviceIndex { get; set; } = 0;
    public bool ShowHud { get; set; } = true;
    public int MidiFloodCapPerMs { get; set; } = 192;
    public bool VSync { get; set; } = false;

    public float ViewSeconds => Math.Clamp(1.25f / Math.Max(ViewSize, 0.05f), 0.45f, 28f);
}
