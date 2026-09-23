struct VSIn
{
    float2 pos   : POSITION;
    float2 uv    : TEXCOORD0;
    float4 color : COLOR0;
};

struct PSIn
{
    float4 pos   : SV_Position;
    float2 uv    : TEXCOORD0;
    float4 color : COLOR0;
};

PSIn VSMain(VSIn i)
{
    PSIn o;
    o.pos = float4(i.pos, 0.0, 1.0);
    o.uv = i.uv;
    o.color = i.color;
    return o;
}

Texture2D Tex : register(t0);
SamplerState Samp : register(s0);

float4 PSMain(PSIn i) : SV_Target
{
    float4 t = Tex.Sample(Samp, i.uv);
    return i.color * t;
}

float4 PSSolid(PSIn i) : SV_Target
{
    return i.color;
}
