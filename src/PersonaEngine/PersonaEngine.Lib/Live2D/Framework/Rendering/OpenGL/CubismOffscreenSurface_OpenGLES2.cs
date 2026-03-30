namespace PersonaEngine.Lib.Live2D.Framework.Rendering.OpenGL;

/// <summary>
///     オフスクリーン描画用構造体
/// </summary>
public class CubismOffscreenSurface_OpenGLES2(OpenGLApi gl)
{
    /// <summary>
    ///     引数によって設定されたカラーバッファか？
    /// </summary>
    private bool _isColorBufferInherited;

    /// <summary>
    ///     旧フレームバッファ
    /// </summary>
    private int _oldFBO;

    /// <summary>
    ///     レンダリングターゲットとしてのアドレス
    /// </summary>
    public int RenderTexture { get; private set; }

    /// <summary>
    ///     描画の際使用するテクスチャとしてのアドレス
    /// </summary>
    public int ColorBuffer { get; private set; }

    /// <summary>
    ///     Create時に指定された幅
    /// </summary>
    public int BufferWidth { get; private set; }

    /// <summary>
    ///     Create時に指定された高さ
    /// </summary>
    public int BufferHeight { get; private set; }

    /// <summary>
    ///     指定の描画ターゲットに向けて描画開始
    /// </summary>
    /// <param name="restoreFBO">0以上の場合、EndDrawでこの値をglBindFramebufferする</param>
    public void BeginDraw(int restoreFBO = -1)
    {
        if (RenderTexture == 0)
        {
            return;
        }

        // バックバッファのサーフェイスを記憶しておく
        if (restoreFBO < 0)
        {
            gl.GetIntegerv(gl.GL_FRAMEBUFFER_BINDING, out _oldFBO);
        }
        else
        {
            _oldFBO = restoreFBO;
        }

        //マスク用RenderTextureをactiveにセット
        gl.BindFramebuffer(gl.GL_FRAMEBUFFER, RenderTexture);
    }

    /// <summary>
    ///     描画終了
    /// </summary>
    public void EndDraw()
    {
        if (RenderTexture == 0)
        {
            return;
        }

        // 描画対象を戻す
        gl.BindFramebuffer(gl.GL_FRAMEBUFFER, _oldFBO);
    }

    /// <summary>
    ///     レンダリングターゲットのクリア
    ///     呼ぶ場合はBeginDrawの後で呼ぶこと
    /// </summary>
    /// <param name="r">赤(0.0~1.0)</param>
    /// <param name="g">緑(0.0~1.0)</param>
    /// <param name="b">青(0.0~1.0)</param>
    /// <param name="a">α(0.0~1.0)</param>
    public void Clear(float r, float g, float b, float a)
    {
        // マスクをクリアする
        gl.ClearColor(r, g, b, a);
        gl.Clear(gl.GL_COLOR_BUFFER_BIT);
    }

    /// <summary>
    ///     CubismOffscreenFrame作成
    /// </summary>
    /// <param name="displayBufferWidth">作成するバッファ幅</param>
    /// <param name="displayBufferHeight">作成するバッファ高さ</param>
    /// <param name="colorBuffer">0以外の場合、ピクセル格納領域としてcolorBufferを使用する</param>
    public bool CreateOffscreenSurface(
        int displayBufferWidth,
        int displayBufferHeight,
        int colorBuffer = 0
    )
    {
        // 一旦削除
        DestroyOffscreenSurface();

        // 新しく生成する
        if (colorBuffer == 0)
        {
            ColorBuffer = gl.GenTexture();

            gl.BindTexture(gl.GL_TEXTURE_2D, ColorBuffer);
            gl.TexImage2D(
                gl.GL_TEXTURE_2D,
                0,
                gl.GL_RGBA,
                displayBufferWidth,
                displayBufferHeight,
                0,
                gl.GL_RGBA,
                gl.GL_UNSIGNED_BYTE,
                0
            );
            gl.TexParameteri(gl.GL_TEXTURE_2D, gl.GL_TEXTURE_WRAP_S, gl.GL_CLAMP_TO_EDGE);
            gl.TexParameteri(gl.GL_TEXTURE_2D, gl.GL_TEXTURE_WRAP_T, gl.GL_CLAMP_TO_EDGE);
            gl.TexParameteri(gl.GL_TEXTURE_2D, gl.GL_TEXTURE_MIN_FILTER, gl.GL_LINEAR);
            gl.TexParameteri(gl.GL_TEXTURE_2D, gl.GL_TEXTURE_MAG_FILTER, gl.GL_LINEAR);
            gl.BindTexture(gl.GL_TEXTURE_2D, 0);

            _isColorBufferInherited = false;
        }
        else
        {
            // 指定されたものを使用
            ColorBuffer = colorBuffer;

            _isColorBufferInherited = true;
        }

        gl.GetIntegerv(gl.GL_FRAMEBUFFER_BINDING, out var tmpFramebufferObject);

        var ret = gl.GenFramebuffer();
        gl.BindFramebuffer(gl.GL_FRAMEBUFFER, ret);
        gl.FramebufferTexture2D(
            gl.GL_FRAMEBUFFER,
            gl.GL_COLOR_ATTACHMENT0,
            gl.GL_TEXTURE_2D,
            ColorBuffer,
            0
        );
        gl.BindFramebuffer(gl.GL_FRAMEBUFFER, tmpFramebufferObject);

        RenderTexture = ret;

        BufferWidth = displayBufferWidth;
        BufferHeight = displayBufferHeight;

        // 成功
        return true;
    }

    /// <summary>
    ///     CubismOffscreenFrameの削除
    /// </summary>
    public void DestroyOffscreenSurface()
    {
        if (!_isColorBufferInherited && ColorBuffer != 0)
        {
            gl.DeleteTexture(ColorBuffer);
            ColorBuffer = 0;
        }

        if (RenderTexture != 0)
        {
            gl.DeleteFramebuffer(RenderTexture);
            RenderTexture = 0;
        }
    }

    /// <summary>
    ///     現在有効かどうか
    /// </summary>
    public bool IsValid()
    {
        return RenderTexture != 0;
    }
}
