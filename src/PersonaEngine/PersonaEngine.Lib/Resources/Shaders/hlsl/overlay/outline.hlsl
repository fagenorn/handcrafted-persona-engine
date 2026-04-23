cbuffer OutlineCB : register(b0) {
    float2 Viewport;
    float  Radius;
    float  Thickness;
    float4 Color;
    float  Alpha;
    float3 _pad0;
    float4 Halo;
    float  HaloRadius;
    float3 _pad1;
};

struct VSIn { float2 pos : POSITION; };
struct VSOut { float4 pos : SV_Position; };

VSOut VSMain(VSIn i) {
    VSOut o;
    o.pos = float4(i.pos, 0.0, 1.0);
    return o;
}

float4 PSMain(VSOut i) : SV_Target {
    // SV_Position is in pixel coordinates for the pixel shader.
    float2 local = i.pos.xy - Viewport * 0.5;
    float2 halfSize = Viewport * 0.5;
    float2 d = abs(local) - halfSize + float2(Radius, Radius);
    float sd = length(max(d, 0.0)) - Radius + min(max(d.x, d.y), 0.0);

    // Accent outline band - sd in [-Thickness, 0], fading at both ends
    // so the stroke antialiases without jaggies on rounded corners.
    float inner = smoothstep(-Thickness - 1.0, -Thickness, sd);
    float outer = 1.0 - smoothstep(-1.0, 0.0, sd);
    float strokeMask = inner * outer;

    // Dark halo just INSIDE the outline - the window edge is at
    // sd = 0 and everything outside is transparent, so the halo has
    // to live on the inner side to remain visible. It peaks right at
    // the outline and fades toward the window interior, guaranteeing
    // contrast against any avatar colour.
    float haloFalloff = 1.0 - saturate((-sd) / HaloRadius);
    float haloInside = 1.0 - smoothstep(-0.5, 0.5, sd);
    float haloMask = haloFalloff * haloInside * (1.0 - strokeMask);

    // Accent stroke in front, dark halo behind (both straight-alpha).
    float strokeA = Color.a * strokeMask;
    float haloA = Halo.a * haloMask;
    float outA = strokeA + haloA * (1.0 - strokeA);
    float3 outRGB = Color.rgb * strokeA + Halo.rgb * haloA * (1.0 - strokeA);

    outA *= Alpha;
    outRGB *= Alpha;
    return float4(outRGB, outA);
}
