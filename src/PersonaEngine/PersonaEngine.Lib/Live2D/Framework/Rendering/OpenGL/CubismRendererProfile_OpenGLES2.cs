namespace PersonaEngine.Lib.Live2D.Framework.Rendering.OpenGL;

internal class CubismRendererProfile_OpenGLES2(OpenGLApi gl)
{
    /// <summary>
    ///     モデル描画直前のアクティブなテクスチャ
    /// </summary>
    internal int LastActiveTexture;

    /// <summary>
    ///     モデル描画直前の頂点バッファ
    /// </summary>
    internal int LastArrayBufferBinding;

    /// <summary>
    ///     モデル描画直前のGL_SCISSOR_TESTパラメータ
    /// </summary>
    internal bool LastBlend;

    /// <summary>
    ///     モデル描画直前のカラーブレンディングパラメータ
    /// </summary>
    internal int[] LastBlending = new int[4];

    /// <summary>
    ///     モデル描画直前のGL_COLOR_WRITEMASKパラメータ
    /// </summary>
    internal bool[] LastColorMask = new bool[4];

    /// <summary>
    ///     モデル描画直前のGL_CULL_FACEパラメータ
    /// </summary>
    internal bool LastCullFace;

    /// <summary>
    ///     モデル描画直前のGL_DEPTH_TESTパラメータ
    /// </summary>
    internal bool LastDepthTest;

    /// <summary>
    ///     モデル描画直前のElementバッファ
    /// </summary>
    internal int LastElementArrayBufferBinding;

    /// <summary>
    ///     モデル描画直前のフレームバッファ
    /// </summary>
    internal int LastFBO;

    /// <summary>
    ///     モデル描画直前のGL_CULL_FACEパラメータ
    /// </summary>
    internal int LastFrontFace;

    /// <summary>
    ///     モデル描画直前のシェーダプログラムバッファ
    /// </summary>
    internal int LastProgram;

    /// <summary>
    ///     モデル描画直前のGL_VERTEX_ATTRIB_ARRAY_ENABLEDパラメータ
    /// </summary>
    internal bool LastScissorTest;

    /// <summary>
    ///     モデル描画直前のGL_STENCIL_TESTパラメータ
    /// </summary>
    internal bool LastStencilTest;

    /// <summary>
    ///     モデル描画直前のテクスチャユニット0
    /// </summary>
    internal int LastTexture0Binding2D;

    /// <summary>
    ///     モデル描画直前のテクスチャユニット1
    /// </summary>
    internal int LastTexture1Binding2D;

    /// <summary>
    ///     モデル描画直前のテクスチャユニット1
    /// </summary>
    internal int[] LastVertexAttribArrayEnabled = new int[4];

    /// <summary>
    ///     モデル描画直前のビューポート
    /// </summary>
    internal int[] LastViewport = new int[4];

    /// <summary>
    ///     OpenGLES2のステートを保持する
    /// </summary>
    internal void Save()
    {
        //-- push state --
        gl.GetIntegerv(gl.GL_ARRAY_BUFFER_BINDING, out LastArrayBufferBinding);
        gl.GetIntegerv(gl.GL_ELEMENT_ARRAY_BUFFER_BINDING, out LastElementArrayBufferBinding);
        gl.GetIntegerv(gl.GL_CURRENT_PROGRAM, out LastProgram);

        gl.GetIntegerv(gl.GL_ACTIVE_TEXTURE, out LastActiveTexture);
        gl.ActiveTexture(gl.GL_TEXTURE1); //テクスチャユニット1をアクティブに（以後の設定対象とする）
        gl.GetIntegerv(gl.GL_TEXTURE_BINDING_2D, out LastTexture1Binding2D);

        gl.ActiveTexture(gl.GL_TEXTURE0); //テクスチャユニット0をアクティブに（以後の設定対象とする）
        gl.GetIntegerv(gl.GL_TEXTURE_BINDING_2D, out LastTexture0Binding2D);

        gl.GetVertexAttribiv(
            0,
            gl.GL_VERTEX_ATTRIB_ARRAY_ENABLED,
            out LastVertexAttribArrayEnabled[0]
        );
        gl.GetVertexAttribiv(
            1,
            gl.GL_VERTEX_ATTRIB_ARRAY_ENABLED,
            out LastVertexAttribArrayEnabled[1]
        );
        gl.GetVertexAttribiv(
            2,
            gl.GL_VERTEX_ATTRIB_ARRAY_ENABLED,
            out LastVertexAttribArrayEnabled[2]
        );
        gl.GetVertexAttribiv(
            3,
            gl.GL_VERTEX_ATTRIB_ARRAY_ENABLED,
            out LastVertexAttribArrayEnabled[3]
        );

        LastScissorTest = gl.IsEnabled(gl.GL_SCISSOR_TEST);
        LastStencilTest = gl.IsEnabled(gl.GL_STENCIL_TEST);
        LastDepthTest = gl.IsEnabled(gl.GL_DEPTH_TEST);
        LastCullFace = gl.IsEnabled(gl.GL_CULL_FACE);
        LastBlend = gl.IsEnabled(gl.GL_BLEND);

        gl.GetIntegerv(gl.GL_FRONT_FACE, out LastFrontFace);

        gl.GetBooleanv(gl.GL_COLOR_WRITEMASK, LastColorMask);

        // backup blending
        gl.GetIntegerv(gl.GL_BLEND_SRC_RGB, out LastBlending[0]);
        gl.GetIntegerv(gl.GL_BLEND_DST_RGB, out LastBlending[1]);
        gl.GetIntegerv(gl.GL_BLEND_SRC_ALPHA, out LastBlending[2]);
        gl.GetIntegerv(gl.GL_BLEND_DST_ALPHA, out LastBlending[3]);

        // モデル描画直前のFBOとビューポートを保存
        gl.GetIntegerv(gl.GL_FRAMEBUFFER_BINDING, out LastFBO);
        gl.GetIntegerv(gl.GL_VIEWPORT, LastViewport);
    }

    /// <summary>
    ///     保持したOpenGLES2のステートを復帰させる
    /// </summary>
    internal void Restore()
    {
        gl.UseProgram(LastProgram);

        SetGlEnableVertexAttribArray(0, LastVertexAttribArrayEnabled[0] > 0);
        SetGlEnableVertexAttribArray(1, LastVertexAttribArrayEnabled[1] > 0);
        SetGlEnableVertexAttribArray(2, LastVertexAttribArrayEnabled[2] > 0);
        SetGlEnableVertexAttribArray(3, LastVertexAttribArrayEnabled[3] > 0);

        SetGlEnable(gl.GL_SCISSOR_TEST, LastScissorTest);
        SetGlEnable(gl.GL_STENCIL_TEST, LastStencilTest);
        SetGlEnable(gl.GL_DEPTH_TEST, LastDepthTest);
        SetGlEnable(gl.GL_CULL_FACE, LastCullFace);
        SetGlEnable(gl.GL_BLEND, LastBlend);

        gl.FrontFace(LastFrontFace);

        gl.ColorMask(LastColorMask[0], LastColorMask[1], LastColorMask[2], LastColorMask[3]);

        gl.BindBuffer(gl.GL_ARRAY_BUFFER, LastArrayBufferBinding); //前にバッファがバインドされていたら破棄する必要がある
        gl.BindBuffer(gl.GL_ELEMENT_ARRAY_BUFFER, LastElementArrayBufferBinding);

        gl.ActiveTexture(gl.GL_TEXTURE1); //テクスチャユニット1を復元
        gl.BindTexture(gl.GL_TEXTURE_2D, LastTexture1Binding2D);

        gl.ActiveTexture(gl.GL_TEXTURE0); //テクスチャユニット0を復元
        gl.BindTexture(gl.GL_TEXTURE_2D, LastTexture0Binding2D);

        gl.ActiveTexture(LastActiveTexture);

        // restore blending
        gl.BlendFuncSeparate(LastBlending[0], LastBlending[1], LastBlending[2], LastBlending[3]);
    }

    /// <summary>
    ///     OpenGLES2の機能の有効・無効をセットする
    /// </summary>
    /// <param name="index">有効・無効にする機能</param>
    /// <param name="enabled">trueなら有効にする</param>
    internal void SetGlEnable(int index, bool enabled)
    {
        if (enabled)
        {
            gl.Enable(index);
        }
        else
        {
            gl.Disable(index);
        }
    }

    /// <summary>
    ///     OpenGLES2のVertex Attribute Array機能の有効・無効をセットする
    /// </summary>
    /// <param name="index">有効・無効にする機能</param>
    /// <param name="enabled">trueなら有効にする</param>
    internal void SetGlEnableVertexAttribArray(int index, bool enabled)
    {
        if (enabled)
        {
            gl.EnableVertexAttribArray(index);
        }
        else
        {
            gl.DisableVertexAttribArray(index);
        }
    }
}
