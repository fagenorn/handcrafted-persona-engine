using PersonaEngine.Lib.Live2D.Framework.Model;

namespace PersonaEngine.Lib.Live2D.Framework.Motion;

/// <summary>
///     モーション再生終了コールバック関数定義
/// </summary>
public delegate void FinishedMotionCallback(CubismModel model, ACubismMotion self);

/// <summary>
///     イベントのコールバックに登録できる関数の型情報
/// </summary>
/// <param name="eventValue">発火したイベントの文字列データ</param>
/// <param name="customData">コールバックに返される登録時に指定されたデータ</param>
public delegate void CubismMotionEventFunction(CubismUserModel? customData, string eventValue);

/// <summary>
///     モーションカーブのセグメントの評価関数。
/// </summary>
/// <param name="points">モーションカーブの制御点リスト</param>
/// <param name="time">評価する時間[秒]</param>
public delegate float csmMotionSegmentEvaluationFunction(
    CubismMotionPoint[] points,
    int start,
    float time
);

/// <summary>
///     モーションの優先度定数
/// </summary>
public enum MotionPriority
{
    PriorityNone = 0,

    PriorityIdle = 1,

    PriorityLow = 2,

    PriorityNormal = 3,

    PriorityForce = 4,
}

/// <summary>
///     表情パラメータ値の計算方式
/// </summary>
public enum ExpressionBlendType
{
    /// <summary>
    ///     加算
    /// </summary>
    Add = 0,

    /// <summary>
    ///     乗算
    /// </summary>
    Multiply = 1,

    /// <summary>
    ///     上書き
    /// </summary>
    Overwrite = 2,
}

/// <summary>
///     表情のパラメータ情報の構造体。
/// </summary>
public record ExpressionParameter
{
    /// <summary>
    ///     パラメータID
    /// </summary>
    public required string ParameterId { get; set; }

    /// <summary>
    ///     パラメータの演算種類
    /// </summary>
    public ExpressionBlendType BlendType { get; set; }

    /// <summary>
    ///     値
    /// </summary>
    public float Value { get; set; }
}

/// <summary>
///     モーションカーブの種類。
/// </summary>
public enum CubismMotionCurveTarget
{
    /// <summary>
    ///     モデルに対して
    /// </summary>
    Model,

    /// <summary>
    ///     パラメータに対して
    /// </summary>
    Parameter,

    /// <summary>
    ///     パーツの不透明度に対して
    /// </summary>
    PartOpacity,
}

/// <summary>
///     モーションカーブのセグメントの種類。
/// </summary>
public enum CubismMotionSegmentType
{
    /// <summary>
    ///     リニア
    /// </summary>
    Linear = 0,

    /// <summary>
    ///     ベジェ曲線
    /// </summary>
    Bezier = 1,

    /// <summary>
    ///     ステップ
    /// </summary>
    Stepped = 2,

    /// <summary>
    ///     インバースステップ
    /// </summary>
    InverseStepped = 3,
}

/// <summary>
///     モーションカーブの制御点。
/// </summary>
public struct CubismMotionPoint
{
    /// <summary>
    ///     時間[秒]
    /// </summary>
    public float Time;

    /// <summary>
    ///     値
    /// </summary>
    public float Value;
}

/// <summary>
///     モーションカーブのセグメント。
/// </summary>
public record CubismMotionSegment
{
    /// <summary>
    ///     最初のセグメントへのインデックス
    /// </summary>
    public int BasePointIndex;

    /// <summary>
    ///     使用する評価関数
    /// </summary>
    public csmMotionSegmentEvaluationFunction Evaluate;

    /// <summary>
    ///     セグメントの種類
    /// </summary>
    public CubismMotionSegmentType SegmentType;
}

/// <summary>
///     モーションカーブ。
/// </summary>
public record CubismMotionCurve
{
    /// <summary>
    ///     最初のセグメントのインデックス
    /// </summary>
    public int BaseSegmentIndex;

    /// <summary>
    ///     フェードインにかかる時間[秒]
    /// </summary>
    public float FadeInTime;

    /// <summary>
    ///     フェードアウトにかかる時間[秒]
    /// </summary>
    public float FadeOutTime;

    /// <summary>
    ///     カーブのID
    /// </summary>
    public string Id;

    /// <summary>
    ///     セグメントの個数
    /// </summary>
    public int SegmentCount;

    /// <summary>
    ///     カーブの種類
    /// </summary>
    public CubismMotionCurveTarget Type;
}

/// <summary>
///     イベント。
/// </summary>
public record CubismMotionEvent
{
    public float FireTime;

    public string Value;
}

/// <summary>
///     モーションデータ。
/// </summary>
public record CubismMotionData
{
    /// <summary>
    ///     カーブの個数
    /// </summary>
    public int CurveCount;

    /// <summary>
    ///     カーブのリスト
    /// </summary>
    public CubismMotionCurve[] Curves;

    /// <summary>
    ///     モーションの長さ[秒]
    /// </summary>
    public float Duration;

    /// <summary>
    ///     UserDataの個数
    /// </summary>
    public int EventCount;

    /// <summary>
    ///     イベントのリスト
    /// </summary>
    public CubismMotionEvent[] Events;

    /// <summary>
    ///     フレームレート
    /// </summary>
    public float Fps;

    /// <summary>
    ///     ループするかどうか
    /// </summary>
    public bool Loop;

    /// <summary>
    ///     ポイントのリスト
    /// </summary>
    public CubismMotionPoint[] Points;

    /// <summary>
    ///     セグメントのリスト
    /// </summary>
    public CubismMotionSegment[] Segments;
}
