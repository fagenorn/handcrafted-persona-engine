Texture2D<float4> SourceTexture : register(t0);
SamplerState LinearSampler : register(s0);

struct VSIn {
    float2 pos : POSITION;
    float2 uv  : TEXCOORD0;
};

struct VSOut {
    float4 pos : SV_Position;
    float2 uv  : TEXCOORD0;
};

VSOut VSMain(VSIn i) {
    VSOut o;
    o.pos = float4(i.pos, 0.0, 1.0);
    o.uv = i.uv;
    return o;
}

float4 PSMain(VSOut i) : SV_Target {
    float4 c = SourceTexture.Sample(LinearSampler, i.uv);
    // Source is straight-alpha; premultiply on output to match the
    // composition swap chain's premultiplied format expectation.
    return float4(c.rgb * c.a, c.a);
}
