namespace PersonaEngine.Lib.Live2D.Framework.Rendering.OpenGL;

internal record CubismShaderSet
{
    /// <summary>
    ///     シェーダプログラムに渡す変数のアドレス(Position)
    /// </summary>
    internal int AttributePositionLocation;

    /// <summary>
    ///     シェーダプログラムに渡す変数のアドレス(TexCoord)
    /// </summary>
    internal int AttributeTexCoordLocation;

    /// <summary>
    ///     シェーダプログラムに渡す変数のアドレス(Texture0)
    /// </summary>
    internal int SamplerTexture0Location;

    /// <summary>
    ///     シェーダプログラムに渡す変数のアドレス(Texture1)
    /// </summary>
    internal int SamplerTexture1Location;

    /// <summary>
    ///     シェーダプログラムのアドレス
    /// </summary>
    internal int ShaderProgram;

    /// <summary>
    ///     シェーダプログラムに渡す変数のアドレス(BaseColor)
    /// </summary>
    internal int UniformBaseColorLocation;

    /// <summary>
    ///     シェーダプログラムに渡す変数のアドレス(ClipMatrix)
    /// </summary>
    internal int UniformClipMatrixLocation;

    /// <summary>
    ///     シェーダプログラムに渡す変数のアドレス(Matrix)
    /// </summary>
    internal int UniformMatrixLocation;

    /// <summary>
    ///     シェーダプログラムに渡す変数のアドレス(MultiplyColor)
    /// </summary>
    internal int UniformMultiplyColorLocation;

    /// <summary>
    ///     シェーダプログラムに渡す変数のアドレス(ScreenColor)
    /// </summary>
    internal int UniformScreenColorLocation;

    /// <summary>
    ///     シェーダプログラムに渡す変数のアドレス(ChannelFlag)
    /// </summary>
    internal int UnifromChannelFlagLocation;
}
