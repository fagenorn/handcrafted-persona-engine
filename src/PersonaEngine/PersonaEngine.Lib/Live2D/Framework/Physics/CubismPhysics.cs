using System.Numerics;
using System.Text.Json;
using PersonaEngine.Lib.Live2D.Framework.Core;
using PersonaEngine.Lib.Live2D.Framework.Math;
using PersonaEngine.Lib.Live2D.Framework.Model;

namespace PersonaEngine.Lib.Live2D.Framework.Physics;

/// <summary>
///     物理演算のクラス。
/// </summary>
public class CubismPhysics
{
    /// physics types tags.
    public const string PhysicsTypeTagX = "X";

    public const string PhysicsTypeTagY = "Y";

    public const string PhysicsTypeTagAngle = "Angle";

    /// Constant of air resistance.
    public const float AirResistance = 5.0f;

    /// Constant of maximum weight of input and output ratio.
    public const float MaximumWeight = 100.0f;

    /// Constant of threshold of movement.
    public const float MovementThreshold = 0.001f;

    /// Constant of maximum allowed delta time
    public const float MaxDeltaTime = 5.0f;

    /// <summary>
    ///     最新の振り子計算の結果
    /// </summary>
    private readonly List<float[]> _currentRigOutputs = [];

    /// <summary>
    ///     物理演算のデータ
    /// </summary>
    private readonly CubismPhysicsRig _physicsRig;

    /// <summary>
    ///     一つ前の振り子計算の結果
    /// </summary>
    private readonly List<float[]> _previousRigOutputs = [];

    /// <summary>
    ///     物理演算が処理していない時間
    /// </summary>
    private float _currentRemainTime;

    /// <summary>
    ///     Evaluateで利用するパラメータのキャッシュs
    /// </summary>
    private float[] _parameterCaches = [];

    /// <summary>
    ///     UpdateParticlesが動くときの入力をキャッシュ
    /// </summary>
    private float[] _parameterInputCaches = [];

    /// <summary>
    ///     重力方向
    /// </summary>
    public Vector2 Gravity;

    /// <summary>
    ///     風の方向
    /// </summary>
    public Vector2 Wind;

    /// <summary>
    ///     インスタンスを作成する。
    /// </summary>
    /// <param name="buffer">physics3.jsonが読み込まれいるバッファ</param>
    public CubismPhysics(string buffer)
    {
        // set default options.
        Gravity.Y = -1.0f;
        Gravity.X = 0;
        Wind.X = 0;
        Wind.Y = 0;
        _currentRemainTime = 0.0f;

        using var stream = File.Open(buffer, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var obj =
            JsonSerializer.Deserialize(stream, CubismPhysicsObjContext.Default.CubismPhysicsObj)
            ?? throw new Exception("Load Physics error");

        _physicsRig = new CubismPhysicsRig
        {
            Gravity = obj.Meta.EffectiveForces.Gravity,
            Wind = obj.Meta.EffectiveForces.Wind,
            SubRigCount = obj.Meta.PhysicsSettingCount,
            Fps = obj.Meta.Fps,
            Settings = new CubismPhysicsSubRig[obj.Meta.PhysicsSettingCount],
            Inputs = new CubismPhysicsInput[obj.Meta.TotalInputCount],
            Outputs = new CubismPhysicsOutput[obj.Meta.TotalOutputCount],
            Particles = new CubismPhysicsParticle[obj.Meta.VertexCount],
        };

        _currentRigOutputs.Clear();
        _previousRigOutputs.Clear();

        int inputIndex = 0,
            outputIndex = 0,
            particleIndex = 0;

        for (var i = 0; i < _physicsRig.Settings.Length; ++i)
        {
            var set = obj.PhysicsSettings[i];
            _physicsRig.Settings[i] = new CubismPhysicsSubRig
            {
                NormalizationPosition = new CubismPhysicsNormalization
                {
                    Minimum = set.Normalization.Position.Minimum,
                    Maximum = set.Normalization.Position.Maximum,
                    Default = set.Normalization.Position.Default,
                },
                NormalizationAngle = new CubismPhysicsNormalization
                {
                    Minimum = set.Normalization.Angle.Minimum,
                    Maximum = set.Normalization.Angle.Maximum,
                    Default = set.Normalization.Angle.Default,
                },
                // Input
                InputCount = set.Input.Count,
                BaseInputIndex = inputIndex,
            };

            for (var j = 0; j < _physicsRig.Settings[i].InputCount; ++j)
            {
                var input = set.Input[j];
                _physicsRig.Inputs[inputIndex + j] = new CubismPhysicsInput
                {
                    SourceParameterIndex = -1,
                    Weight = input.Weight,
                    Reflect = input.Reflect,
                    Source = new CubismPhysicsParameter
                    {
                        TargetType = CubismPhysicsTargetType.CubismPhysicsTargetType_Parameter,
                        Id = input.Source.Id,
                    },
                };

                if (input.Type == PhysicsTypeTagX)
                {
                    _physicsRig.Inputs[inputIndex + j].Type =
                        CubismPhysicsSource.CubismPhysicsSource_X;
                    _physicsRig.Inputs[inputIndex + j].GetNormalizedParameterValue =
                        GetInputTranslationXFromNormalizedParameterValue;
                }
                else if (input.Type == PhysicsTypeTagY)
                {
                    _physicsRig.Inputs[inputIndex + j].Type =
                        CubismPhysicsSource.CubismPhysicsSource_Y;
                    _physicsRig.Inputs[inputIndex + j].GetNormalizedParameterValue =
                        GetInputTranslationYFromNormalizedParameterValue;
                }
                else if (input.Type == PhysicsTypeTagAngle)
                {
                    _physicsRig.Inputs[inputIndex + j].Type =
                        CubismPhysicsSource.CubismPhysicsSource_Angle;
                    _physicsRig.Inputs[inputIndex + j].GetNormalizedParameterValue =
                        GetInputAngleFromNormalizedParameterValue;
                }
            }

            inputIndex += _physicsRig.Settings[i].InputCount;

            // Output
            _physicsRig.Settings[i].OutputCount = set.Output.Count;
            _physicsRig.Settings[i].BaseOutputIndex = outputIndex;

            _currentRigOutputs.Add(new float[set.Output.Count]);
            _previousRigOutputs.Add(new float[set.Output.Count]);

            for (var j = 0; j < _physicsRig.Settings[i].OutputCount; ++j)
            {
                var output = set.Output[j];
                _physicsRig.Outputs[outputIndex + j] = new CubismPhysicsOutput
                {
                    DestinationParameterIndex = -1,
                    VertexIndex = output.VertexIndex,
                    AngleScale = output.Scale,
                    Weight = output.Weight,
                    Destination = new CubismPhysicsParameter
                    {
                        TargetType = CubismPhysicsTargetType.CubismPhysicsTargetType_Parameter,
                        Id = output.Destination.Id,
                    },
                    Reflect = output.Reflect,
                };

                var key = output.Type;
                if (key == PhysicsTypeTagX)
                {
                    _physicsRig.Outputs[outputIndex + j].Type =
                        CubismPhysicsSource.CubismPhysicsSource_X;
                    _physicsRig.Outputs[outputIndex + j].GetValue = GetOutputTranslationX;
                    _physicsRig.Outputs[outputIndex + j].GetScale = GetOutputScaleTranslationX;
                }
                else if (key == PhysicsTypeTagY)
                {
                    _physicsRig.Outputs[outputIndex + j].Type =
                        CubismPhysicsSource.CubismPhysicsSource_Y;
                    _physicsRig.Outputs[outputIndex + j].GetValue = GetOutputTranslationY;
                    _physicsRig.Outputs[outputIndex + j].GetScale = GetOutputScaleTranslationY;
                }
                else if (key == PhysicsTypeTagAngle)
                {
                    _physicsRig.Outputs[outputIndex + j].Type =
                        CubismPhysicsSource.CubismPhysicsSource_Angle;
                    _physicsRig.Outputs[outputIndex + j].GetValue = GetOutputAngle;
                    _physicsRig.Outputs[outputIndex + j].GetScale = GetOutputScaleAngle;
                }
            }

            outputIndex += _physicsRig.Settings[i].OutputCount;

            // Particle
            _physicsRig.Settings[i].ParticleCount = set.Vertices.Count;
            _physicsRig.Settings[i].BaseParticleIndex = particleIndex;
            for (var j = 0; j < _physicsRig.Settings[i].ParticleCount; ++j)
            {
                var par = set.Vertices[j];
                _physicsRig.Particles[particleIndex + j] = new CubismPhysicsParticle
                {
                    Mobility = par.Mobility,
                    Delay = par.Delay,
                    Acceleration = par.Acceleration,
                    Radius = par.Radius,
                    Position = par.Position,
                };
            }

            particleIndex += _physicsRig.Settings[i].ParticleCount;
        }

        Initialize();

        _physicsRig.Gravity.Y = 0;
    }

    /// <summary>
    ///     パラメータをリセットする。
    /// </summary>
    public void Reset()
    {
        // set default options.
        Gravity.Y = -1.0f;
        Gravity.X = 0.0f;
        Wind.X = 0.0f;
        Wind.Y = 0.0f;

        _physicsRig.Gravity.X = 0.0f;
        _physicsRig.Gravity.Y = 0.0f;
        _physicsRig.Wind.X = 0.0f;
        _physicsRig.Wind.Y = 0.0f;

        Initialize();
    }

    /// <summary>
    ///     現在のパラメータ値で物理演算が安定化する状態を演算する。
    /// </summary>
    /// <param name="model">物理演算の結果を適用するモデル</param>
    public unsafe void Stabilization(CubismModel model)
    {
        float totalAngle;
        float weight;
        float radAngle;
        float outputValue;
        Vector2 totalTranslation;
        int i,
            settingIndex,
            particleIndex;

        float* parameterValues;
        float* parameterMaximumValues;
        float* parameterMinimumValues;
        float* parameterDefaultValues;

        parameterValues = CubismCore.GetParameterValues(model.Model);
        parameterMaximumValues = CubismCore.GetParameterMaximumValues(model.Model);
        parameterMinimumValues = CubismCore.GetParameterMinimumValues(model.Model);
        parameterDefaultValues = CubismCore.GetParameterDefaultValues(model.Model);

        if (_parameterCaches.Length < model.GetParameterCount())
        {
            _parameterCaches = new float[model.GetParameterCount()];
        }

        if (_parameterInputCaches.Length < model.GetParameterCount())
        {
            _parameterInputCaches = new float[model.GetParameterCount()];
        }

        for (var j = 0; j < model.GetParameterCount(); ++j)
        {
            _parameterCaches[j] = parameterValues[j];
            _parameterInputCaches[j] = parameterValues[j];
        }

        for (settingIndex = 0; settingIndex < _physicsRig.SubRigCount; ++settingIndex)
        {
            totalAngle = 0.0f;
            totalTranslation.X = 0.0f;
            totalTranslation.Y = 0.0f;

            var currentSetting = _physicsRig.Settings[settingIndex];
            var currentInputIndex = currentSetting.BaseInputIndex;
            var currentOutputIndex = currentSetting.BaseOutputIndex;
            var currentParticleIndex = currentSetting.BaseParticleIndex;

            // Load input parameters
            for (i = 0; i < currentSetting.InputCount; ++i)
            {
                weight = _physicsRig.Inputs[i + currentInputIndex].Weight / MaximumWeight;

                if (_physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex == -1)
                {
                    _physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex =
                        model.GetParameterIndex(
                            _physicsRig.Inputs[i + currentInputIndex].Source.Id
                        );
                }

                _physicsRig
                    .Inputs[i + currentInputIndex]
                    .GetNormalizedParameterValue(
                        ref totalTranslation,
                        ref totalAngle,
                        parameterValues[
                            _physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex
                        ],
                        parameterMinimumValues[
                            _physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex
                        ],
                        parameterMaximumValues[
                            _physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex
                        ],
                        parameterDefaultValues[
                            _physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex
                        ],
                        currentSetting.NormalizationPosition,
                        currentSetting.NormalizationAngle,
                        _physicsRig.Inputs[i + currentInputIndex].Reflect,
                        weight
                    );

                _parameterCaches[_physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex] =
                    parameterValues[_physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex];
            }

            radAngle = CubismMath.DegreesToRadian(-totalAngle);

            totalTranslation.X =
                totalTranslation.X * MathF.Cos(radAngle) - totalTranslation.Y * MathF.Sin(radAngle);
            totalTranslation.Y =
                totalTranslation.X * MathF.Sin(radAngle) + totalTranslation.Y * MathF.Cos(radAngle);

            // Calculate particles position.
            UpdateParticlesForStabilization(
                _physicsRig.Particles,
                currentSetting.BaseParticleIndex,
                currentSetting.ParticleCount,
                totalTranslation,
                totalAngle,
                Wind,
                MovementThreshold * currentSetting.NormalizationPosition.Maximum
            );

            // Update output parameters.
            for (i = 0; i < currentSetting.OutputCount; ++i)
            {
                particleIndex = _physicsRig.Outputs[i + currentOutputIndex].VertexIndex;

                if (_physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex == -1)
                {
                    _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex =
                        model.GetParameterIndex(
                            _physicsRig.Outputs[i + currentOutputIndex].Destination.Id
                        );
                }

                if (particleIndex < 1 || particleIndex >= currentSetting.ParticleCount)
                {
                    continue;
                }

                Vector2 translation = new()
                {
                    X =
                        _physicsRig.Particles[particleIndex + currentParticleIndex].Position.X
                        - _physicsRig
                            .Particles[particleIndex - 1 + currentParticleIndex]
                            .Position
                            .X,
                    Y =
                        _physicsRig.Particles[particleIndex + currentParticleIndex].Position.Y
                        - _physicsRig
                            .Particles[particleIndex - 1 + currentParticleIndex]
                            .Position
                            .Y,
                };

                outputValue = _physicsRig
                    .Outputs[i + currentOutputIndex]
                    .GetValue(
                        translation,
                        _physicsRig.Particles,
                        currentParticleIndex,
                        particleIndex,
                        _physicsRig.Outputs[i + currentOutputIndex].Reflect,
                        Gravity
                    );

                _currentRigOutputs[settingIndex][i] = outputValue;
                _previousRigOutputs[settingIndex][i] = outputValue;

                UpdateOutputParameterValue(
                    ref parameterValues[
                        _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex
                    ],
                    parameterMinimumValues[
                        _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex
                    ],
                    parameterMaximumValues[
                        _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex
                    ],
                    outputValue,
                    _physicsRig.Outputs[i + currentOutputIndex]
                );

                _parameterCaches[
                    _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex
                ] = parameterValues[
                    _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex
                ];
            }
        }
    }

    /// <summary>
    ///     物理演算を評価する。
    ///     Pendulum interpolation weights
    ///     振り子の計算結果は保存され、パラメータへの出力は保存された前回の結果で補間されます。
    ///     The result of the pendulum calculation is saved and
    ///     the output to the parameters is interpolated with the saved previous result of the pendulum calculation.
    ///     図で示すと[1]と[2]で補間されます。
    ///     The figure shows the interpolation between [1] and [2].
    ///     補間の重みは最新の振り子計算タイミングと次回のタイミングの間で見た現在時間で決定する。
    ///     The weight of the interpolation are determined by the current time seen between
    ///     the latest pendulum calculation timing and the next timing.
    ///     図で示すと[2]と[4]の間でみた(3)の位置の重みになる。
    ///     Figure shows the weight of position (3) as seen between [2] and [4].
    ///     解釈として振り子計算のタイミングと重み計算のタイミングがズレる。
    ///     As an interpretation, the pendulum calculation and weights are misaligned.
    ///     physics3.jsonにFPS情報が存在しない場合は常に前の振り子状態で設定される。
    ///     If there is no FPS information in physics3.json, it is always set in the previous pendulum state.
    ///     この仕様は補間範囲を逸脱したことが原因の震えたような見た目を回避を目的にしている。
    ///     The purpose of this specification is to avoid the quivering appearance caused by deviations from the interpolation
    ///     range.
    ///     ------------ time -------------->
    ///     |+++++|------|
    ///     <- weight
    ///         ==[1]====#=====[2]---(3)----(4)
    ///         ^ output contents
    ///
    ///         1 :_previousRigOutputs
    ///         2 :_currentRigOutputs
    ///         3 :_currentRemainTime ( now rendering)
    ///         4 :next particles timing
    ///
    ///         @ param model
    ///         @ param deltaTimeSeconds rendering delta time.
    /// </summary>
    /// <param name="model">物理演算の結果を適用するモデル</param>
    /// <param name="deltaTimeSeconds">デルタ時間[秒]</param>
    public unsafe void Evaluate(CubismModel model, float deltaTimeSeconds)
    {
        float totalAngle;
        float weight;
        float radAngle;
        float outputValue;
        Vector2 totalTranslation;
        int i,
            settingIndex,
            particleIndex;

        if (0.0f >= deltaTimeSeconds)
        {
            return;
        }

        float physicsDeltaTime;
        _currentRemainTime += deltaTimeSeconds;
        if (_currentRemainTime > MaxDeltaTime)
        {
            _currentRemainTime = 0.0f;
        }

        var parameterValues = CubismCore.GetParameterValues(model.Model);
        var parameterMaximumValues = CubismCore.GetParameterMaximumValues(model.Model);
        var parameterMinimumValues = CubismCore.GetParameterMinimumValues(model.Model);
        var parameterDefaultValues = CubismCore.GetParameterDefaultValues(model.Model);

        if (_parameterCaches.Length < model.GetParameterCount())
        {
            _parameterCaches = new float[model.GetParameterCount()];
        }

        if (_parameterInputCaches.Length < model.GetParameterCount())
        {
            _parameterInputCaches = new float[model.GetParameterCount()];
            for (var j = 0; j < model.GetParameterCount(); ++j)
            {
                _parameterInputCaches[j] = parameterValues[j];
            }
        }

        if (_physicsRig.Fps > 0.0f)
        {
            physicsDeltaTime = 1.0f / _physicsRig.Fps;
        }
        else
        {
            physicsDeltaTime = deltaTimeSeconds;
        }

        CubismPhysicsSubRig currentSetting;
        CubismPhysicsOutput currentOutputs;

        while (_currentRemainTime >= physicsDeltaTime)
        {
            // copyRigOutputs _currentRigOutputs to _previousRigOutputs
            for (settingIndex = 0; settingIndex < _physicsRig.SubRigCount; ++settingIndex)
            {
                currentSetting = _physicsRig.Settings[settingIndex];
                if (currentSetting.BaseOutputIndex >= _physicsRig.Outputs.Length)
                {
                    continue;
                }

                currentOutputs = _physicsRig.Outputs[currentSetting.BaseOutputIndex];
                for (i = 0; i < currentSetting.OutputCount; ++i)
                {
                    _previousRigOutputs[settingIndex][i] = _currentRigOutputs[settingIndex][i];
                }
            }

            // 入力キャッシュとパラメータで線形補間してUpdateParticlesするタイミングでの入力を計算する。
            // Calculate the input at the timing to UpdateParticles by linear interpolation with the _parameterInputCaches and parameterValues.
            // _parameterCachesはグループ間での値の伝搬の役割があるので_parameterInputCachesとの分離が必要。
            // _parameterCaches needs to be separated from _parameterInputCaches because of its role in propagating values between groups.
            var inputWeight = physicsDeltaTime / _currentRemainTime;
            for (var j = 0; j < model.GetParameterCount(); ++j)
            {
                _parameterCaches[j] =
                    _parameterInputCaches[j] * (1.0f - inputWeight)
                    + parameterValues[j] * inputWeight;
                _parameterInputCaches[j] = _parameterCaches[j];
            }

            for (settingIndex = 0; settingIndex < _physicsRig.SubRigCount; ++settingIndex)
            {
                totalAngle = 0.0f;
                totalTranslation.X = 0.0f;
                totalTranslation.Y = 0.0f;
                currentSetting = _physicsRig.Settings[settingIndex];
                var currentInputIndex = currentSetting.BaseInputIndex;
                var currentOutputIndex = currentSetting.BaseOutputIndex;
                var currentParticleIndex = currentSetting.BaseParticleIndex;

                // Load input parameters.
                for (i = 0; i < currentSetting.InputCount; ++i)
                {
                    weight = _physicsRig.Inputs[i + currentInputIndex].Weight / MaximumWeight;

                    if (_physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex == -1)
                    {
                        _physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex =
                            model.GetParameterIndex(
                                _physicsRig.Inputs[i + currentInputIndex].Source.Id
                            );
                    }

                    _physicsRig
                        .Inputs[i + currentInputIndex]
                        .GetNormalizedParameterValue(
                            ref totalTranslation,
                            ref totalAngle,
                            _parameterCaches[
                                _physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex
                            ],
                            parameterMinimumValues[
                                _physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex
                            ],
                            parameterMaximumValues[
                                _physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex
                            ],
                            parameterDefaultValues[
                                _physicsRig.Inputs[i + currentInputIndex].SourceParameterIndex
                            ],
                            currentSetting.NormalizationPosition,
                            currentSetting.NormalizationAngle,
                            _physicsRig.Inputs[i + currentInputIndex].Reflect,
                            weight
                        );
                }

                radAngle = CubismMath.DegreesToRadian(-totalAngle);

                totalTranslation.X =
                    totalTranslation.X * MathF.Cos(radAngle)
                    - totalTranslation.Y * MathF.Sin(radAngle);
                totalTranslation.Y =
                    totalTranslation.X * MathF.Sin(radAngle)
                    + totalTranslation.Y * MathF.Cos(radAngle);

                // Calculate particles position.
                UpdateParticles(
                    _physicsRig.Particles,
                    currentParticleIndex,
                    currentSetting.ParticleCount,
                    totalTranslation,
                    totalAngle,
                    Wind,
                    MovementThreshold * currentSetting.NormalizationPosition.Maximum,
                    physicsDeltaTime,
                    AirResistance
                );

                // Update output parameters.
                for (i = 0; i < currentSetting.OutputCount; ++i)
                {
                    particleIndex = _physicsRig.Outputs[i + currentOutputIndex].VertexIndex;

                    if (_physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex == -1)
                    {
                        _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex =
                            model.GetParameterIndex(
                                _physicsRig.Outputs[i + currentOutputIndex].Destination.Id
                            );
                    }

                    if (particleIndex < 1 || particleIndex >= currentSetting.ParticleCount)
                    {
                        continue;
                    }

                    Vector2 translation;
                    translation.X =
                        _physicsRig.Particles[particleIndex + currentParticleIndex].Position.X
                        - _physicsRig
                            .Particles[particleIndex - 1 + currentParticleIndex]
                            .Position
                            .X;
                    translation.Y =
                        _physicsRig.Particles[particleIndex + currentParticleIndex].Position.Y
                        - _physicsRig
                            .Particles[particleIndex - 1 + currentParticleIndex]
                            .Position
                            .Y;

                    outputValue = _physicsRig
                        .Outputs[i + currentOutputIndex]
                        .GetValue(
                            translation,
                            _physicsRig.Particles,
                            currentParticleIndex,
                            particleIndex,
                            _physicsRig.Outputs[i + currentOutputIndex].Reflect,
                            Gravity
                        );

                    _currentRigOutputs[settingIndex][i] = outputValue;

                    UpdateOutputParameterValue(
                        ref _parameterCaches[
                            _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex
                        ],
                        parameterMinimumValues[
                            _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex
                        ],
                        parameterMaximumValues[
                            _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex
                        ],
                        outputValue,
                        _physicsRig.Outputs[i + currentOutputIndex]
                    );
                }
            }

            _currentRemainTime -= physicsDeltaTime;
        }

        var alpha = _currentRemainTime / physicsDeltaTime;
        Interpolate(model, alpha);
    }

    /// <summary>
    ///     オプションを設定する。
    /// </summary>
    public void SetOptions(Vector2 gravity, Vector2 wind)
    {
        Gravity = gravity;
        Wind = wind;
    }

    /// <summary>
    ///     オプションを取得する。
    /// </summary>
    /// <returns>オプション</returns>
    public (Vector2 Gravity, Vector2 Wind) GetOptions()
    {
        return (Gravity, Wind);
    }

    /// <summary>
    ///     初期化する。
    /// </summary>
    private void Initialize()
    {
        CubismPhysicsSubRig currentSetting;
        int i,
            settingIndex;

        Vector2 radius;

        for (settingIndex = 0; settingIndex < _physicsRig.SubRigCount; ++settingIndex)
        {
            currentSetting = _physicsRig.Settings[settingIndex];
            var index = currentSetting.BaseParticleIndex;

            // Initialize the top of particle.
            _physicsRig.Particles[index].InitialPosition = new Vector2(0.0f, 0.0f);
            _physicsRig.Particles[index].LastPosition = _physicsRig
                .Particles[index]
                .InitialPosition;
            _physicsRig.Particles[index].LastGravity = new Vector2(0.0f, -1.0f);
            _physicsRig.Particles[index].LastGravity.Y *= -1.0f;
            _physicsRig.Particles[index].Velocity = new Vector2(0.0f, 0.0f);
            _physicsRig.Particles[index].Force = new Vector2(0.0f, 0.0f);

            // Initialize particles.
            for (i = 1; i < currentSetting.ParticleCount; ++i)
            {
                radius = new Vector2(0.0f, _physicsRig.Particles[i + index].Radius);
                _physicsRig.Particles[i + index].InitialPosition =
                    _physicsRig.Particles[i - 1 + index].InitialPosition + radius;
                _physicsRig.Particles[i + index].Position = _physicsRig
                    .Particles[i + index]
                    .InitialPosition;
                _physicsRig.Particles[i + index].LastPosition = _physicsRig
                    .Particles[i + index]
                    .InitialPosition;
                _physicsRig.Particles[i + index].LastGravity = new Vector2(0.0f, -1.0f);
                _physicsRig.Particles[i + index].LastGravity.Y *= -1.0f;
                _physicsRig.Particles[i + index].Velocity = new Vector2(0.0f, 0.0f);
                _physicsRig.Particles[i + index].Force = new Vector2(0.0f, 0.0f);
            }
        }
    }

    /// <summary>
    ///     振り子演算の最新の結果と一つ前の結果から指定した重みで適用する。
    /// </summary>
    /// <param name="model">物理演算の結果を適用するモデル</param>
    /// <param name="weight">最新結果の重み</param>
    private unsafe void Interpolate(CubismModel model, float weight)
    {
        int i,
            settingIndex;

        float* parameterValues;
        float* parameterMaximumValues;
        float* parameterMinimumValues;

        int currentOutputIndex;
        CubismPhysicsSubRig currentSetting;

        parameterValues = CubismCore.GetParameterValues(model.Model);
        parameterMaximumValues = CubismCore.GetParameterMaximumValues(model.Model);
        parameterMinimumValues = CubismCore.GetParameterMinimumValues(model.Model);

        for (settingIndex = 0; settingIndex < _physicsRig.SubRigCount; ++settingIndex)
        {
            currentSetting = _physicsRig.Settings[settingIndex];
            currentOutputIndex = currentSetting.BaseOutputIndex;

            // Load input parameters.
            for (i = 0; i < currentSetting.OutputCount; ++i)
            {
                if (_physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex == -1)
                {
                    continue;
                }

                var value =
                    _previousRigOutputs[settingIndex][i] * (1 - weight)
                    + _currentRigOutputs[settingIndex][i] * weight;

                UpdateOutputParameterValue(
                    ref parameterValues[
                        _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex
                    ],
                    parameterMinimumValues[
                        _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex
                    ],
                    parameterMaximumValues[
                        _physicsRig.Outputs[i + currentOutputIndex].DestinationParameterIndex
                    ],
                    value,
                    _physicsRig.Outputs[i + currentOutputIndex]
                );
            }
        }
    }

    private float GetRangeValue(float min, float max)
    {
        var maxValue = CubismMath.Max(min, max);
        var minValue = CubismMath.Min(min, max);

        return MathF.Abs(maxValue - minValue);
    }

    /// Gets sign.
    ///
    /// @param  value  Evaluation target value.
    ///
    /// @return  Sign of value.
    private static int Sign(float value)
    {
        var ret = 0;

        if (value > 0.0f)
        {
            ret = 1;
        }
        else if (value < 0.0f)
        {
            ret = -1;
        }

        return ret;
    }

    private float GetDefaultValue(float min, float max)
    {
        var minValue = CubismMath.Min(min, max);

        return minValue + GetRangeValue(min, max) / 2.0f;
    }

    private float NormalizeParameterValue(
        float value,
        float parameterMinimum,
        float parameterMaximum,
        float parameterDefault,
        float normalizedMinimum,
        float normalizedMaximum,
        float normalizedDefault,
        bool isInverted
    )
    {
        var result = 0.0f;

        var maxValue = CubismMath.Max(parameterMaximum, parameterMinimum);

        if (maxValue < value)
        {
            value = maxValue;
        }

        var minValue = CubismMath.Min(parameterMaximum, parameterMinimum);

        if (minValue > value)
        {
            value = minValue;
        }

        var minNormValue = CubismMath.Min(normalizedMinimum, normalizedMaximum);
        var maxNormValue = CubismMath.Max(normalizedMinimum, normalizedMaximum);
        var middleNormValue = normalizedDefault;

        var middleValue = GetDefaultValue(minValue, maxValue);
        var paramValue = value - middleValue;

        switch (Sign(paramValue))
        {
            case 1:
            {
                var nLength = maxNormValue - middleNormValue;
                var pLength = maxValue - middleValue;
                if (pLength != 0.0f)
                {
                    result = paramValue * (nLength / pLength);
                    result += middleNormValue;
                }

                break;
            }
            case -1:
            {
                var nLength = minNormValue - middleNormValue;
                var pLength = minValue - middleValue;
                if (pLength != 0.0f)
                {
                    result = paramValue * (nLength / pLength);
                    result += middleNormValue;
                }

                break;
            }
            case 0:
            {
                result = middleNormValue;

                break;
            }
        }

        return isInverted ? result : result * -1.0f;
    }

    private void GetInputTranslationXFromNormalizedParameterValue(
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
    )
    {
        targetTranslation.X +=
            NormalizeParameterValue(
                value,
                parameterMinimumValue,
                parameterMaximumValue,
                parameterDefaultValue,
                normalizationPosition.Minimum,
                normalizationPosition.Maximum,
                normalizationPosition.Default,
                isInverted
            ) * weight;
    }

    private void GetInputTranslationYFromNormalizedParameterValue(
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
    )
    {
        targetTranslation.Y +=
            NormalizeParameterValue(
                value,
                parameterMinimumValue,
                parameterMaximumValue,
                parameterDefaultValue,
                normalizationPosition.Minimum,
                normalizationPosition.Maximum,
                normalizationPosition.Default,
                isInverted
            ) * weight;
    }

    private void GetInputAngleFromNormalizedParameterValue(
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
    )
    {
        targetAngle +=
            NormalizeParameterValue(
                value,
                parameterMinimumValue,
                parameterMaximumValue,
                parameterDefaultValue,
                normalizationAngle.Minimum,
                normalizationAngle.Maximum,
                normalizationAngle.Default,
                isInverted
            ) * weight;
    }

    private float GetOutputTranslationX(
        Vector2 translation,
        CubismPhysicsParticle[] particles,
        int start,
        int particleIndex,
        bool isInverted,
        Vector2 parentGravity
    )
    {
        var outputValue = translation.X;

        if (isInverted)
        {
            outputValue *= -1.0f;
        }

        return outputValue;
    }

    private float GetOutputTranslationY(
        Vector2 translation,
        CubismPhysicsParticle[] particles,
        int start,
        int particleIndex,
        bool isInverted,
        Vector2 parentGravity
    )
    {
        var outputValue = translation.Y;

        if (isInverted)
        {
            outputValue *= -1.0f;
        }

        return outputValue;
    }

    private float GetOutputAngle(
        Vector2 translation,
        CubismPhysicsParticle[] particles,
        int start,
        int particleIndex,
        bool isInverted,
        Vector2 parentGravity
    )
    {
        float outputValue;

        if (particleIndex >= 2)
        {
            parentGravity =
                particles[particleIndex - 1 + start].Position
                - particles[particleIndex - 2 + start].Position;
        }
        else
        {
            parentGravity *= -1.0f;
        }

        outputValue = CubismMath.DirectionToRadian(parentGravity, translation);

        if (isInverted)
        {
            outputValue *= -1.0f;
        }

        return outputValue;
    }

    private float GetOutputScaleTranslationX(Vector2 translationScale, float angleScale)
    {
        return translationScale.X;
    }

    private float GetOutputScaleTranslationY(Vector2 translationScale, float angleScale)
    {
        return translationScale.Y;
    }

    private float GetOutputScaleAngle(Vector2 translationScale, float angleScale)
    {
        return angleScale;
    }

    /// Updates particles.
    ///
    /// @param  strand            Target array of particle.
    /// @param  strandCount       Count of particle.
    /// @param  totalTranslation  Total translation value.
    /// @param  totalAngle        Total angle.
    /// @param  windDirection              Direction of wind.
    /// @param  thresholdValue    Threshold of movement.
    /// @param  deltaTimeSeconds  Delta time.
    /// @param  airResistance     Air resistance.
    private static void UpdateParticles(
        CubismPhysicsParticle[] strand,
        int start,
        int strandCount,
        Vector2 totalTranslation,
        float totalAngle,
        Vector2 windDirection,
        float thresholdValue,
        float deltaTimeSeconds,
        float airResistance
    )
    {
        int i;
        float totalRadian;
        float delay;
        float radian;
        Vector2 currentGravity;
        Vector2 direction;
        Vector2 velocity;
        Vector2 force;
        Vector2 newDirection;

        strand[start].Position = totalTranslation;

        totalRadian = CubismMath.DegreesToRadian(totalAngle);
        currentGravity = CubismMath.RadianToDirection(totalRadian);
        currentGravity = Vector2.Normalize(currentGravity);

        for (i = 1; i < strandCount; ++i)
        {
            strand[i + start].Force =
                currentGravity * strand[i + start].Acceleration + windDirection;

            strand[i + start].LastPosition = strand[i + start].Position;

            delay = strand[i + start].Delay * deltaTimeSeconds * 30.0f;

            direction.X = strand[i + start].Position.X - strand[i - 1 + start].Position.X;
            direction.Y = strand[i + start].Position.Y - strand[i - 1 + start].Position.Y;

            radian =
                CubismMath.DirectionToRadian(strand[i + start].LastGravity, currentGravity)
                / airResistance;

            direction.X = MathF.Cos(radian) * direction.X - direction.Y * MathF.Sin(radian);
            direction.Y = MathF.Sin(radian) * direction.X + direction.Y * MathF.Cos(radian);

            strand[i + start].Position = strand[i - 1 + start].Position + direction;

            velocity.X = strand[i + start].Velocity.X * delay;
            velocity.Y = strand[i + start].Velocity.Y * delay;
            force = strand[i + start].Force * delay * delay;

            strand[i + start].Position = strand[i + start].Position + velocity + force;

            newDirection = strand[i + start].Position - strand[i - 1 + start].Position;

            newDirection = Vector2.Normalize(newDirection);

            strand[i + start].Position =
                strand[i - 1 + start].Position + newDirection * strand[i + start].Radius;

            if (MathF.Abs(strand[i + start].Position.X) < thresholdValue)
            {
                strand[i + start].Position.X = 0.0f;
            }

            if (delay != 0.0f)
            {
                strand[i + start].Velocity.X =
                    strand[i + start].Position.X - strand[i + start].LastPosition.X;
                strand[i + start].Velocity.Y =
                    strand[i + start].Position.Y - strand[i + start].LastPosition.Y;
                strand[i + start].Velocity /= delay;
                strand[i + start].Velocity *= strand[i + start].Mobility;
            }

            strand[i + start].Force = new Vector2(0.0f, 0.0f);
            strand[i + start].LastGravity = currentGravity;
        }
    }

    /**
     * Updates particles for stabilization.
     *
     * @param strand                Target array of particle.
     * @param strandCount           Count of particle.
     * @param totalTranslation      Total translation value.
     * @param totalAngle            Total angle.
     * @param windDirection         Direction of Wind.
     * @param thresholdValue        Threshold of movement.
     */
    private static void UpdateParticlesForStabilization(
        CubismPhysicsParticle[] strand,
        int start,
        int strandCount,
        Vector2 totalTranslation,
        float totalAngle,
        Vector2 windDirection,
        float thresholdValue
    )
    {
        int i;
        float totalRadian;
        Vector2 currentGravity;
        Vector2 force;

        strand[start].Position = totalTranslation;

        totalRadian = CubismMath.DegreesToRadian(totalAngle);
        currentGravity = CubismMath.RadianToDirection(totalRadian);
        currentGravity = Vector2.Normalize(currentGravity);

        for (i = 1; i < strandCount; ++i)
        {
            strand[i + start].Force =
                currentGravity * strand[i + start].Acceleration + windDirection;

            strand[i + start].LastPosition = strand[i + start].Position;

            strand[i + start].Velocity = new Vector2(0.0f, 0.0f);

            force = strand[i + start].Force;
            force = Vector2.Normalize(force);

            force *= strand[i + start].Radius;
            strand[i + start].Position = strand[i - 1].Position + force;

            if (MathF.Abs(strand[i + start].Position.X) < thresholdValue)
            {
                strand[i + start].Position.X = 0.0f;
            }

            strand[i + start].Force = new Vector2(0.0f, 0.0f);
            strand[i + start].LastGravity = currentGravity;
        }
    }

    /// Updates output parameter value.
    ///
    /// @param  parameterValue         Target parameter value.
    /// @param  parameterValueMinimum  Minimum of parameter value.
    /// @param  parameterValueMaximum  Maximum of parameter value.
    /// @param  translation            Translation value.
    private static void UpdateOutputParameterValue(
        ref float parameterValue,
        float parameterValueMinimum,
        float parameterValueMaximum,
        float translation,
        CubismPhysicsOutput output
    )
    {
        float outputScale;
        float value;
        float weight;

        outputScale = output.GetScale(output.TranslationScale, output.AngleScale);

        value = translation * outputScale;

        if (value < parameterValueMinimum)
        {
            if (value < output.ValueBelowMinimum)
            {
                output.ValueBelowMinimum = value;
            }

            value = parameterValueMinimum;
        }
        else if (value > parameterValueMaximum)
        {
            if (value > output.ValueExceededMaximum)
            {
                output.ValueExceededMaximum = value;
            }

            value = parameterValueMaximum;
        }

        weight = output.Weight / MaximumWeight;

        if (weight >= 1.0f)
        {
            parameterValue = value;
        }
        else
        {
            value = parameterValue * (1.0f - weight) + value * weight;
            parameterValue = value;
        }
    }
}
