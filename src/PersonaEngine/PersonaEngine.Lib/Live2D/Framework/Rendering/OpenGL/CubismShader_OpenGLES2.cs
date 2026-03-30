using PersonaEngine.Lib.Live2D.Framework.Model;

namespace PersonaEngine.Lib.Live2D.Framework.Rendering.OpenGL;

internal class CubismShader_OpenGLES2(OpenGLApi gl)
{
    public const string CSM_FRAGMENT_SHADER_FP_PRECISION_HIGH = "highp";

    public const string CSM_FRAGMENT_SHADER_FP_PRECISION_MID = "mediump";

    public const string CSM_FRAGMENT_SHADER_FP_PRECISION_LOW = "lowp";

    public const string CSM_FRAGMENT_SHADER_FP_PRECISION = CSM_FRAGMENT_SHADER_FP_PRECISION_HIGH;

    private const string GLES2 = "#version 100\n";

    private const string GLES2C =
        GLES2 + "precision " + CSM_FRAGMENT_SHADER_FP_PRECISION + " float;";

    private const string Normal = "#version 120\n";

    private const string Tegra =
        "#version 100\n"
        + "#extension GL_NV_shader_framebuffer_fetch : enable\n"
        + "precision "
        + CSM_FRAGMENT_SHADER_FP_PRECISION
        + " float;";

    // SetupMask
    public const string VertShaderSrcSetupMask_ES2 = GLES2 + VertShaderSrcSetupMask_Base;

    public const string VertShaderSrcSetupMask_Normal = Normal + VertShaderSrcSetupMask_Base;

    private const string VertShaderSrcSetupMask_Base =
        @"attribute vec4 a_position;
attribute vec2 a_texCoord;
varying vec2 v_texCoord;
varying vec4 v_myPos;
uniform mat4 u_clipMatrix;
void main()
{
gl_Position = u_clipMatrix * a_position;
v_myPos = u_clipMatrix * a_position;
v_texCoord = a_texCoord;
v_texCoord.y = 1.0 - v_texCoord.y;
}";

    public const string FragShaderSrcSetupMask_ES2 = GLES2C + FragShaderSrcSetupMask_Base;

    public const string FragShaderSrcSetupMask_Normal = Normal + FragShaderSrcSetupMask_Base;

    private const string FragShaderSrcSetupMask_Base =
        @"varying vec2 v_texCoord;
varying vec4 v_myPos;
uniform sampler2D s_texture0;
uniform vec4 u_channelFlag;
uniform vec4 u_baseColor;
void main()
{
float isInside = 
    step(u_baseColor.x, v_myPos.x/v_myPos.w)
* step(u_baseColor.y, v_myPos.y/v_myPos.w)
* step(v_myPos.x/v_myPos.w, u_baseColor.z)
* step(v_myPos.y/v_myPos.w, u_baseColor.w);
gl_FragColor = u_channelFlag * texture2D(s_texture0 , v_texCoord).a * isInside;
}";

    public const string FragShaderSrcSetupMaskTegra = Tegra + FragShaderSrcSetupMask_Base;

    //----- バーテックスシェーダプログラム -----
    // Normal & Add & Mult 共通
    public const string VertShaderSrc_ES2 = GLES2 + VertShaderSrc_Base;

    public const string VertShaderSrc_Normal = Normal + VertShaderSrc_Base;

    private const string VertShaderSrc_Base =
        @"attribute vec4 a_position;
attribute vec2 a_texCoord;
varying vec2 v_texCoord;
uniform mat4 u_matrix;
void main()
{
gl_Position = u_matrix * a_position;
v_texCoord = a_texCoord;
v_texCoord.y = 1.0 - v_texCoord.y;
}";

    // Normal & Add & Mult 共通（クリッピングされたものの描画用）
    public const string VertShaderSrcMasked_ES2 = GLES2 + VertShaderSrcMasked_Base;

    public const string VertShaderSrcMasked_Normal = Normal + VertShaderSrcMasked_Base;

    private const string VertShaderSrcMasked_Base =
        @"attribute vec4 a_position;
attribute vec2 a_texCoord;
varying vec2 v_texCoord;
varying vec4 v_clipPos;
uniform mat4 u_matrix;
uniform mat4 u_clipMatrix;
void main()
{
gl_Position = u_matrix * a_position;
v_clipPos = u_clipMatrix * a_position;
v_texCoord = a_texCoord;
v_texCoord.y = 1.0 - v_texCoord.y;
}";

    //----- フラグメントシェーダプログラム -----
    // Normal & Add & Mult 共通
    public const string FragShaderSrc_ES2 = GLES2C + FragShaderSrc_Base;

    public const string FragShaderSrc_Normal = Normal + FragShaderSrc_Base;

    public const string FragShaderSrc_Base =
        @"varying vec2 v_texCoord;
uniform sampler2D s_texture0;
uniform vec4 u_baseColor;
uniform vec4 u_multiplyColor;
uniform vec4 u_screenColor;
void main()
{
vec4 texColor = texture2D(s_texture0 , v_texCoord);
texColor.rgb = texColor.rgb * u_multiplyColor.rgb;
texColor.rgb = texColor.rgb + u_screenColor.rgb - (texColor.rgb * u_screenColor.rgb);
vec4 color = texColor * u_baseColor;
gl_FragColor = vec4(color.rgb * color.a,  color.a);
}";

    public const string FragShaderSrcTegra = Tegra + FragShaderSrc_Base;

    // Normal & Add & Mult 共通 （PremultipliedAlpha）
    public const string FragShaderSrcPremultipliedAlpha_ES2 =
        GLES2C + FragShaderSrcPremultipliedAlpha_Base;

    public const string FragShaderSrcPremultipliedAlpha_Normal =
        Normal + FragShaderSrcPremultipliedAlpha_Base;

    public const string FragShaderSrcPremultipliedAlpha_Base =
        @"varying vec2 v_texCoord;
uniform sampler2D s_texture0;
uniform vec4 u_baseColor;
uniform vec4 u_multiplyColor;
uniform vec4 u_screenColor;
void main()
{
vec4 texColor = texture2D(s_texture0 , v_texCoord);
texColor.rgb = texColor.rgb * u_multiplyColor.rgb;
texColor.rgb = (texColor.rgb + u_screenColor.rgb * texColor.a) - (texColor.rgb * u_screenColor.rgb);
gl_FragColor = texColor * u_baseColor;
}";

    public const string FragShaderSrcPremultipliedAlphaTegra =
        Tegra + FragShaderSrcPremultipliedAlpha_Base;

    // Normal & Add & Mult 共通（クリッピングされたものの描画用）
    public const string FragShaderSrcMask_ES2 = GLES2C + FragShaderSrcMask_Base;

    public const string FragShaderSrcMask_Normal = Normal + FragShaderSrcMask_Base;

    public const string FragShaderSrcMask_Base =
        @"varying vec2 v_texCoord;
varying vec4 v_clipPos;
uniform sampler2D s_texture0;
uniform sampler2D s_texture1;
uniform vec4 u_channelFlag;
uniform vec4 u_baseColor;
uniform vec4 u_multiplyColor;
uniform vec4 u_screenColor;
void main()
{
vec4 texColor = texture2D(s_texture0 , v_texCoord);
texColor.rgb = texColor.rgb * u_multiplyColor.rgb;
texColor.rgb = texColor.rgb + u_screenColor.rgb - (texColor.rgb * u_screenColor.rgb);
vec4 col_formask = texColor * u_baseColor;
col_formask.rgb = col_formask.rgb  * col_formask.a ;
vec4 clipMask = (1.0 - texture2D(s_texture1, v_clipPos.xy / v_clipPos.w)) * u_channelFlag;
float maskVal = clipMask.r + clipMask.g + clipMask.b + clipMask.a;
col_formask = col_formask * maskVal;
gl_FragColor = col_formask;
}";

    public const string FragShaderSrcMaskTegra = Tegra + FragShaderSrcMask_Base;

    // Normal & Add & Mult 共通（クリッピングされて反転使用の描画用）
    public const string FragShaderSrcMaskInverted_ES2 = GLES2C + FragShaderSrcMaskInverted_Base;

    public const string FragShaderSrcMaskInverted_Normal = Normal + FragShaderSrcMaskInverted_Base;

    public const string FragShaderSrcMaskInverted_Base =
        @"varying vec2 v_texCoord;
varying vec4 v_clipPos;
uniform sampler2D s_texture0;
uniform sampler2D s_texture1;
uniform vec4 u_channelFlag;
uniform vec4 u_baseColor;
uniform vec4 u_multiplyColor;
uniform vec4 u_screenColor;
void main()
{
vec4 texColor = texture2D(s_texture0 , v_texCoord);
texColor.rgb = texColor.rgb * u_multiplyColor.rgb;
texColor.rgb = texColor.rgb + u_screenColor.rgb - (texColor.rgb * u_screenColor.rgb);
vec4 col_formask = texColor * u_baseColor;
col_formask.rgb = col_formask.rgb  * col_formask.a ;
vec4 clipMask = (1.0 - texture2D(s_texture1, v_clipPos.xy / v_clipPos.w)) * u_channelFlag;
float maskVal = clipMask.r + clipMask.g + clipMask.b + clipMask.a;
col_formask = col_formask * (1.0 - maskVal);
gl_FragColor = col_formask;
}";

    public const string FragShaderSrcMaskInvertedTegra = Tegra + FragShaderSrcMaskInverted_Base;

    // Normal & Add & Mult 共通（クリッピングされたものの描画用、PremultipliedAlphaの場合）
    public const string FragShaderSrcMaskPremultipliedAlpha_ES2 =
        GLES2C + FragShaderSrcMaskPremultipliedAlpha_Base;

    public const string FragShaderSrcMaskPremultipliedAlpha_Normal =
        Normal + FragShaderSrcMaskPremultipliedAlpha_Base;

    public const string FragShaderSrcMaskPremultipliedAlpha_Base =
        @"varying vec2 v_texCoord;
        varying vec4 v_clipPos;
        uniform sampler2D s_texture0;
        uniform sampler2D s_texture1;
        uniform vec4 u_channelFlag;
        uniform vec4 u_baseColor;
        uniform vec4 u_multiplyColor;
        uniform vec4 u_screenColor;
        void main()
        {
        vec4 texColor = texture2D(s_texture0 , v_texCoord);
        texColor.rgb = texColor.rgb * u_multiplyColor.rgb;
        texColor.rgb = (texColor.rgb + u_screenColor.rgb * texColor.a) - (texColor.rgb * u_screenColor.rgb);
        vec4 col_formask = texColor * u_baseColor;
        vec4 clipMask = (1.0 - texture2D(s_texture1, v_clipPos.xy / v_clipPos.w)) * u_channelFlag;
        float maskVal = clipMask.r + clipMask.g + clipMask.b + clipMask.a;
        col_formask = col_formask * maskVal;
        gl_FragColor = col_formask;
        }";

    public const string FragShaderSrcMaskPremultipliedAlphaTegra =
        Tegra + FragShaderSrcMaskPremultipliedAlpha_Base;

    // Normal & Add & Mult 共通（クリッピングされて反転使用の描画用、PremultipliedAlphaの場合）
    public const string FragShaderSrcMaskInvertedPremultipliedAlpha_ES2 =
        GLES2C + FragShaderSrcMaskInvertedPremultipliedAlpha_Base;

    public const string FragShaderSrcMaskInvertedPremultipliedAlpha_Normal =
        Normal + FragShaderSrcMaskInvertedPremultipliedAlpha_Base;

    public const string FragShaderSrcMaskInvertedPremultipliedAlpha_Base =
        @"varying vec2 v_texCoord;
varying vec4 v_clipPos;
uniform sampler2D s_texture0;
uniform sampler2D s_texture1;
uniform vec4 u_channelFlag;
uniform vec4 u_baseColor;
uniform vec4 u_multiplyColor;
uniform vec4 u_screenColor;
void main()
{
vec4 texColor = texture2D(s_texture0 , v_texCoord);
texColor.rgb = texColor.rgb * u_multiplyColor.rgb;
texColor.rgb = (texColor.rgb + u_screenColor.rgb * texColor.a) - (texColor.rgb * u_screenColor.rgb);
vec4 col_formask = texColor * u_baseColor;
vec4 clipMask = (1.0 - texture2D(s_texture1, v_clipPos.xy / v_clipPos.w)) * u_channelFlag;
float maskVal = clipMask.r + clipMask.g + clipMask.b + clipMask.a;
col_formask = col_formask * (1.0 - maskVal);
gl_FragColor = col_formask;
}";

    public const string FragShaderSrcMaskInvertedPremultipliedAlphaTegra =
        Tegra + FragShaderSrcMaskInvertedPremultipliedAlpha_Base;

    public const int ShaderCount = 19; // シェーダの数 = マスク生成用 + (通常 + 加算 + 乗算) * (マスク無 + マスク有 + マスク有反転 + マスク無の乗算済アルファ対応版 + マスク有の乗算済アルファ対応版 + マスク有反転の乗算済アルファ対応版)

    /// <summary>
    ///     ロードしたシェーダプログラムを保持する変数
    /// </summary>
    private readonly List<CubismShaderSet> _shaderSets = [];

    /// <summary>
    ///     Tegra対応.拡張方式で描画
    /// </summary>
    internal bool s_extMode;

    /// <summary>
    ///     拡張方式のPA設定用の変数
    /// </summary>
    internal bool s_extPAMode;

    /// <summary>
    ///     シェーダプログラムの一連のセットアップを実行する
    /// </summary>
    /// <param name="renderer">レンダラのインスタンス</param>
    /// <param name="textureId">GPUのテクスチャID</param>
    /// <param name="vertexCount">ポリゴンメッシュの頂点数</param>
    /// <param name="vertexArray">ポリゴンメッシュの頂点配列</param>
    /// <param name="uvArray">uv配列</param>
    /// <param name="opacity">不透明度</param>
    /// <param name="colorBlendMode">カラーブレンディングのタイプ</param>
    /// <param name="baseColor">ベースカラー</param>
    /// <param name="multiplyColor"></param>
    /// <param name="screenColor"></param>
    /// <param name="isPremultipliedAlpha">乗算済みアルファかどうか</param>
    /// <param name="matrix4x4">Model-View-Projection行列</param>
    /// <param name="invertedMask">マスクを反転して使用するフラグ</param>
    internal void SetupShaderProgramForDraw(
        CubismRenderer_OpenGLES2 renderer,
        CubismModel model,
        int index
    )
    {
        if (_shaderSets.Count == 0)
        {
            GenerateShaders();
        }

        // Blending
        int SRC_COLOR;
        int DST_COLOR;
        int SRC_ALPHA;
        int DST_ALPHA;

        // _shaderSets用のオフセット計算
        var masked = renderer.ClippingContextBufferForDraw != null; // この描画オブジェクトはマスク対象か
        var invertedMask = model.GetDrawableInvertedMask(index);
        var isPremultipliedAlpha = renderer.IsPremultipliedAlpha;
        var offset =
            (
                masked
                    ? invertedMask
                        ? 2
                        : 1
                    : 0
            ) + (isPremultipliedAlpha ? 3 : 0);

        CubismShaderSet shaderSet;
        switch (model.GetDrawableBlendMode(index))
        {
            case CubismBlendMode.Normal:
            default:
                shaderSet = _shaderSets[(int)ShaderNames.Normal + offset];
                SRC_COLOR = gl.GL_ONE;
                DST_COLOR = gl.GL_ONE_MINUS_SRC_ALPHA;
                SRC_ALPHA = gl.GL_ONE;
                DST_ALPHA = gl.GL_ONE_MINUS_SRC_ALPHA;

                break;

            case CubismBlendMode.Additive:
                shaderSet = _shaderSets[(int)ShaderNames.Add + offset];
                SRC_COLOR = gl.GL_ONE;
                DST_COLOR = gl.GL_ONE;
                SRC_ALPHA = gl.GL_ZERO;
                DST_ALPHA = gl.GL_ONE;

                break;

            case CubismBlendMode.Multiplicative:
                shaderSet = _shaderSets[(int)ShaderNames.Mult + offset];
                SRC_COLOR = gl.GL_DST_COLOR;
                DST_COLOR = gl.GL_ONE_MINUS_SRC_ALPHA;
                SRC_ALPHA = gl.GL_ZERO;
                DST_ALPHA = gl.GL_ONE;

                break;
        }

        gl.UseProgram(shaderSet.ShaderProgram);

        // 頂点配列の設定
        SetupTexture(renderer, model, index, shaderSet);

        // テクスチャ頂点の設定
        SetVertexAttributes(shaderSet);

        if (masked)
        {
            gl.ActiveTexture(gl.GL_TEXTURE1);

            var draw = renderer.ClippingContextBufferForDraw!;

            // frameBufferに書かれたテクスチャ
            var tex = renderer.GetMaskBuffer(draw.BufferIndex).ColorBuffer;

            gl.BindTexture(gl.GL_TEXTURE_2D, tex);
            gl.Uniform1i(shaderSet.SamplerTexture1Location, 1);

            // View座標をClippingContextの座標に変換するための行列を設定
            gl.UniformMatrix4fv(
                shaderSet.UniformClipMatrixLocation,
                1,
                false,
                draw.MatrixForDraw.Tr
            );

            // 使用するカラーチャンネルを設定
            SetColorChannelUniformVariables(shaderSet, renderer.ClippingContextBufferForDraw!);
        }

        //座標変換
        gl.UniformMatrix4fv(shaderSet.UniformMatrixLocation, 1, false, renderer.GetMvpMatrix().Tr);

        // ユニフォーム変数設定
        var baseColor = renderer.GetModelColorWithOpacity(model.GetDrawableOpacity(index));
        var multiplyColor = model.GetMultiplyColor(index);
        var screenColor = model.GetScreenColor(index);
        SetColorUniformVariables(shaderSet, baseColor, multiplyColor, screenColor);

        gl.BlendFuncSeparate(SRC_COLOR, DST_COLOR, SRC_ALPHA, DST_ALPHA);
    }

    internal void SetupShaderProgramForMask(
        CubismRenderer_OpenGLES2 renderer,
        CubismModel model,
        int index
    )
    {
        if (_shaderSets.Count == 0)
        {
            GenerateShaders();
        }

        // Blending
        var SRC_COLOR = gl.GL_ZERO;
        var DST_COLOR = gl.GL_ONE_MINUS_SRC_COLOR;
        var SRC_ALPHA = gl.GL_ZERO;
        var DST_ALPHA = gl.GL_ONE_MINUS_SRC_ALPHA;

        var shaderSet = _shaderSets[(int)ShaderNames.SetupMask];
        gl.UseProgram(shaderSet.ShaderProgram);

        var draw = renderer.ClippingContextBufferForMask!;

        //テクスチャ設定
        SetupTexture(renderer, model, index, shaderSet);

        // 頂点配列の設定
        SetVertexAttributes(shaderSet);

        // 使用するカラーチャンネルを設定
        SetColorChannelUniformVariables(shaderSet, draw);

        gl.UniformMatrix4fv(shaderSet.UniformClipMatrixLocation, 1, false, draw.MatrixForMask.Tr);

        var rect = draw.LayoutBounds;
        CubismTextureColor baseColor = new(
            rect.X * 2.0f - 1.0f,
            rect.Y * 2.0f - 1.0f,
            rect.GetRight() * 2.0f - 1.0f,
            rect.GetBottom() * 2.0f - 1.0f
        );
        var multiplyColor = model.GetMultiplyColor(index);
        var screenColor = model.GetScreenColor(index);
        SetColorUniformVariables(shaderSet, baseColor, multiplyColor, screenColor);

        gl.BlendFuncSeparate(SRC_COLOR, DST_COLOR, SRC_ALPHA, DST_ALPHA);
    }

    /// <summary>
    ///     シェーダプログラムを解放する
    /// </summary>
    internal void ReleaseShaderProgram()
    {
        for (var i = 0; i < _shaderSets.Count; i++)
        {
            if (_shaderSets[i].ShaderProgram != 0)
            {
                gl.DeleteProgram(_shaderSets[i].ShaderProgram);
                _shaderSets[i].ShaderProgram = 0;
            }
        }
    }

    /// <summary>
    ///     シェーダプログラムを初期化する
    /// </summary>
    internal void GenerateShaders()
    {
        for (var i = 0; i < ShaderCount; i++)
        {
            _shaderSets.Add(new CubismShaderSet());
        }

        if (gl.IsES2)
        {
            if (s_extMode)
            {
                _shaderSets[0].ShaderProgram = LoadShaderProgram(
                    VertShaderSrcSetupMask_ES2,
                    FragShaderSrcSetupMaskTegra
                );

                _shaderSets[1].ShaderProgram = LoadShaderProgram(
                    VertShaderSrc_ES2,
                    FragShaderSrcTegra
                );
                _shaderSets[2].ShaderProgram = LoadShaderProgram(
                    VertShaderSrcMasked_ES2,
                    FragShaderSrcMaskTegra
                );
                _shaderSets[3].ShaderProgram = LoadShaderProgram(
                    VertShaderSrcMasked_ES2,
                    FragShaderSrcMaskInvertedTegra
                );
                _shaderSets[4].ShaderProgram = LoadShaderProgram(
                    VertShaderSrc_ES2,
                    FragShaderSrcPremultipliedAlphaTegra
                );
                _shaderSets[5].ShaderProgram = LoadShaderProgram(
                    VertShaderSrcMasked_ES2,
                    FragShaderSrcMaskPremultipliedAlphaTegra
                );
                _shaderSets[6].ShaderProgram = LoadShaderProgram(
                    VertShaderSrcMasked_ES2,
                    FragShaderSrcMaskInvertedPremultipliedAlphaTegra
                );
            }
            else
            {
                _shaderSets[0].ShaderProgram = LoadShaderProgram(
                    VertShaderSrcSetupMask_ES2,
                    FragShaderSrcSetupMask_ES2
                );

                _shaderSets[1].ShaderProgram = LoadShaderProgram(
                    VertShaderSrc_ES2,
                    FragShaderSrc_ES2
                );
                _shaderSets[2].ShaderProgram = LoadShaderProgram(
                    VertShaderSrcMasked_ES2,
                    FragShaderSrcMask_ES2
                );
                _shaderSets[3].ShaderProgram = LoadShaderProgram(
                    VertShaderSrcMasked_ES2,
                    FragShaderSrcMaskInverted_ES2
                );
                _shaderSets[4].ShaderProgram = LoadShaderProgram(
                    VertShaderSrc_ES2,
                    FragShaderSrcPremultipliedAlpha_ES2
                );
                _shaderSets[5].ShaderProgram = LoadShaderProgram(
                    VertShaderSrcMasked_ES2,
                    FragShaderSrcMaskPremultipliedAlpha_ES2
                );
                _shaderSets[6].ShaderProgram = LoadShaderProgram(
                    VertShaderSrcMasked_ES2,
                    FragShaderSrcMaskInvertedPremultipliedAlpha_ES2
                );
            }

            // 加算も通常と同じシェーダーを利用する
            _shaderSets[7].ShaderProgram = _shaderSets[1].ShaderProgram;
            _shaderSets[8].ShaderProgram = _shaderSets[2].ShaderProgram;
            _shaderSets[9].ShaderProgram = _shaderSets[3].ShaderProgram;
            _shaderSets[10].ShaderProgram = _shaderSets[4].ShaderProgram;
            _shaderSets[11].ShaderProgram = _shaderSets[5].ShaderProgram;
            _shaderSets[12].ShaderProgram = _shaderSets[6].ShaderProgram;

            // 乗算も通常と同じシェーダーを利用する
            _shaderSets[13].ShaderProgram = _shaderSets[1].ShaderProgram;
            _shaderSets[14].ShaderProgram = _shaderSets[2].ShaderProgram;
            _shaderSets[15].ShaderProgram = _shaderSets[3].ShaderProgram;
            _shaderSets[16].ShaderProgram = _shaderSets[4].ShaderProgram;
            _shaderSets[17].ShaderProgram = _shaderSets[5].ShaderProgram;
            _shaderSets[18].ShaderProgram = _shaderSets[6].ShaderProgram;
        }
        else
        {
            _shaderSets[0].ShaderProgram = LoadShaderProgram(
                VertShaderSrcSetupMask_Normal,
                FragShaderSrcSetupMask_Normal
            );

            _shaderSets[1].ShaderProgram = LoadShaderProgram(
                VertShaderSrc_Normal,
                FragShaderSrc_Normal
            );
            _shaderSets[2].ShaderProgram = LoadShaderProgram(
                VertShaderSrcMasked_Normal,
                FragShaderSrcMask_Normal
            );
            _shaderSets[3].ShaderProgram = LoadShaderProgram(
                VertShaderSrcMasked_Normal,
                FragShaderSrcMaskInverted_Normal
            );
            _shaderSets[4].ShaderProgram = LoadShaderProgram(
                VertShaderSrc_Normal,
                FragShaderSrcPremultipliedAlpha_Normal
            );
            _shaderSets[5].ShaderProgram = LoadShaderProgram(
                VertShaderSrcMasked_Normal,
                FragShaderSrcMaskPremultipliedAlpha_Normal
            );
            _shaderSets[6].ShaderProgram = LoadShaderProgram(
                VertShaderSrcMasked_Normal,
                FragShaderSrcMaskInvertedPremultipliedAlpha_Normal
            );

            // 加算も通常と同じシェーダーを利用する
            _shaderSets[7].ShaderProgram = _shaderSets[1].ShaderProgram;
            _shaderSets[8].ShaderProgram = _shaderSets[2].ShaderProgram;
            _shaderSets[9].ShaderProgram = _shaderSets[3].ShaderProgram;
            _shaderSets[10].ShaderProgram = _shaderSets[4].ShaderProgram;
            _shaderSets[11].ShaderProgram = _shaderSets[5].ShaderProgram;
            _shaderSets[12].ShaderProgram = _shaderSets[6].ShaderProgram;

            // 乗算も通常と同じシェーダーを利用する
            _shaderSets[13].ShaderProgram = _shaderSets[1].ShaderProgram;
            _shaderSets[14].ShaderProgram = _shaderSets[2].ShaderProgram;
            _shaderSets[15].ShaderProgram = _shaderSets[3].ShaderProgram;
            _shaderSets[16].ShaderProgram = _shaderSets[4].ShaderProgram;
            _shaderSets[17].ShaderProgram = _shaderSets[5].ShaderProgram;
            _shaderSets[18].ShaderProgram = _shaderSets[6].ShaderProgram;
        }

        // SetupMask
        _shaderSets[0].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[0].ShaderProgram,
            "a_position"
        );
        _shaderSets[0].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[0].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[0].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[0].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[0].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[0].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[0].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[0].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[0].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[0].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[0].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[0].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[0].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[0].ShaderProgram,
            "u_screenColor"
        );

        // 通常
        _shaderSets[1].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[1].ShaderProgram,
            "a_position"
        );
        _shaderSets[1].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[1].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[1].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[1].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[1].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[1].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[1].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[1].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[1].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[1].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[1].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[1].ShaderProgram,
            "u_screenColor"
        );

        // 通常（クリッピング）
        _shaderSets[2].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[2].ShaderProgram,
            "a_position"
        );
        _shaderSets[2].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[2].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[2].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[2].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[2].SamplerTexture1Location = gl.GetUniformLocation(
            _shaderSets[2].ShaderProgram,
            "s_texture1"
        );
        _shaderSets[2].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[2].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[2].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[2].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[2].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[2].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[2].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[2].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[2].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[2].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[2].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[2].ShaderProgram,
            "u_screenColor"
        );

        // 通常（クリッピング・反転）
        _shaderSets[3].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[3].ShaderProgram,
            "a_position"
        );
        _shaderSets[3].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[3].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[3].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[3].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[3].SamplerTexture1Location = gl.GetUniformLocation(
            _shaderSets[3].ShaderProgram,
            "s_texture1"
        );
        _shaderSets[3].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[3].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[3].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[3].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[3].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[3].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[3].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[3].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[3].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[3].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[3].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[3].ShaderProgram,
            "u_screenColor"
        );

        // 通常（PremultipliedAlpha）
        _shaderSets[4].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[4].ShaderProgram,
            "a_position"
        );
        _shaderSets[4].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[4].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[4].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[4].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[4].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[4].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[4].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[4].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[4].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[4].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[4].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[4].ShaderProgram,
            "u_screenColor"
        );

        // 通常（クリッピング、PremultipliedAlpha）
        _shaderSets[5].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[5].ShaderProgram,
            "a_position"
        );
        _shaderSets[5].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[5].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[5].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[5].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[5].SamplerTexture1Location = gl.GetUniformLocation(
            _shaderSets[5].ShaderProgram,
            "s_texture1"
        );
        _shaderSets[5].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[5].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[5].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[5].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[5].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[5].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[5].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[5].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[5].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[5].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[5].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[5].ShaderProgram,
            "u_screenColor"
        );

        // 通常（クリッピング・反転、PremultipliedAlpha）
        _shaderSets[6].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[6].ShaderProgram,
            "a_position"
        );
        _shaderSets[6].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[6].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[6].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[6].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[6].SamplerTexture1Location = gl.GetUniformLocation(
            _shaderSets[6].ShaderProgram,
            "s_texture1"
        );
        _shaderSets[6].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[6].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[6].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[6].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[6].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[6].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[6].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[6].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[6].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[6].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[6].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[6].ShaderProgram,
            "u_screenColor"
        );

        // 加算
        _shaderSets[7].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[7].ShaderProgram,
            "a_position"
        );
        _shaderSets[7].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[7].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[7].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[7].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[7].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[7].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[7].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[7].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[7].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[7].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[7].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[7].ShaderProgram,
            "u_screenColor"
        );

        // 加算（クリッピング）
        _shaderSets[8].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[8].ShaderProgram,
            "a_position"
        );
        _shaderSets[8].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[8].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[8].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[8].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[8].SamplerTexture1Location = gl.GetUniformLocation(
            _shaderSets[8].ShaderProgram,
            "s_texture1"
        );
        _shaderSets[8].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[8].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[8].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[8].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[8].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[8].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[8].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[8].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[8].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[8].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[8].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[8].ShaderProgram,
            "u_screenColor"
        );

        // 加算（クリッピング・反転）
        _shaderSets[9].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[9].ShaderProgram,
            "a_position"
        );
        _shaderSets[9].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[9].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[9].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[9].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[9].SamplerTexture1Location = gl.GetUniformLocation(
            _shaderSets[9].ShaderProgram,
            "s_texture1"
        );
        _shaderSets[9].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[9].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[9].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[9].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[9].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[9].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[9].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[9].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[9].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[9].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[9].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[9].ShaderProgram,
            "u_screenColor"
        );

        // 加算（PremultipliedAlpha）
        _shaderSets[10].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[10].ShaderProgram,
            "a_position"
        );
        _shaderSets[10].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[10].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[10].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[10].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[10].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[10].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[10].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[10].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[10].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[10].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[10].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[10].ShaderProgram,
            "u_screenColor"
        );

        // 加算（クリッピング、PremultipliedAlpha）
        _shaderSets[11].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[11].ShaderProgram,
            "a_position"
        );
        _shaderSets[11].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[11].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[11].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[11].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[11].SamplerTexture1Location = gl.GetUniformLocation(
            _shaderSets[11].ShaderProgram,
            "s_texture1"
        );
        _shaderSets[11].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[11].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[11].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[11].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[11].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[11].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[11].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[11].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[11].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[11].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[11].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[11].ShaderProgram,
            "u_screenColor"
        );

        // 加算（クリッピング・反転、PremultipliedAlpha）
        _shaderSets[12].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[12].ShaderProgram,
            "a_position"
        );
        _shaderSets[12].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[12].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[12].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[12].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[12].SamplerTexture1Location = gl.GetUniformLocation(
            _shaderSets[12].ShaderProgram,
            "s_texture1"
        );
        _shaderSets[12].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[12].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[12].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[12].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[12].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[12].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[12].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[12].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[12].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[12].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[12].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[12].ShaderProgram,
            "u_screenColor"
        );

        // 乗算
        _shaderSets[13].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[13].ShaderProgram,
            "a_position"
        );
        _shaderSets[13].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[13].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[13].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[13].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[13].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[13].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[13].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[13].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[13].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[13].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[13].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[13].ShaderProgram,
            "u_screenColor"
        );

        // 乗算（クリッピング）
        _shaderSets[14].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[14].ShaderProgram,
            "a_position"
        );
        _shaderSets[14].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[14].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[14].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[14].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[14].SamplerTexture1Location = gl.GetUniformLocation(
            _shaderSets[14].ShaderProgram,
            "s_texture1"
        );
        _shaderSets[14].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[14].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[14].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[14].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[14].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[14].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[14].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[14].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[14].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[14].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[14].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[14].ShaderProgram,
            "u_screenColor"
        );

        // 乗算（クリッピング・反転）
        _shaderSets[15].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[15].ShaderProgram,
            "a_position"
        );
        _shaderSets[15].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[15].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[15].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[15].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[15].SamplerTexture1Location = gl.GetUniformLocation(
            _shaderSets[15].ShaderProgram,
            "s_texture1"
        );
        _shaderSets[15].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[15].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[15].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[15].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[15].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[15].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[15].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[15].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[15].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[15].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[15].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[15].ShaderProgram,
            "u_screenColor"
        );

        // 乗算（PremultipliedAlpha）
        _shaderSets[16].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[16].ShaderProgram,
            "a_position"
        );
        _shaderSets[16].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[16].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[16].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[16].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[16].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[16].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[16].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[16].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[16].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[16].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[16].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[16].ShaderProgram,
            "u_screenColor"
        );

        // 乗算（クリッピング、PremultipliedAlpha）
        _shaderSets[17].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[17].ShaderProgram,
            "a_position"
        );
        _shaderSets[17].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[17].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[17].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[17].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[17].SamplerTexture1Location = gl.GetUniformLocation(
            _shaderSets[17].ShaderProgram,
            "s_texture1"
        );
        _shaderSets[17].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[17].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[17].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[17].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[17].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[17].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[17].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[17].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[17].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[17].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[17].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[17].ShaderProgram,
            "u_screenColor"
        );

        // 乗算（クリッピング・反転、PremultipliedAlpha）
        _shaderSets[18].AttributePositionLocation = gl.GetAttribLocation(
            _shaderSets[18].ShaderProgram,
            "a_position"
        );
        _shaderSets[18].AttributeTexCoordLocation = gl.GetAttribLocation(
            _shaderSets[18].ShaderProgram,
            "a_texCoord"
        );
        _shaderSets[18].SamplerTexture0Location = gl.GetUniformLocation(
            _shaderSets[18].ShaderProgram,
            "s_texture0"
        );
        _shaderSets[18].SamplerTexture1Location = gl.GetUniformLocation(
            _shaderSets[18].ShaderProgram,
            "s_texture1"
        );
        _shaderSets[18].UniformMatrixLocation = gl.GetUniformLocation(
            _shaderSets[18].ShaderProgram,
            "u_matrix"
        );
        _shaderSets[18].UniformClipMatrixLocation = gl.GetUniformLocation(
            _shaderSets[18].ShaderProgram,
            "u_clipMatrix"
        );
        _shaderSets[18].UnifromChannelFlagLocation = gl.GetUniformLocation(
            _shaderSets[18].ShaderProgram,
            "u_channelFlag"
        );
        _shaderSets[18].UniformBaseColorLocation = gl.GetUniformLocation(
            _shaderSets[18].ShaderProgram,
            "u_baseColor"
        );
        _shaderSets[18].UniformMultiplyColorLocation = gl.GetUniformLocation(
            _shaderSets[18].ShaderProgram,
            "u_multiplyColor"
        );
        _shaderSets[18].UniformScreenColorLocation = gl.GetUniformLocation(
            _shaderSets[18].ShaderProgram,
            "u_screenColor"
        );
    }

    /// <summary>
    ///     シェーダプログラムをロードしてアドレス返す。
    /// </summary>
    /// <param name="vertShaderSrc">頂点シェーダのソース</param>
    /// <param name="fragShaderSrc">フラグメントシェーダのソース</param>
    /// <returns>シェーダプログラムのアドレス</returns>
    internal int LoadShaderProgram(string vertShaderSrc, string fragShaderSrc)
    {
        // Create shader program.
        var shaderProgram = gl.CreateProgram();

        if (!CompileShaderSource(out var vertShader, gl.GL_VERTEX_SHADER, vertShaderSrc))
        {
            CubismLog.Error("[Live2D SDK]Vertex shader compile error!");

            return 0;
        }

        // Create and compile fragment shader.
        if (!CompileShaderSource(out var fragShader, gl.GL_FRAGMENT_SHADER, fragShaderSrc))
        {
            CubismLog.Error("[Live2D SDK]Fragment shader compile error!");

            return 0;
        }

        // Attach vertex shader to program.
        gl.AttachShader(shaderProgram, vertShader);

        // Attach fragment shader to program.
        gl.AttachShader(shaderProgram, fragShader);

        // Link program.
        if (!LinkProgram(shaderProgram))
        {
            CubismLog.Error("[Live2D SDK]Failed to link program: %d", shaderProgram);

            if (vertShader != 0)
            {
                gl.DeleteShader(vertShader);
            }

            if (fragShader != 0)
            {
                gl.DeleteShader(fragShader);
            }

            if (shaderProgram != 0)
            {
                gl.DeleteProgram(shaderProgram);
            }

            return 0;
        }

        // Release vertex and fragment shaders.
        if (vertShader != 0)
        {
            gl.DetachShader(shaderProgram, vertShader);
            gl.DeleteShader(vertShader);
        }

        if (fragShader != 0)
        {
            gl.DetachShader(shaderProgram, fragShader);
            gl.DeleteShader(fragShader);
        }

        return shaderProgram;
    }

    /// <summary>
    ///     シェーダプログラムをコンパイルする
    /// </summary>
    /// <param name="outShader">コンパイルされたシェーダプログラムのアドレス</param>
    /// <param name="shaderType">シェーダタイプ(Vertex/Fragment)</param>
    /// <param name="shaderSource">シェーダソースコード</param>
    /// <returns>
    ///     true         .      コンパイル成功
    ///     false        .      コンパイル失敗
    /// </returns>
    internal unsafe bool CompileShaderSource(out int outShader, int shaderType, string shaderSource)
    {
        int status;

        outShader = gl.CreateShader(shaderType);
        gl.ShaderSource(outShader, shaderSource);
        gl.CompileShader(outShader);

        int logLength;
        gl.GetShaderiv(outShader, gl.GL_INFO_LOG_LENGTH, &logLength);
        if (logLength > 0)
        {
            gl.GetShaderInfoLog(outShader, out var log);
            CubismLog.Error($"[Live2D SDK]Shader compile log: {log}");
        }

        gl.GetShaderiv(outShader, gl.GL_COMPILE_STATUS, &status);
        if (status == gl.GL_FALSE)
        {
            gl.DeleteShader(outShader);

            return false;
        }

        return true;
    }

    /// <summary>
    ///     シェーダプログラムをリンクする
    /// </summary>
    /// <param name="shaderProgram">リンクするシェーダプログラムのアドレス</param>
    /// <returns>
    ///     true            .  リンク成功
    ///     false           .  リンク失敗
    /// </returns>
    internal unsafe bool LinkProgram(int shaderProgram)
    {
        int status;
        gl.LinkProgram(shaderProgram);

        int logLength;
        gl.GetProgramiv(shaderProgram, gl.GL_INFO_LOG_LENGTH, &logLength);
        if (logLength > 0)
        {
            gl.GetProgramInfoLog(shaderProgram, out var log);
            CubismLog.Error($"[Live2D SDK]Program link log: {log}");
        }

        gl.GetProgramiv(shaderProgram, gl.GL_LINK_STATUS, &status);
        if (status == gl.GL_FALSE)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     シェーダプログラムを検証する
    /// </summary>
    /// <param name="shaderProgram">検証するシェーダプログラムのアドレス</param>
    /// <returns>
    ///     true            .  正常
    ///     false           .  異常
    /// </returns>
    internal unsafe bool ValidateProgram(int shaderProgram)
    {
        int logLength,
            status;

        gl.ValidateProgram(shaderProgram);
        gl.GetProgramiv(shaderProgram, gl.GL_INFO_LOG_LENGTH, &logLength);
        if (logLength > 0)
        {
            gl.GetProgramInfoLog(shaderProgram, out var log);
            CubismLog.Error($"[Live2D SDK]Validate program log: {log}");
        }

        gl.GetProgramiv(shaderProgram, gl.GL_VALIDATE_STATUS, &status);
        if (status == gl.GL_FALSE)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Tegraプロセッサ対応。拡張方式による描画の有効・無効
    /// </summary>
    /// <param name="extMode">trueなら拡張方式で描画する</param>
    /// <param name="extPAMode">trueなら拡張方式のPA設定を有効にする</param>
    internal void SetExtShaderMode(bool extMode, bool extPAMode)
    {
        s_extMode = extMode;
        s_extPAMode = extPAMode;
    }

    /// <summary>
    ///     必要な頂点属性を設定する
    /// </summary>
    /// <param name="model">描画対象のモデル</param>
    /// <param name="index">描画対象のメッシュのインデックス</param>
    /// <param name="shaderSet">シェーダープログラムのセット</param>
    public void SetVertexAttributes(CubismShaderSet shaderSet)
    {
        // 頂点位置属性の設定
        gl.EnableVertexAttribArray(shaderSet.AttributePositionLocation);
        gl.VertexAttribPointer(
            shaderSet.AttributePositionLocation,
            2,
            gl.GL_FLOAT,
            false,
            4 * sizeof(float),
            0
        );

        // テクスチャ座標属性の設定
        gl.EnableVertexAttribArray(shaderSet.AttributeTexCoordLocation);
        gl.VertexAttribPointer(
            shaderSet.AttributeTexCoordLocation,
            2,
            gl.GL_FLOAT,
            false,
            4 * sizeof(float),
            2 * sizeof(float)
        );
    }

    /// <summary>
    ///     テクスチャの設定を行う
    /// </summary>
    /// <param name="renderer">レンダラー</param>
    /// <param name="model">描画対象のモデル</param>
    /// <param name="index">描画対象のメッシュのインデックス</param>
    /// <param name="shaderSet">シェーダープログラムのセット</param>
    public void SetupTexture(
        CubismRenderer_OpenGLES2 renderer,
        CubismModel model,
        int index,
        CubismShaderSet shaderSet
    )
    {
        var textureIndex = model.GetDrawableTextureIndex(index);
        var textureId = renderer.GetBindedTextureId(textureIndex);
        gl.ActiveTexture(gl.GL_TEXTURE0);
        gl.BindTexture(gl.GL_TEXTURE_2D, textureId);
        gl.Uniform1i(shaderSet.SamplerTexture0Location, 0);
    }

    /// <summary>
    ///     色関連のユニフォーム変数の設定を行う
    /// </summary>
    /// <param name="renderer">レンダラー</param>
    /// <param name="model">描画対象のモデル</param>
    /// <param name="index">描画対象のメッシュのインデックス</param>
    /// <param name="shaderSet">シェーダープログラムのセット</param>
    /// <param name="baseColor">ベースカラー</param>
    /// <param name="multiplyColor">乗算カラー</param>
    /// <param name="screenColor">スクリーンカラー</param>
    public void SetColorUniformVariables(
        CubismShaderSet shaderSet,
        CubismTextureColor baseColor,
        CubismTextureColor multiplyColor,
        CubismTextureColor screenColor
    )
    {
        gl.Uniform4f(
            shaderSet.UniformBaseColorLocation,
            baseColor.R,
            baseColor.G,
            baseColor.B,
            baseColor.A
        );
        gl.Uniform4f(
            shaderSet.UniformMultiplyColorLocation,
            multiplyColor.R,
            multiplyColor.G,
            multiplyColor.B,
            multiplyColor.A
        );
        gl.Uniform4f(
            shaderSet.UniformScreenColorLocation,
            screenColor.R,
            screenColor.G,
            screenColor.B,
            screenColor.A
        );
    }

    /// <summary>
    ///     カラーチャンネル関連のユニフォーム変数の設定を行う
    /// </summary>
    /// <param name="shaderSet">シェーダープログラムのセット</param>
    /// <param name="contextBuffer">描画コンテクスト</param>
    public void SetColorChannelUniformVariables(
        CubismShaderSet shaderSet,
        CubismClippingContext contextBuffer
    )
    {
        var channelIndex = contextBuffer.LayoutChannelIndex;
        var colorChannel = contextBuffer.Manager.GetChannelFlagAsColor(channelIndex);
        gl.Uniform4f(
            shaderSet.UnifromChannelFlagLocation,
            colorChannel.R,
            colorChannel.G,
            colorChannel.B,
            colorChannel.A
        );
    }
}
