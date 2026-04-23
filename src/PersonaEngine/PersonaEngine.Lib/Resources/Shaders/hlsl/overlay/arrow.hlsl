// Smooth SDF-based double-headed arrow. Evaluated in a rotated
// local coordinate frame so the arrow is axis-aligned in shader
// space - no pixel-grid artifacts like the rect-based approach
// suffered on 45 degree diagonals. SDF primitives (line segment +
// isosceles triangle) courtesy of Inigo Quilez,
// https://iquilezles.org/articles/distfunctions2d/ .
cbuffer ArrowCB : register(b0) {
    float2 Viewport;        // swap-chain size, used only by VS
    float2 Center;          // button center in pixel coords
    float  Angle;           // arrow axis angle in radians (0 = +x)
    float  HalfLength;      // half the tip-to-tip distance
    float  StemHalfLength;  // half the stem length (tip-less portion)
    float  StemHalfThick;   // half the stem thickness (perpendicular)
    float  HeadHalfBase;    // half the arrowhead base width
    float3 _pad0;
    float4 Color;
    float  Alpha;
    float3 _pad1;
};

struct VSIn { float2 pos : POSITION; };
struct VSOut { float4 pos : SV_Position; };

VSOut VSMain(VSIn i) {
    VSOut o;
    o.pos = float4(i.pos, 0.0, 1.0);
    return o;
}

// Distance from point p to line segment from a to b.
float sdSegment(float2 p, float2 a, float2 b) {
    float2 pa = p - a;
    float2 ba = b - a;
    float h = saturate(dot(pa, ba) / dot(ba, ba));
    return length(pa - ba * h);
}

// Signed distance to an isosceles triangle with apex at origin,
// growing in +y direction. q.x = half base width, q.y = height.
float sdTriangleIsosceles(float2 p, float2 q) {
    p.x = abs(p.x);
    float2 a = p - q * saturate(dot(p, q) / dot(q, q));
    float2 b = p - q * float2(saturate(p.x / q.x), 1.0);
    float s = -sign(q.y);
    float2 d = min(
        float2(dot(a, a), s * (p.x * q.y - p.y * q.x)),
        float2(dot(b, b), s * (p.y - q.y))
    );
    return -sqrt(d.x) * sign(d.y);
}

float4 PSMain(VSOut i) : SV_Target {
    // Transform the pixel into arrow-local space: translate to button
    // center, then rotate by -Angle so the arrow axis becomes +x.
    float2 p = i.pos.xy - Center;
    float c = cos(-Angle);
    float s = sin(-Angle);
    float2 local = float2(c * p.x - s * p.y, s * p.x + c * p.y);

    float headLen = HalfLength - StemHalfLength;

    // Stem: a capsule along the x-axis from -StemHalfLength to +StemHalfLength.
    float stem = sdSegment(
        local,
        float2(-StemHalfLength, 0.0),
        float2( StemHalfLength, 0.0)
    ) - StemHalfThick;

    // Right arrowhead. sdTriangleIsosceles wants its apex at origin
    // growing +y; we want apex at (+HalfLength, 0) growing -x in
    // world-local, so map world (x, y) -> local ((halfLen - x), y)
    // via a 90 degree rotation plus translation.
    float2 rotR = float2(local.y, HalfLength - local.x);
    float headR = sdTriangleIsosceles(rotR, float2(HeadHalfBase, headLen));

    // Left arrowhead: mirror - apex at (-HalfLength, 0).
    float2 rotL = float2(local.y, local.x + HalfLength);
    float headL = sdTriangleIsosceles(rotL, float2(HeadHalfBase, headLen));

    // Union of stem + both heads (SDFs combine via min for union).
    float d = min(min(stem, headR), headL);

    // 1-pixel antialiased band around the zero-level set.
    float mask = 1.0 - saturate(d + 0.5);
    float a = Color.a * mask * Alpha;
    return float4(Color.rgb * a, a);
}
