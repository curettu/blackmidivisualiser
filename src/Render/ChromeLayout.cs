using System.Drawing;

namespace BlackMidiVisualizer.Render;

public readonly record struct HitRect(string Id, RectangleF Rect);

public sealed class ChromeLayout
{
    public const int BarHeight = 34;
    public const int SeekHeight = 5;
    public const int TotalHeight = BarHeight + SeekHeight;

    public RectangleF Open, Pause, Play, Settings;
    public RectangleF SpeedTrack, SpeedThumb, SpeedBox, SpeedUp, SpeedDown, SpeedHelp;
    public RectangleF SizeTrack, SizeThumb, SizeBox, SizeUp, SizeDown, SizeHelp;
    public RectangleF Seek;

    public static ChromeLayout Build(int width, float speed, float size)
    {
        var l = new ChromeLayout();
        float y = 4;
        float h = 26;
        float x = 8;

        l.Open = new RectangleF(x, y, 26, h); x += 30;
        l.Pause = new RectangleF(x, y, 26, h); x += 28;
        l.Play = new RectangleF(x, y, 26, h); x += 30;
        l.Settings = new RectangleF(x, y, 26, h); x += 40;

        // Speed
        x += 52; // label drawn in GDI before this
        float speedLabel = 8 + 30 + 28 + 30 + 26 + 16;
        x = speedLabel + 48;
        l.SpeedTrack = new RectangleF(x, y + 8, 130, 10);
        float st = SpeedToT(speed);
        l.SpeedThumb = new RectangleF(l.SpeedTrack.X + st * (l.SpeedTrack.Width - 12), y + 5, 12, 16);
        x = l.SpeedTrack.Right + 8;
        l.SpeedBox = new RectangleF(x, y + 2, 44, 22);
        l.SpeedUp = new RectangleF(l.SpeedBox.Right, y + 2, 16, 11);
        l.SpeedDown = new RectangleF(l.SpeedBox.Right, y + 13, 16, 11);
        l.SpeedHelp = new RectangleF(l.SpeedUp.Right + 4, y + 4, 18, 18);

        x = l.SpeedHelp.Right + 18;
        float sizeTrackX = x + 40;
        l.SizeTrack = new RectangleF(sizeTrackX, y + 8, 130, 10);
        float zt = SizeToT(size);
        l.SizeThumb = new RectangleF(l.SizeTrack.X + zt * (l.SizeTrack.Width - 12), y + 5, 12, 16);
        x = l.SizeTrack.Right + 8;
        l.SizeBox = new RectangleF(x, y + 2, 44, 22);
        l.SizeUp = new RectangleF(l.SizeBox.Right, y + 2, 16, 11);
        l.SizeDown = new RectangleF(l.SizeBox.Right, y + 13, 16, 11);
        l.SizeHelp = new RectangleF(l.SizeUp.Right + 4, y + 4, 18, 18);

        l.Seek = new RectangleF(0, BarHeight, width, SeekHeight);
        return l;
    }

    public IEnumerable<HitRect> Hits()
    {
        yield return new("open", Open);
        yield return new("pause", Pause);
        yield return new("play", Play);
        yield return new("settings", Settings);
        yield return new("speed", SpeedTrack);
        yield return new("speedThumb", SpeedThumb);
        yield return new("speedUp", SpeedUp);
        yield return new("speedDown", SpeedDown);
        yield return new("speedHelp", SpeedHelp);
        yield return new("size", SizeTrack);
        yield return new("sizeThumb", SizeThumb);
        yield return new("sizeUp", SizeUp);
        yield return new("sizeDown", SizeDown);
        yield return new("sizeHelp", SizeHelp);
        yield return new("seek", Seek);
    }

    public static float SpeedToT(float speed) => Math.Clamp((speed - 0.25f) / (4f - 0.25f), 0, 1);
    public static float SizeToT(float size) => Math.Clamp((size - 0.08f) / (2f - 0.08f), 0, 1);
    public static float TToSpeed(float t) => 0.25f + Math.Clamp(t, 0, 1) * (4f - 0.25f);
    public static float TToSize(float t) => 0.08f + Math.Clamp(t, 0, 1) * (2f - 0.08f);

    public float HitToSpeed(float mouseX)
    {
        float t = (mouseX - SpeedTrack.X) / Math.Max(SpeedTrack.Width, 1);
        return TToSpeed(t);
    }

    public float HitToSize(float mouseX)
    {
        float t = (mouseX - SizeTrack.X) / Math.Max(SizeTrack.Width, 1);
        return TToSize(t);
    }
}
