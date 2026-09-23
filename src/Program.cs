using System.Runtime.InteropServices;

namespace BlackMidiVisualizer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form = new MainForm();
        form.Show();
        var msg = new NativeMessage();
        while (form.Visible)
        {
            while (PeekMessage(ref msg, 0, 0, 0, 1))
            {
                if (msg.msg == 0x0012) // WM_QUIT
                    return;
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            if (form.Visible)
                form.Frame();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint hwnd;
        public uint msg;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x, y;
    }

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(ref NativeMessage lpMsg, nint hWnd, uint wFilterMin, uint wFilterMax, uint wRemove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref NativeMessage lpMsg);
}
