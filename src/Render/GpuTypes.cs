using System.Runtime.InteropServices;

namespace BlackMidiVisualizer.Render;

[StructLayout(LayoutKind.Sequential)]
public struct GpuNote
{
    public float Start;
    public float End;
    public float Key;
    public float Color;
}

[StructLayout(LayoutKind.Sequential)]
public struct FrameConstants
{
    public float Time;
    public float TimeSpan;
    public float PianoTopNdc;
    public float ViewTopNdc;
    public uint NoteCount;
    public float ScreenW;
    public float ScreenH;
    public float NoteGapPx;
}

[StructLayout(LayoutKind.Sequential)]
public struct QuadVertex
{
    public float X, Y;
    public float U, V;
    public float R, G, B, A;
}

public static class ColorPack
{
    public static readonly uint[] TrackPalette =
    {
        0xFF46A0FF, 0xFF28B4FF, 0xFF50DC78, 0xFFFFC832,
        0xFFFF5A8C, 0xFFAA64FF, 0xFFFF5AC8, 0xFF3CE6FF,
        0xFFFF8C3C, 0xFF64F0C8, 0xFFE878FF, 0xFF7CF064,
        0xFFFF648C, 0xFF5AA0FF, 0xFFF0D256, 0xFF9B7CFF
    };

    public static float PackBgra(uint bgra) => BitConverter.Int32BitsToSingle(unchecked((int)bgra));

    public static float FromTrack(int track, byte velocity)
    {
        uint c = TrackPalette[track & 15];
        float v = Math.Clamp(velocity / 127f, 0.35f, 1f);
        byte b = (byte)(c & 0xFF);
        byte g = (byte)((c >> 8) & 0xFF);
        byte r = (byte)((c >> 16) & 0xFF);
        r = (byte)Math.Clamp((int)(r * v), 0, 255);
        g = (byte)Math.Clamp((int)(g * v), 0, 255);
        b = (byte)Math.Clamp((int)(b * v), 0, 255);
        uint packed = r | ((uint)g << 8) | ((uint)b << 16) | 0xFF000000u;
        return PackBgra(packed);
    }

    public static void UnpackRgb(int track, out float r, out float g, out float b)
    {
        uint c = TrackPalette[track & 15];
        b = (c & 0xFF) / 255f;
        g = ((c >> 8) & 0xFF) / 255f;
        r = ((c >> 16) & 0xFF) / 255f;
    }
}

public readonly struct KeyGeom
{
    public readonly float X;
    public readonly float Width;
    public readonly bool IsBlack;
    public KeyGeom(float x, float width, bool isBlack)
    {
        X = x; Width = width; IsBlack = isBlack;
    }
}

public static class KeyboardLayout
{
    private static readonly bool[] Black = { false, true, false, true, false, false, true, false, true, false, true, false };

    public static bool IsBlackKey(int key) => Black[((key % 12) + 12) % 12];

    public static (int first, int last) Range(int mode)
        => mode >= 128 ? (0, 127) : (21, 108);

    public static KeyGeom[] Build(int firstKey, int lastKey)
    {
        var geom = new KeyGeom[128];
        int whites = 0;
        for (int k = firstKey; k <= lastKey; k++)
            if (!IsBlackKey(k)) whites++;

        float wWhite = whites > 0 ? 1f / whites : 1f;
        float x = 0;
        var whiteX = new float[128];
        for (int k = firstKey; k <= lastKey; k++)
        {
            if (!IsBlackKey(k))
            {
                whiteX[k] = x;
                geom[k] = new KeyGeom(x, wWhite, false);
                x += wWhite;
            }
        }

        float wBlack = wWhite * 0.58f;
        for (int k = firstKey; k <= lastKey; k++)
        {
            if (!IsBlackKey(k)) continue;
            int prev = k - 1;
            while (prev >= firstKey && IsBlackKey(prev)) prev--;
            float anchor = prev >= firstKey ? whiteX[prev] + wWhite * 0.68f : 0;
            geom[k] = new KeyGeom(anchor - wBlack * 0.5f, wBlack, true);
        }
        return geom;
    }
}
