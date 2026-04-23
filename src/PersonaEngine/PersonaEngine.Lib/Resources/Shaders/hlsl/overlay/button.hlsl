cbuffer ButtonCB : register(b0) {
    float2 Viewport;
    float2 HalfSize;
    float  Radius;
    float  Alpha;
    float2 _pad0;
    float4 Fill;
    float4 Border;
    float4 Halo;
    float  HaloRadius;
    float3 _pad1;
};

struct VSIn {
    float2 pos   : POSITION;
    float2 local : TEXCOORD0;
};

struct VSOut {
    float4 pos   : SV_Position;
    float2 local : TEXCOORD0;
};

VSOut VSMain(VSIn i) {
    VSOut o;
    float2 ndc = (i.pos / Viewport) * 2.0 - 1.0;
    o.pos = float4(ndc.x, -ndc.y, 0.0, 1.0);
    o.local = i.local;
    return o;
}

float4 PSMain(VSOut i) : SV_Target {
    float2 d = abs(i.local) - HalfSize + float2(Radius, Radius);
    float sd = length(max(d, 0.0)) - Radius + min(max(d.x, d.y), 0.0);

    // Interior fill + accent border.
    float fillMask = 1.0 - smoothstep(-1.0, 0.0, sd);
    float borderIn = smoothstep(-2.5, -1.5, sd);
    float borderOut = 1.0 - smoothstep(-1.0, 0.0, sd);
    float borderMask = borderIn * borderOut;
    float4 c = lerp(Fill, Border, borderMask);

    // Outer dark halo - a ring of shadow just outside the button so the
    // silhouette stays readable against any background. Peaks near the
    // edge and falls off linearly to the outer halo radius.
    // Full strength at sd=0 falling to zero at sd=HaloRadius; masked
    // to only apply outside the shape to avoid darkening the fill.
    float haloFalloff = 1.0 - saturate(sd / HaloRadius);
    float haloOutside = smoothstep(-0.5, 0.5, sd);
    float haloMask = haloFalloff * haloOutside;

    // Porter-Duff "over": fill layer in front of halo layer.
    // Both colours are in straight alpha; output is premultiplied.
    float fillA = c.a * fillMask;
    float haloA = Halo.a * haloMask;
    float outA = fillA + haloA * (1.0 - fillA);
    float3 outRGB = c.rgb * fillA + Halo.rgb * haloA * (1.0 - fillA);

    outA *= Alpha;
    outRGB *= Alpha;
    return float4(outRGB, outA);
}
