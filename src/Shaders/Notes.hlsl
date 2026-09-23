cbuffer Frame : register(b0)
{
    float Time;
    float TimeSpan;
    float PianoTopNdc;
    float ViewTopNdc;
    uint  NoteCount;
    float ScreenW;
    float ScreenH;
    float NoteGapPx;
};

struct GpuNote
{
    float start;
    float end;
    float key;
    float color;
};

StructuredBuffer<GpuNote> Notes : register(t0);

cbuffer KeyLayout : register(b1)
{
    float4 KeyGeom[128];
};

struct PSIn
{
    float4 pos    : SV_Position;
    float4 col    : COLOR0;
    float2 uv     : TEXCOORD0;
    float2 pxSize : TEXCOORD1;
};

float4 UnpackColor(float packed)
{
    uint c = asuint(packed);
    return float4(
        (c & 255) / 255.0,
        ((c >> 8) & 255) / 255.0,
        ((c >> 16) & 255) / 255.0,
        ((c >> 24) & 255) / 255.0);
}

static const float2 Corners[6] =
{
    float2(0, 0),
    float2(1, 0),
    float2(0, 1),
    float2(0, 1),
    float2(1, 0),
    float2(1, 1)
};

float sdRoundBox(float2 p, float2 b, float r)
{
    float2 q = abs(p) - b + r;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
}

PSIn VSMain(uint vid : SV_VertexID)
{
    PSIn o;
    uint nid = vid / 6;
    uint cid = vid % 6;

    if (nid >= NoteCount)
    {
        o.pos = float4(0, 0, 0, 0);
        o.col = 0;
        o.uv = 0;
        o.pxSize = 0;
        return o;
    }

    GpuNote n = Notes[nid];
    int key = clamp((int)n.key, 0, 127);
    float4 g = KeyGeom[key];

    float gap = NoteGapPx / max(ScreenW, 1.0);
    float x0 = g.x + gap;
    float x1 = g.x + g.y - gap;
    if (x1 <= x0)
    {
        x0 = g.x;
        x1 = g.x + g.y;
    }

    float y0 = (n.start - Time) / max(TimeSpan, 0.0001);
    float y1 = (n.end   - Time) / max(TimeSpan, 0.0001);

    float ndcY0 = lerp(PianoTopNdc, ViewTopNdc, y0);
    float ndcY1 = lerp(PianoTopNdc, ViewTopNdc, y1);

    float minH = 3.0 / max(ScreenH, 1.0) * 2.0;
    if (ndcY1 - ndcY0 < minH)
        ndcY1 = ndcY0 + minH;

    float2 c = Corners[cid];
    float ndcX = lerp(x0 * 2.0 - 1.0, x1 * 2.0 - 1.0, c.x);
    float ndcY = lerp(ndcY0, ndcY1, c.y);

    o.pos = float4(ndcX, ndcY, 0.5, 1);
    o.col = UnpackColor(n.color);
    o.uv = c;
    o.pxSize = float2(max((x1 - x0) * ScreenW, 2.0), max((ndcY1 - ndcY0) * 0.5 * ScreenH, 2.0));
    return o;
}

float4 PSMain(PSIn i) : SV_Target
{
    float2 uv = i.uv;
    float2 px = max(i.pxSize, float2(2, 2));
    float2 center = (uv - 0.5) * px;
    float2 halfSize = px * 0.5 - 0.5;
    float rad = min(6.5, min(halfSize.x, halfSize.y) * 0.42);
    rad = max(rad, 1.2);

    float d = sdRoundBox(center, halfSize, rad);
    float aa = 1.0 - smoothstep(-1.1, 1.1, d);
    if (aa <= 0.01)
        discard;

    float3 baseCol = i.col.rgb;
    float3 face = baseCol * lerp(0.72, 1.18, saturate(uv.y * 0.85 + 0.08));

    float insetX = min(5.5, px.x * 0.22);
    float insetY = min(6.0, px.y * 0.18);
    float2 innerHalf = max(halfSize - float2(insetX, insetY), float2(0.5, 0.5));
    float innerR = max(rad - 2.2, 0.8);
    float di = sdRoundBox(center, innerHalf, innerR);
    float inner = 1.0 - smoothstep(-0.8, 1.2, di);

    float3 rim = baseCol * 0.32;
    float3 col = lerp(rim, face, inner);

    float side = smoothstep(0.0, insetX, uv.x * px.x) * smoothstep(0.0, insetX, (1.0 - uv.x) * px.x);
    col *= 0.78 + 0.22 * side;

    float topBand = saturate((uv.y - 0.78) / 0.22);
    col += topBand * lerp(baseCol, float3(1, 1, 1), 0.45) * 0.28;

    float gloss = pow(saturate(1.0 - abs(uv.y - 0.90) * 9.0), 1.6) * inner;
    col += gloss * 0.38;

    col *= 0.88 + 0.12 * saturate(uv.y);

    float ring = saturate(1.0 - abs(di + 0.4) * 1.8) * 0.12 * inner;
    col += ring;

    return float4(saturate(col), aa);
}
