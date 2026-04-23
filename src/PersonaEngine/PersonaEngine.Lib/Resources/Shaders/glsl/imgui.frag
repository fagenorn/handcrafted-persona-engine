#version 330
in vec2 Frag_UV;
in vec4 Frag_Color;
uniform sampler2D Texture;
layout (location = 0) out vec4 Out_Color;
#include "glsl/include/bayer_dither.glsl"
void main()
{
    vec4 sampled = Frag_Color * texture(Texture, Frag_UV.st);
    Out_Color = applyBayerDither(sampled);
}
