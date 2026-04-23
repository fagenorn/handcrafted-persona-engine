const float bayer4x4[16] = float[](
     0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
    12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
     3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
    15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
);

vec4 applyBayerDither(vec4 color) {
    ivec2 p = ivec2(mod(gl_FragCoord.xy, 4.0));
    float threshold = bayer4x4[p.y * 4 + p.x] - 0.5;
    return vec4(color.rgb + threshold * 2.0 / 255.0, color.a);
}
