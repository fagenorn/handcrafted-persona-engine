using System.Numerics;
using System.Runtime.InteropServices;

namespace PersonaEngine.Lib.UI.Rendering.RouletteWheel;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct VertexPositionTexture
{
    public readonly Vector3 Position;

    public readonly Vector2 TextureCoordinate;

    public VertexPositionTexture(Vector3 position, Vector2 textureCoordinate)
    {
        Position = position;
        TextureCoordinate = textureCoordinate;
    }
}
