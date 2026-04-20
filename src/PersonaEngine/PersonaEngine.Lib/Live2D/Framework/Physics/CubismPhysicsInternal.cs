using System.Numerics;

namespace PersonaEngine.Lib.Live2D.Framework.Physics;

/// <summary>
///     物理演算の適用先の種類。
/// </summary>
public enum CubismPhysicsTargetType
{
    /// <summary>
    ///     パラメータに対して適用
    /// </summary>
    CubismPhysicsTargetType_Parameter,
}

/// <summary>
///     物理演算の入力の種類。
/// </summary>
public enum CubismPhysicsSource
{
    /// <summary>
    ///     X軸の位置から
    /// </summary>
    CubismPhysicsSource_X,

    /// <summary>
    ///     Y軸の位置から
    /// </summary>
    CubismPhysicsSource_Y,

    /// <summary>
    ///     角度から
    /// </summary>
    CubismPhysicsSource_Angle,
}

/// <summary>
///     物理演算で使用する外部の力。
/// </summary>
public record PhysicsJsonEffectiveForces
{
    /// <summary>
    ///     重力
    /// </summary>
    public Vector2 Gravity;

    /// <summary>
    ///     風
    /// </summary>
    public Vector2 Wind;
}

/// <summary>
///     物理演算のパラメータ情報。
/// </summary>
public record CubismPhysicsParameter
{
    /// <summary>
    ///     パラメータID
    /// </summary>
    public required string Id;

    /// <summary>
    ///     適用先の種類
    /// </summary>
    public CubismPhysicsTargetType TargetType;
}

/// <summary>
///     物理演算の正規化情報。
/// </summary>
public record CubismPhysicsNormalization
{
    /// <summary>
    ///     デフォルト値
    /// </summary>
    public float Default;

    /// <summary>
    ///     最小値
    /// </summary>
    public float Maximum;

    /// <summary>
    ///     最大値
    /// </summary>
    public float Minimum;
}

/// <summary>
///     物理演算の演算に使用する物理点の情報。
/// </summary>
public record CubismPhysicsParticle
{
    /// <summary>
    ///     加速度
    /// </summary>
    public float Acceleration;

    /// <summary>
    ///     遅れ
    /// </summary>
    public float Delay;

    /// <summary>
    ///     現在かかっている力
    /// </summary>
    public Vector2 Force;

    /// <summary>
    ///     初期位置
    /// </summary>
    public Vector2 InitialPosition;

    /// <summary>
    ///     最後の重力
    /// </summary>
    public Vector2 LastGravity;

    /// <summary>
    ///     最後の位置
    /// </summary>
    public Vector2 LastPosition;

    /// <summary>
    ///     動きやすさ
    /// </summary>
    public float Mobility;

    /// <summary>
    ///     現在の位置
    /// </summary>
    public Vector2 Position;

    /// <summary>
    ///     距離
    /// </summary>
    public float Radius;

    /// <summary>
    ///     現在の速度
    /// </summary>
    public Vector2 Velocity;
}

/// <summary>
///     物理演算の物理点の管理。
/// </summary>
public record CubismPhysicsSubRig
{
    /// <summary>
    ///     入力の最初のインデックス
    /// </summary>
    public int BaseInputIndex;

    /// <summary>
    ///     出力の最初のインデックス
    /// </summary>
    public int BaseOutputIndex;

    /// <summary>
    ///     /物理点の最初のインデックス
    /// </summary>
    public int BaseParticleIndex;

    /// <summary>
    ///     入力の個数
    /// </summary>
    public int InputCount;

    /// <summary>
    ///     正規化された角度
    /// </summary>
    public required CubismPhysicsNormalization NormalizationAngle;

    /// <summary>
    ///     正規化された位置
    /// </summary>
    public required CubismPhysicsNormalization NormalizationPosition;

    /// <summary>
    ///     出力の個数
    /// </summary>
    public int OutputCount;

    /// <summary>
    ///     物理点の個数
    /// </summary>
    public int ParticleCount;
}

/// <summary>
///     正規化されたパラメータの取得関数の宣言。
/// </summary>
/// <param name="targetTranslation">演算結果の移動値</param>
/// <param name="targetAngle">演算結果の角度</param>
/// <param name="value">パラメータの値</param>
/// <param name="parameterMinimumValue">パラメータの最小値</param>
/// <param name="parameterMaximumValue">パラメータの最大値</param>
/// <param name="parameterDefaultValue">パラメータのデフォルト値</param>
/// <param name="normalizationPosition">正規化された位置</param>
/// <param name="normalizationAngle">正規化された角度</param>
/// <param name="isInverted">値が反転されているか？</param>
/// <param name="weight">重み</param>
public delegate void NormalizedPhysicsParameterValueGetter(
    ref Vector2 targetTranslation,
    ref float targetAngle,
    float value,
    float parameterMinimumValue,
    float parameterMaximumValue,
    float parameterDefaultValue,
    CubismPhysicsNormalization normalizationPosition,
    CubismPhysicsNormalization normalizationAngle,
    bool isInverted,
    float weight
);

/// <summary>
///     物理演算の値の取得関数の宣言。
/// </summary>
/// <param name="translation">移動値</param>
/// <param name="particles">物理点のリスト</param>
/// <param name="particleIndex"></param>
/// <param name="isInverted">値が反転されているか？</param>
/// <param name="parentGravity">重力</param>
/// <returns>値</returns>
public delegate float PhysicsValueGetter(
    Vector2 translation,
    CubismPhysicsParticle[] particles,
    int currentParticleIndex,
    int particleIndex,
    bool isInverted,
    Vector2 parentGravity
);

/// <summary>
///     物理演算のスケールの取得関数の宣言。
/// </summary>
/// <param name="translationScale">移動値のスケール</param>
/// <param name="angleScale">角度のスケール</param>
/// <returns>スケール値</returns>
public delegate float PhysicsScaleGetter(Vector2 translationScale, float angleScale);

/// <summary>
///     物理演算の入力情報。
/// </summary>
public record CubismPhysicsInput
{
    /// <summary>
    ///     正規化されたパラメータ値の取得関数
    /// </summary>
    public NormalizedPhysicsParameterValueGetter GetNormalizedParameterValue;

    /// <summary>
    ///     値が反転されているかどうか
    /// </summary>
    public bool Reflect;

    /// <summary>
    ///     入力元のパラメータ
    /// </summary>
    public required CubismPhysicsParameter Source;

    /// <summary>
    ///     入力元のパラメータのインデックス
    /// </summary>
    public int SourceParameterIndex;

    /// <summary>
    ///     入力の種類
    /// </summary>
    public CubismPhysicsSource Type;

    /// <summary>
    ///     重み
    /// </summary>
    public float Weight;
}

/// <summary>
///     物理演算の出力情報。
/// </summary>
public record CubismPhysicsOutput
{
    /// <summary>
    ///     角度のスケール
    /// </summary>
    public float AngleScale;

    /// <summary>
    ///     出力先のパラメータ
    /// </summary>
    public required CubismPhysicsParameter Destination;

    /// <summary>
    ///     出力先のパラメータのインデックス
    /// </summary>
    public int DestinationParameterIndex;

    /// <summary>
    ///     物理演算のスケール値の取得関数
    /// </summary>
    public PhysicsScaleGetter GetScale;

    /// <summary>
    ///     物理演算の値の取得関数
    /// </summary>
    public PhysicsValueGetter GetValue;

    /// <summary>
    ///     値が反転されているかどうか
    /// </summary>
    public bool Reflect;

    /// <summary>
    ///     移動値のスケール
    /// </summary>
    public Vector2 TranslationScale;

    /// <summary>
    ///     出力の種類
    /// </summary>
    public CubismPhysicsSource Type;

    /// <summary>
    ///     最小値を下回った時の値
    /// </summary>
    public float ValueBelowMinimum;

    /// <summary>
    ///     最大値をこえた時の値
    /// </summary>
    public float ValueExceededMaximum;

    /// <summary>
    ///     振り子のインデックス
    /// </summary>
    public int VertexIndex;

    /// <summary>
    ///     重み
    /// </summary>
    public float Weight;
}

/// <summary>
///     物理演算のデータ。
/// </summary>
public record CubismPhysicsRig
{
    /// <summary>
    ///     物理演算動作FPS
    /// </summary>
    public float Fps;

    /// <summary>
    ///     重力
    /// </summary>
    public Vector2 Gravity;

    /// <summary>
    ///     物理演算の入力のリスト
    /// </summary>
    public required CubismPhysicsInput[] Inputs;

    /// <summary>
    ///     物理演算の出力のリスト
    /// </summary>
    public required CubismPhysicsOutput[] Outputs;

    /// <summary>
    ///     物理演算の物理点のリスト
    /// </summary>
    public required CubismPhysicsParticle[] Particles;

    /// <summary>
    ///     物理演算の物理点の管理のリスト
    /// </summary>
    public required CubismPhysicsSubRig[] Settings;

    /// <summary>
    ///     物理演算の物理点の個数
    /// </summary>
    public int SubRigCount;

    /// <summary>
    ///     風
    /// </summary>
    public Vector2 Wind;
}
