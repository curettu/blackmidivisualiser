using System.Runtime.InteropServices;

namespace BlackMidiVisualizer.Midi;

public sealed class MidiOutput : IDisposable
{
    private IntPtr _handle;
    private bool _open;

    public static string[] ListDevices()
    {
        uint n = midiOutGetNumDevs();
        var names = new string[n];
        for (uint i = 0; i < n; i++)
        {
            var caps = new MidiOutCaps();
            midiOutGetDevCaps(i, ref caps, (uint)Marshal.SizeOf<MidiOutCaps>());
            names[i] = string.IsNullOrWhiteSpace(caps.szPname) ? $"Device {i}" : caps.szPname.TrimEnd('\0');
        }
        return names;
    }

    public void Open(int deviceIndex)
    {
        Close();
        uint n = midiOutGetNumDevs();
        if (n == 0)
            throw new InvalidOperationException("No MIDI output devices found. Install a synth (OmniMIDI / Windows GS).");

        uint id = (uint)Math.Clamp(deviceIndex, 0, (int)n - 1);
        int mm = midiOutOpen(out _handle, id, IntPtr.Zero, IntPtr.Zero, 0);
        if (mm != 0)
            throw new InvalidOperationException($"midiOutOpen failed ({mm}).");
        _open = true;
    }

    public void ShortMsg(uint message)
    {
        if (_open)
            midiOutShortMsg(_handle, message);
    }

    public void Reset()
    {
        if (_open)
            midiOutReset(_handle);
    }

    public void Close()
    {
        if (!_open) return;
        midiOutReset(_handle);
        midiOutClose(_handle);
        _open = false;
        _handle = IntPtr.Zero;
    }

    public void Dispose() => Close();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MidiOutCaps
    {
        public ushort wMid, wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public ushort wTechnology, wVoices, wNotes, wChannelMask;
        public uint dwSupport;
    }

    [DllImport("winmm.dll")] private static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int midiOutGetDevCaps(uint uDeviceID, ref MidiOutCaps caps, uint cb);
    [DllImport("winmm.dll")] private static extern int midiOutOpen(out IntPtr ph, uint id, IntPtr cb, IntPtr inst, uint flags);
    [DllImport("winmm.dll")] private static extern int midiOutShortMsg(IntPtr h, uint msg);
    [DllImport("winmm.dll")] private static extern int midiOutReset(IntPtr h);
    [DllImport("winmm.dll")] private static extern int midiOutClose(IntPtr h);
}
