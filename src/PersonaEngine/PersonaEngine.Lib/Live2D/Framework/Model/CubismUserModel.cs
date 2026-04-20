using PersonaEngine.Lib.Live2D.Framework.Effect;
using PersonaEngine.Lib.Live2D.Framework.Math;
using PersonaEngine.Lib.Live2D.Framework.Motion;
using PersonaEngine.Lib.Live2D.Framework.Physics;
using PersonaEngine.Lib.Live2D.Framework.Rendering;

namespace PersonaEngine.Lib.Live2D.Framework.Model;

/// <summary>
///     ユーザーが実際に使用するモデルの基底クラス。これを継承してユーザーが実装する。
/// </summary>
public abstract class CubismUserModel : IDisposable
{
    /// <summary>
    ///     X軸方向の加速度
    /// </summary>
    protected float _accelerationX;

    /// <summary>
    ///     Y軸方向の加速度
    /// </summary>
    protected float _accelerationY;

    /// <summary>
    ///     Z軸方向の加速度
    /// </summary>
    protected float _accelerationZ;

    /// <summary>
    ///     呼吸
    /// </summary>
    protected CubismBreath _breath;

    /// <summary>
    ///     マウスドラッグ
    /// </summary>
    protected CubismTargetPoint _dragManager;

    /// <summary>
    ///     マウスドラッグのX位置
    /// </summary>
    protected float _dragX;

    /// <summary>
    ///     マウスドラッグのY位置
    /// </summary>
    protected float _dragY;

    /// <summary>
    ///     表情管理
    /// </summary>
    protected CubismExpressionMotionManager _expressionManager;

    /// <summary>
    ///     自動まばたき
    /// </summary>
    protected CubismEyeBlink? _eyeBlink;

    /// <summary>
    ///     最後のリップシンクの制御値
    /// </summary>
    protected float _lastLipSyncValue;

    /// <summary>
    ///     リップシンクするかどうか
    /// </summary>
    protected bool _lipSync;

    /// <summary>
    ///     Mocデータ
    /// </summary>
    protected CubismMoc _moc;

    /// <summary>
    ///     MOC3整合性検証するかどうか
    /// </summary>
    protected bool _mocConsistency;

    /// <summary>
    ///     ユーザデータ
    /// </summary>
    protected CubismModelUserData? _modelUserData;

    /// <summary>
    ///     モーション管理
    /// </summary>
    protected CubismMotionManager _motionManager;

    /// <summary>
    ///     物理演算
    /// </summary>
    protected CubismPhysics? _physics;

    /// <summary>
    ///     ポーズ管理
    /// </summary>
    protected CubismPose? _pose;

    /// <summary>
    ///     コンストラクタ。
    /// </summary>
    public CubismUserModel()
    {
        _lipSync = true;

        Opacity = 1.0f;

        // モーションマネージャーを作成
        // MotionQueueManagerクラスからの継承なので使い方は同じ
        _motionManager = new CubismMotionManager();
        _motionManager.SetEventCallback(CubismDefaultMotionEventCallback, this);

        // 表情モーションマネージャを作成
        _expressionManager = new CubismExpressionMotionManager();

        // ドラッグによるアニメーション
        _dragManager = new CubismTargetPoint();
    }

    /// <summary>
    ///     レンダラ
    /// </summary>
    public CubismRenderer? Renderer { get; private set; }

    /// <summary>
    ///     モデル行列
    /// </summary>
    public CubismModelMatrix ModelMatrix { get; protected set; }

    /// <summary>
    ///     Modelインスタンス
    /// </summary>
    public CubismModel Model => _moc.Model;

    /// <summary>
    ///     初期化されたかどうか
    /// </summary>
    public bool Initialized { get; set; }

    /// <summary>
    ///     更新されたかどうか
    /// </summary>
    public bool Updating { get; set; }

    /// <summary>
    ///     不透明度
    /// </summary>
    public float Opacity { get; set; }

    public void Dispose()
    {
        _moc.Dispose();

        DeleteRenderer();
    }

    /// <summary>
    ///     CubismMotionQueueManagerにイベント用に登録するためのCallback。
    ///     CubismUserModelの継承先のEventFiredを呼ぶ。
    /// </summary>
    /// <param name="eventValue">発火したイベントの文字列データ</param>
    /// <param name="customData">CubismUserModelを継承したインスタンスを想定</param>
    public static void CubismDefaultMotionEventCallback(
        CubismUserModel? customData,
        string eventValue
    )
    {
        customData?.MotionEventFired(eventValue);
    }

    /// <summary>
    ///     マウスドラッグの情報を設定する。
    /// </summary>
    /// <param name="x">ドラッグしているカーソルのX位置</param>
    /// <param name="y">ドラッグしているカーソルのY位置</param>
    public void SetDragging(float x, float y)
    {
        _dragManager.Set(x, y);
    }

    /// <summary>
    ///     加速度の情報を設定する。
    /// </summary>
    /// <param name="x">X軸方向の加速度</param>
    /// <param name="y">Y軸方向の加速度</param>
    /// <param name="z">Z軸方向の加速度</param>
    protected void SetAcceleration(float x, float y, float z)
    {
        _accelerationX = x;
        _accelerationY = y;
        _accelerationZ = z;
    }

    /// <summary>
    ///     モデルデータを読み込む。
    /// </summary>
    /// <param name="buffer">moc3ファイルが読み込まれているバッファ</param>
    /// <param name="shouldCheckMocConsistency">MOCの整合性チェックフラグ(初期値 : false)</param>
    protected void LoadModel(byte[] buffer, bool shouldCheckMocConsistency = false)
    {
        _moc = new CubismMoc(buffer, shouldCheckMocConsistency);
        Model.SaveParameters();
        ModelMatrix = new CubismModelMatrix(Model.GetCanvasWidth(), Model.GetCanvasHeight());
    }

    /// <summary>
    ///     ポーズデータを読み込む。
    /// </summary>
    /// <param name="buffer">pose3.jsonが読み込まれているバッファ</param>
    protected void LoadPose(string buffer)
    {
        _pose = new CubismPose(buffer);
    }

    /// <summary>
    ///     物理演算データを読み込む。
    /// </summary>
    /// <param name="buffer">physics3.jsonが読み込まれているバッファ</param>
    protected void LoadPhysics(string buffer)
    {
        _physics = new CubismPhysics(buffer);
    }

    /// <summary>
    ///     ユーザーデータを読み込む。
    /// </summary>
    /// <param name="buffer">userdata3.jsonが読み込まれているバッファ</param>
    protected void LoadUserData(string buffer)
    {
        _modelUserData = new CubismModelUserData(buffer);
    }

    /// <summary>
    ///     指定した位置にDrawableがヒットしているかどうかを取得する。
    /// </summary>
    /// <param name="drawableId">検証したいDrawableのID</param>
    /// <param name="pointX">X位置</param>
    /// <param name="pointY">Y位置</param>
    /// <returns>
    ///     true    ヒットしている
    ///     false   ヒットしていない
    /// </returns>
    public unsafe bool IsHit(string drawableId, float pointX, float pointY)
    {
        var drawIndex = Model.GetDrawableIndex(drawableId);

        if (drawIndex < 0)
        {
            return false; // 存在しない場合はfalse
        }

        var count = Model.GetDrawableVertexCount(drawIndex);
        var vertices = Model.GetDrawableVertices(drawIndex);

        var left = vertices[0];
        var right = vertices[0];
        var top = vertices[1];
        var bottom = vertices[1];

        for (var j = 1; j < count; ++j)
        {
            var x = vertices[CubismFramework.VertexOffset + j * CubismFramework.VertexStep];
            var y = vertices[CubismFramework.VertexOffset + j * CubismFramework.VertexStep + 1];

            if (x < left)
            {
                left = x; // Min x
            }

            if (x > right)
            {
                right = x; // Max x
            }

            if (y < top)
            {
                top = y; // Min y
            }

            if (y > bottom)
            {
                bottom = y; // Max y
            }
        }

        var tx = ModelMatrix.InvertTransformX(pointX);
        var ty = ModelMatrix.InvertTransformY(pointY);

        return left <= tx && tx <= right && top <= ty && ty <= bottom;
    }

    /// <summary>
    ///     レンダラを生成して初期化を実行する。
    /// </summary>
    protected void CreateRenderer(CubismRenderer renderer)
    {
        if (Renderer != null)
        {
            DeleteRenderer();
        }

        Renderer = renderer;
    }

    /// <summary>
    ///     レンダラを解放する。
    /// </summary>
    protected void DeleteRenderer()
    {
        if (Renderer != null)
        {
            Renderer.Dispose();
            Renderer = null;
        }
    }

    /// <summary>
    ///     Eventが再生処理時にあった場合の処理をする。
    ///     継承で上書きすることを想定している。
    ///     上書きしない場合はログ出力をする。
    /// </summary>
    /// <param name="eventValue">発火したイベントの文字列データ</param>
    protected abstract void MotionEventFired(string eventValue);
}
