cbuffer IconCB : register(b0) {
    float2 Viewport;
    float2 _pad0;
    float4 Color;
    float  Alpha;
    float3 _pad1;
};

struct VSIn { float2 pos : POSITION; };
struct VSOut { float4 pos : SV_Position; };

VSOut VSMain(VSIn i) {
    VSOut o;
    float2 ndc = (i.pos / Viewport) * 2.0 - 1.0;
    o.pos = float4(ndc.x, -ndc.y, 0.0, 1.0);
    return o;
}

float4 PSMain(VSOut i) : SV_Target {
    float a = Color.a * Alpha;
    return float4(Color.rgb * a, a);
}
