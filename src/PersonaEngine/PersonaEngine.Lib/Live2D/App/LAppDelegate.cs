using PersonaEngine.Lib.Live2D.Framework;
using PersonaEngine.Lib.Live2D.Framework.Core;
using PersonaEngine.Lib.Live2D.Framework.Rendering;
using PersonaEngine.Lib.Live2D.Framework.Rendering.OpenGL;

namespace PersonaEngine.Lib.Live2D.App;

/// <summary>
///     アプリケーションクラス。
///     Cubism SDK の管理を行う。
/// </summary>
public class LAppDelegate : IDisposable
{
    /// <summary>
    ///     Cubism SDK Allocator
    /// </summary>
    private readonly LAppAllocator _cubismAllocator;

    /// <summary>
    ///     Cubism SDK Option
    /// </summary>
    private readonly Option _cubismOption;

    /// <summary>
    ///     クリックしているか
    /// </summary>
    private bool _captured;

    /// <summary>
    ///     マウスX座標
    /// </summary>
    private float _mouseX;

    /// <summary>
    ///     マウスY座標
    /// </summary>
    private float _mouseY;

    /// <summary>
    ///     Initialize関数で設定したウィンドウ高さ
    /// </summary>
    private int _windowHeight;

    /// <summary>
    ///     Initialize関数で設定したウィンドウ幅
    /// </summary>
    private int _windowWidth;

    public LAppDelegate(OpenGLApi gl, LogFunction log)
    {
        GL = gl;

        View = new LAppView(this);
        TextureManager = new LAppTextureManager(this);
        _cubismAllocator = new LAppAllocator();

        //テクスチャサンプリング設定
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, GL.GL_LINEAR);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, GL.GL_LINEAR);

        //透過設定
        GL.Enable(GL.GL_BLEND);
        GL.BlendFunc(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA);

        // ウィンドウサイズ記憶
        GL.GetWindowSize(out _windowWidth, out _windowHeight);

        //AppViewの初期化
        View.Initialize();

        // Cubism SDK の初期化
        _cubismOption = new Option
        {
            LogFunction = log,
            LoggingLevel = LAppDefine.CubismLoggingLevel,
        };
        CubismFramework.StartUp(_cubismAllocator, _cubismOption);

        //Initialize cubism
        CubismFramework.Initialize();

        //load model
        Live2dManager = new LAppLive2DManager(this);

        LAppPal.DeltaTime = 0;
    }

    /// <summary>
    ///     テクスチャマネージャー
    /// </summary>
    public LAppTextureManager TextureManager { get; private set; }

    public LAppLive2DManager Live2dManager { get; private set; }

    public OpenGLApi GL { get; }

    /// <summary>
    ///     View情報
    /// </summary>
    public LAppView View { get; private set; }

    public CubismTextureColor BGColor { get; set; } = new(0, 0, 0, 0);

    /// <summary>
    ///     解放する。
    /// </summary>
    public void Dispose()
    {
        // リソースを解放
        Live2dManager.Dispose();

        //Cubism SDK の解放
        CubismFramework.Dispose();

        GC.SuppressFinalize(this);
    }

    public void Resize()
    {
        GL.GetWindowSize(out var width, out var height);
        if ((_windowWidth != width || _windowHeight != height) && width > 0 && height > 0)
        {
            //AppViewの初期化
            View.Initialize();
            // サイズを保存しておく
            _windowWidth = width;
            _windowHeight = height;
        }
    }

    public void Update(float tick)
    {
        // 時間更新
        LAppPal.DeltaTime = tick;
    }

    /// <summary>
    ///     実行処理。
    /// </summary>
    public void Run()
    {
        // 画面の初期化
        GL.ClearColor(BGColor.R, BGColor.G, BGColor.B, BGColor.A);
        GL.Clear(GL.GL_COLOR_BUFFER_BIT | GL.GL_DEPTH_BUFFER_BIT);
        GL.ClearDepthf(1.0f);

        //描画更新
        View.Render();
    }

    /// <summary>
    ///     OpenGL用 glfwSetMouseButtonCallback用関数。
    /// </summary>
    /// <param name="button">ボタン種類</param>
    /// <param name="action">実行結果</param>
    public void OnMouseCallBack(bool press)
    {
        if (press)
        {
            _captured = true;
            View.OnTouchesBegan(_mouseX, _mouseY);
        }
        else
        {
            if (_captured)
            {
                _captured = false;
                View.OnTouchesEnded(_mouseX, _mouseY);
            }
        }
    }

    /// <summary>
    ///     OpenGL用 glfwSetCursorPosCallback用関数。
    /// </summary>
    /// <param name="x">x座標</param>
    /// <param name="y">x座標</param>
    public void OnMouseCallBack(float x, float y)
    {
        if (!_captured)
        {
            return;
        }

        _mouseX = x;
        _mouseY = y;

        View.OnTouchesMoved(_mouseX, _mouseY);
    }

    public void StartSpeaking(string filePath)
    {
        Live2dManager.StartSpeaking(filePath);
    }
}
