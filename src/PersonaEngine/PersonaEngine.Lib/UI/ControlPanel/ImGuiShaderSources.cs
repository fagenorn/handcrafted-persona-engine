using PersonaEngine.Lib.UI.Common;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
/// GLSL shader sources used by <see cref="ImGuiController"/> for ImGui rendering.
/// The fragment stage composes <see cref="BayerDither"/> to suppress banding in
/// low-alpha gradients (e.g. the ambient background glows).
/// </summary>
internal static class ImGuiShaderSources
{
    public const string Vertex =
        @"#version 330
layout (location = 0) in vec2 Position;
layout (location = 1) in vec2 UV;
layout (location = 2) in vec4 Color;
uniform mat4 ProjMtx;
out vec2 Frag_UV;
out vec4 Frag_Color;
void main()
{
    Frag_UV = UV;
    Frag_Color = Color;
    gl_Position = ProjMtx * vec4(Position.xy, 0, 1);
}";

    public static readonly string Fragment =
        @"#version 330
in vec2 Frag_UV;
in vec4 Frag_Color;
uniform sampler2D Texture;
layout (location = 0) out vec4 Out_Color;
"
        + BayerDither.FunctionGlsl
        + @"
void main()
{
    vec4 sampled = Frag_Color * texture(Texture, Frag_UV.st);
    Out_Color = applyBayerDither(sampled);
}";
}
