using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BlackMidiVisualizer.Render;

public sealed class HudSurface : IDisposable
{
    public const int Width = 268;
    public const int Height = 148;

    private readonly Bitmap _bmp = new(Width, Height, PixelFormat.Format32bppArgb);
    private readonly byte[] _pixels = new byte[Width * Height * 4];

    public byte[] Pixels => _pixels;
    public int Stride => Width * 4;

    public void Draw(
        string fileName,
        double time,
        double duration,
        int noteCount,
        int visible,
        int nps,
        int fps,
        int poly,
        int passed,
        bool playing)
    {
        using var g = Graphics.FromImage(_bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        using var bg = new SolidBrush(Color.FromArgb(186, 22, 22, 26));
        g.FillRectangle(bg, 0, 0, Width, Height);

        DrawMark(g, 10, 10);

        using var title = new Font("Segoe UI", 16f, FontStyle.Italic | FontStyle.Bold);
        using var ver = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var row = new Font("Consolas", 8.5f, FontStyle.Regular);
        using var white = new SolidBrush(Color.FromArgb(236, 236, 240));
        using var accent = new SolidBrush(Color.FromArgb(230, 64, 72));
        using var muted = new SolidBrush(Color.FromArgb(168, 168, 176));

        g.DrawString("BMV", title, white, 40, 8);
        g.DrawString("v1.0  Own", ver, accent, 108, 16);

        string name = string.IsNullOrEmpty(fileName) ? "No MIDI loaded" : fileName;
        if (name.Length > 28) name = name[..25] + "...";

        float y = 42;
        DrawRow(g, row, muted, white, "File:", name, y); y += 16;
        DrawRow(g, row, muted, white, "Time:", $"{Fmt(time)} / {Fmt(duration)}", y); y += 16;
        DrawRow(g, row, muted, white, "FPS:", fps.ToString("0.0"), y); y += 16;
        DrawRow(g, row, muted, white, "Rendered Notes:", visible.ToString("N0"), y); y += 16;
        DrawRow(g, row, muted, white, "NPS:", nps.ToString("N0"), y); y += 16;
        DrawRow(g, row, muted, white, "NC:", noteCount.ToString("N0"), y); y += 16;
        DrawRow(g, row, muted, white, "Passed:", passed.ToString("N0"), y);

        _ = playing;
        _ = poly;
        CopyToBgra();
    }

    private static void DrawRow(Graphics g, Font font, Brush lab, Brush val, string k, string v, float y)
    {
        g.DrawString(k, font, lab, 10, y);
        g.DrawString(v, font, val, 128, y);
    }

    private static void DrawMark(Graphics g, int x, int y)
    {
        using var ring = new Pen(Color.FromArgb(230, 64, 72), 2.2f);
        g.DrawEllipse(ring, x, y + 2, 22, 22);
        using var ivory = new SolidBrush(Color.FromArgb(230, 230, 232));
        using var ebony = new SolidBrush(Color.FromArgb(20, 20, 22));
        g.FillRectangle(ivory, x + 5, y + 8, 3.1f, 12);
        g.FillRectangle(ivory, x + 9, y + 8, 3.1f, 12);
        g.FillRectangle(ivory, x + 13, y + 8, 3.1f, 12);
        g.FillRectangle(ebony, x + 7.2f, y + 8, 2.2f, 7);
        g.FillRectangle(ebony, x + 11.2f, y + 8, 2.2f, 7);
    }

    private void CopyToBgra()
    {
        var data = _bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(data.Scan0, _pixels, 0, _pixels.Length); }
        finally { _bmp.UnlockBits(data); }
    }

    private static string Fmt(double s)
    {
        if (s < 0) s = 0;
        int m = (int)(s / 60);
        int sec = (int)s % 60;
        int ms = (int)((s - Math.Floor(s)) * 10);
        return $"{m:00}:{sec:00}.{ms}";
    }

    public void Dispose() => _bmp.Dispose();
}
