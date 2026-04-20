namespace PersonaEngine.Lib.UI.Common;

/// <summary>
/// Provides a 4×4 Bayer ordered dithering GLSL snippet. Eliminates visible
/// banding in low-alpha gradients on 8-bit framebuffers by adding a
/// deterministic sub-pixel noise pattern that averages to zero but breaks up
/// quantization steps.
/// </summary>
/// <remarks>
/// Amplitude is ±1 / 255 per channel — imperceptible on high-contrast content
/// but enough to break banding even where multiple low-alpha gradients stack.
/// Include <see cref="FunctionGlsl"/> near the top of a GLSL fragment shader,
/// then call <c>applyBayerDither(color)</c> just before writing to the output.
/// </remarks>
public static class BayerDither
{
    /// <summary>
    /// GLSL declaration providing a <c>vec4 applyBayerDither(vec4 color)</c>
    /// function. Uses <c>gl_FragCoord.xy</c> to index the Bayer matrix, so
    /// callers don't need to supply screen position explicitly.
    /// </summary>
    public const string FunctionGlsl =
        @"
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
";
}
