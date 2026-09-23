using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BlackMidiVisualizer.Render;

public sealed class ChromeSurface : IDisposable
{
    public const int TexWidth = 2048;
    public const int TexHeight = ChromeLayout.TotalHeight;

    private readonly Bitmap _bmp = new(TexWidth, TexHeight, PixelFormat.Format32bppArgb);
    private readonly byte[] _pixels = new byte[TexWidth * TexHeight * 4];

    public byte[] Pixels => _pixels;
    public int Stride => TexWidth * 4;
    public ChromeLayout Layout { get; private set; } = ChromeLayout.Build(1280, 1, 0.25f);

    public void Draw(int viewWidth, AppSettings settings, bool playing, double time, double duration, string? hover)
    {
        int w = Math.Clamp(viewWidth, 640, TexWidth);
        Layout = ChromeLayout.Build(w, settings.PlaybackSpeed, settings.ViewSize);

        using var g = Graphics.FromImage(_bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        using var bar = new SolidBrush(Color.FromArgb(255, 28, 30, 36));
        g.FillRectangle(bar, 0, 0, w, ChromeLayout.BarHeight);
        using var redLine = new SolidBrush(Color.FromArgb(220, 48, 54));
        g.FillRectangle(redLine, 0, ChromeLayout.BarHeight - 1, w, 1);

        var L = Layout;
        DrawIcon(g, L.Open, hover == "open", IconKind.Folder);
        DrawIcon(g, L.Pause, hover == "pause", IconKind.Pause);
        DrawIcon(g, L.Play, hover == "play", IconKind.Play);
        DrawIcon(g, L.Settings, hover == "settings", IconKind.Gear);

        using var labelFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var boxFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var white = new SolidBrush(Color.FromArgb(235, 235, 238));
        using var muted = new SolidBrush(Color.FromArgb(200, 200, 206));

        g.DrawString("Speed:", labelFont, muted, L.SpeedTrack.X - 50, 8);
        DrawSlider(g, L.SpeedTrack, L.SpeedThumb, hover is "speed" or "speedThumb");
        DrawValueBox(g, L.SpeedBox, L.SpeedUp, L.SpeedDown, settings.PlaybackSpeed.ToString("0.##"), boxFont, hover);
        DrawHelp(g, L.SpeedHelp, hover == "speedHelp");

        g.DrawString("Size:", labelFont, muted, L.SizeTrack.X - 38, 8);
        DrawSlider(g, L.SizeTrack, L.SizeThumb, hover is "size" or "sizeThumb");
        DrawValueBox(g, L.SizeBox, L.SizeUp, L.SizeDown, settings.ViewSize.ToString("0.##"), boxFont, hover);
        DrawHelp(g, L.SizeHelp, hover == "sizeHelp");

        // Seek
        using var seekBg = new SolidBrush(Color.FromArgb(255, 12, 12, 14));
        g.FillRectangle(seekBg, 0, ChromeLayout.BarHeight, w, ChromeLayout.SeekHeight);
        float t = duration > 0.001 ? (float)Math.Clamp(time / duration, 0, 1) : 0;
        using var seekFg = new SolidBrush(Color.FromArgb(255, 210, 48, 56));
        g.FillRectangle(seekFg, 0, ChromeLayout.BarHeight, t * w, ChromeLayout.SeekHeight);

        CopyPixels();
    }

    private enum IconKind { Folder, Pause, Play, Gear }

    private static void DrawIcon(Graphics g, RectangleF r, bool hot, IconKind kind)
    {
        using var bg = new SolidBrush(hot ? Color.FromArgb(255, 48, 50, 58) : Color.FromArgb(0, 0, 0, 0));
        g.FillRectangle(bg, r);
        using var pen = new Pen(Color.FromArgb(240, 240, 242), 1.6f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fill = new SolidBrush(Color.FromArgb(240, 240, 242));
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        switch (kind)
        {
            case IconKind.Folder:
                using (var folder = new GraphicsPath())
                {
                    folder.AddLines(new[]
                    {
                        new PointF(cx - 8, cy + 7), new PointF(cx - 8, cy - 3),
                        new PointF(cx - 3, cy - 3), new PointF(cx - 1, cy - 6),
                        new PointF(cx + 8, cy - 6), new PointF(cx + 8, cy + 7)
                    });
                    folder.CloseFigure();
                    g.FillPath(fill, folder);
                }
                break;
            case IconKind.Pause:
                g.FillRectangle(fill, cx - 6, cy - 7, 4, 14);
                g.FillRectangle(fill, cx + 2, cy - 7, 4, 14);
                break;
            case IconKind.Play:
                g.FillPolygon(fill, new[]
                {
                    new PointF(cx - 5, cy - 8), new PointF(cx - 5, cy + 8), new PointF(cx + 8, cy)
                });
                break;
            case IconKind.Gear:
                using (var gp = new GraphicsPath())
                {
                    gp.AddEllipse(cx - 4.2f, cy - 4.2f, 8.4f, 8.4f);
                    g.DrawPath(pen, gp);
                    for (int i = 0; i < 6; i++)
                    {
                        double a = i * Math.PI / 3.0;
                        float x0 = cx + (float)Math.Cos(a) * 5.4f;
                        float y0 = cy + (float)Math.Sin(a) * 5.4f;
                        g.FillEllipse(fill, x0 - 1.7f, y0 - 1.7f, 3.4f, 3.4f);
                    }
                }
                break;
        }
    }

    private static void DrawSlider(Graphics g, RectangleF track, RectangleF thumb, bool hot)
    {
        using var trough = new SolidBrush(Color.FromArgb(255, 14, 14, 18));
        using var fill = new SolidBrush(Color.FromArgb(255, 200, 52, 60));
        g.FillRectangle(trough, track);
        float filled = thumb.X + thumb.Width / 2f - track.X;
        g.FillRectangle(fill, track.X, track.Y, Math.Max(0, filled), track.Height);
        using var th = new SolidBrush(hot ? Color.White : Color.FromArgb(230, 230, 234));
        g.FillRectangle(th, thumb);
        using var edge = new Pen(Color.FromArgb(40, 40, 44));
        g.DrawRectangle(edge, thumb.X, thumb.Y, thumb.Width, thumb.Height);
    }

    private static void DrawValueBox(Graphics g, RectangleF box, RectangleF up, RectangleF down, string text, Font font, string? hover)
    {
        using var bg = new SolidBrush(Color.FromArgb(255, 245, 245, 247));
        using var ink = new SolidBrush(Color.FromArgb(30, 30, 34));
        g.FillRectangle(bg, box.X, box.Y, box.Width + up.Width, box.Height);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, ink, box, sf);
        using var btn = new SolidBrush(Color.FromArgb(230, 232, 236));
        g.FillRectangle(hover == "speedUp" || hover == "sizeUp" ? Brushes.White : btn, up);
        g.FillRectangle(hover == "speedDown" || hover == "sizeDown" ? Brushes.White : btn, down);
        using var p = new Pen(Color.FromArgb(60, 60, 64), 1.3f);
        g.DrawLine(p, up.X + 4, up.Y + 7, up.X + up.Width / 2, up.Y + 3);
        g.DrawLine(p, up.Right - 4, up.Y + 7, up.X + up.Width / 2, up.Y + 3);
        g.DrawLine(p, down.X + 4, down.Y + 3, down.X + down.Width / 2, down.Y + 8);
        g.DrawLine(p, down.Right - 4, down.Y + 3, down.X + down.Width / 2, down.Y + 8);
        using var border = new Pen(Color.FromArgb(80, 80, 86));
        g.DrawRectangle(border, box.X, box.Y, box.Width + up.Width, box.Height);
    }

    private static void DrawHelp(Graphics g, RectangleF r, bool hot)
    {
        using var bg = new SolidBrush(hot ? Color.FromArgb(70, 72, 80) : Color.FromArgb(50, 52, 60));
        g.FillEllipse(bg, r);
        using var f = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        using var w = new SolidBrush(Color.White);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("?", f, w, r, sf);
    }

    private void CopyPixels()
    {
        var data = _bmp.LockBits(new Rectangle(0, 0, TexWidth, TexHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(data.Scan0, _pixels, 0, _pixels.Length); }
        finally { _bmp.UnlockBits(data); }
    }

    public void Dispose() => _bmp.Dispose();
}
