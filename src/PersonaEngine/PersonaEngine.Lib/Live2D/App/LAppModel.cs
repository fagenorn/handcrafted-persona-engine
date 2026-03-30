using System.Text.Json;
using PersonaEngine.Lib.Live2D.Framework;
using PersonaEngine.Lib.Live2D.Framework.Effect;
using PersonaEngine.Lib.Live2D.Framework.Math;
using PersonaEngine.Lib.Live2D.Framework.Model;
using PersonaEngine.Lib.Live2D.Framework.Motion;
using PersonaEngine.Lib.Live2D.Framework.Rendering.OpenGL;

namespace PersonaEngine.Lib.Live2D.App;

public class LAppModel : CubismUserModel
{
    /// <summary>
    ///     読み込まれている表情のリスト
    /// </summary>
    private readonly Dictionary<string, ACubismMotion> _expressions = [];

    /// <summary>
    ///     モデルに設定されたまばたき機能用パラメータID
    /// </summary>
    private readonly List<string> _eyeBlinkIds = [];

    private readonly LAppDelegate _lapp;

    /// <summary>
    ///     モデルに設定されたリップシンク機能用パラメータID
    /// </summary>
    private readonly List<string> _lipSyncIds = [];

    /// <summary>
    ///     モデルセッティングが置かれたディレクトリ
    /// </summary>
    private readonly string _modelHomeDir;

    /// <summary>
    ///     モデルセッティング情報
    /// </summary>
    private readonly ModelSettingObj _modelSetting;

    /// <summary>
    ///     読み込まれているモーションのリスト
    /// </summary>
    private readonly Dictionary<string, ACubismMotion> _motions = [];

    private readonly Random _random = new();

    /// <summary>
    ///     wavファイルハンドラ
    /// </summary>
    public LAppWavFileHandler _wavFileHandler = new();

    public static string GetMotionGroupName(string motionName)
    {
        var underscoreIndex = motionName.LastIndexOf('_');
        if (underscoreIndex > 0 && int.TryParse(motionName.AsSpan(underscoreIndex + 1), out _))
        {
            return motionName[..underscoreIndex];
        }

        return motionName; // Treat whole name as group if no "_Number" suffix
    }

    public Action<LAppModel>? ValueUpdate;

    public LAppModel(LAppDelegate lapp, string dir, string fileName)
    {
        _lapp = lapp;

        if (LAppDefine.MocConsistencyValidationEnable)
        {
            _mocConsistency = true;
        }

        IdParamAngleX = CubismFramework.CubismIdManager.GetId(CubismDefaultParameterId.ParamAngleX);

        IdParamAngleY = CubismFramework.CubismIdManager.GetId(CubismDefaultParameterId.ParamAngleY);

        IdParamAngleZ = CubismFramework.CubismIdManager.GetId(CubismDefaultParameterId.ParamAngleZ);
        IdParamBodyAngleX = CubismFramework.CubismIdManager.GetId(
            CubismDefaultParameterId.ParamBodyAngleX
        );

        IdParamEyeBallX = CubismFramework.CubismIdManager.GetId(
            CubismDefaultParameterId.ParamEyeBallX
        );

        IdParamEyeBallY = CubismFramework.CubismIdManager.GetId(
            CubismDefaultParameterId.ParamEyeBallY
        );

        _modelHomeDir = dir;

        CubismLog.Debug($"[Live2D]load model setting: {fileName}");

        using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        _modelSetting =
            JsonSerializer.Deserialize(stream, ModelSettingObjContext.Default.ModelSettingObj)
            ?? throw new Exception("model3.json error");

        Updating = true;
        Initialized = false;

        //Cubism Model
        var path = _modelSetting.FileReferences?.Moc;
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = Path.GetFullPath(_modelHomeDir + path);
            if (!File.Exists(path))
            {
                throw new Exception("model is null");
            }

            CubismLog.Debug($"[Live2D]create model: {path}");

            LoadModel(File.ReadAllBytes(path), _mocConsistency);
        }

        //Expression
        if (_modelSetting.FileReferences?.Expressions?.Count > 0)
        {
            for (var i = 0; i < _modelSetting.FileReferences.Expressions.Count; i++)
            {
                var item = _modelSetting.FileReferences.Expressions[i];
                var name = item.Name;
                path = item.File;
                path = Path.GetFullPath(_modelHomeDir + path);
                if (!File.Exists(path))
                {
                    continue;
                }

                var motion = new CubismExpressionMotion(path);

                if (_expressions.ContainsKey(name))
                {
                    _expressions[name] = motion;
                }
                else
                {
                    _expressions.Add(name, motion);
                }
            }
        }

        //Physics
        path = _modelSetting.FileReferences?.Physics;
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = Path.GetFullPath(_modelHomeDir + path);
            if (File.Exists(path))
            {
                LoadPhysics(path);
            }
        }

        //Pose
        path = _modelSetting.FileReferences?.Pose;
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = Path.GetFullPath(_modelHomeDir + path);
            if (File.Exists(path))
            {
                LoadPose(path);
            }
        }

        //EyeBlink
        if (_modelSetting.IsExistEyeBlinkParameters())
        {
            _eyeBlink = new CubismEyeBlink(_modelSetting);
        }

        LoadBreath();

        //UserData
        path = _modelSetting.FileReferences?.UserData;
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = Path.GetFullPath(_modelHomeDir + path);
            if (File.Exists(path))
            {
                LoadUserData(path);
            }
        }

        // EyeBlinkIds
        if (_eyeBlink != null)
        {
            _eyeBlinkIds.AddRange(_eyeBlink.ParameterIds);
        }

        // LipSyncIds
        if (_modelSetting.IsExistLipSyncParameters())
        {
            foreach (var item in _modelSetting.Groups)
            {
                if (item.Name == CubismModelSettingJson.LipSync)
                {
                    _lipSyncIds.AddRange(item.Ids);
                }
            }
        }

        //Layout
        Dictionary<string, float> layout = [];
        _modelSetting.GetLayoutMap(layout);
        ModelMatrix.SetupFromLayout(layout);

        Model.SaveParameters();

        if (_modelSetting.FileReferences?.Motions?.Count > 0)
        {
            foreach (var item in _modelSetting.FileReferences.Motions)
            {
                PreloadMotionGroup(item.Key);
            }
        }

        _motionManager.StopAllMotions();

        Updating = false;
        Initialized = true;

        CreateRenderer(new CubismRenderer_OpenGLES2(_lapp.GL, Model));

        SetupTextures();
    }

    public List<string> Motions => new(_motions.Keys);

    public List<string> Expressions => new(_expressions.Keys);

    public List<(string, int, float)> Parts
    {
        get
        {
            var list = new List<(string, int, float)>();
            var count = Model.GetPartCount();
            for (var a = 0; a < count; a++)
            {
                list.Add((Model.GetPartId(a), a, Model.GetPartOpacity(a)));
            }

            return list;
        }
    }

    public List<string> Parameters => new(Model.ParameterIds);

    /// <summary>
    ///     デルタ時間の積算値[秒]
    /// </summary>
    public float UserTimeSeconds { get; set; }

    public bool RandomMotion { get; set; } = true;

    public bool CustomValueUpdate { get; set; }

    /// <summary>
    ///     パラメータID: ParamAngleX
    /// </summary>
    public string IdParamAngleX { get; set; }

    /// <summary>
    ///     パラメータID: ParamAngleY
    /// </summary>
    public string IdParamAngleY { get; set; }

    /// <summary>
    ///     パラメータID: ParamAngleZ
    /// </summary>
    public string IdParamAngleZ { get; set; }

    /// <summary>
    ///     パラメータID: ParamBodyAngleX
    /// </summary>
    public string IdParamBodyAngleX { get; set; }

    /// <summary>
    ///     パラメータID: ParamEyeBallX
    /// </summary>
    public string IdParamEyeBallX { get; set; }

    /// <summary>
    ///     パラメータID: ParamEyeBallXY
    /// </summary>
    public string IdParamEyeBallY { get; set; }

    public string IdParamBreath { get; set; } =
        CubismFramework.CubismIdManager.GetId(CubismDefaultParameterId.ParamBreath);

    public event Action<LAppModel, string>? Motion;

    public new void Dispose()
    {
        base.Dispose();

        _motions.Clear();
        _expressions.Clear();

        if (_modelSetting.FileReferences?.Motions.Count > 0)
        {
            foreach (var item in _modelSetting.FileReferences.Motions)
            {
                ReleaseMotionGroup(item.Key);
            }
        }
    }

    public void LoadBreath()
    {
        //Breath
        _breath = new CubismBreath
        {
            Parameters =
            [
                new BreathParameterData
                {
                    ParameterId = IdParamAngleX,
                    Offset = 0.0f,
                    Peak = 15.0f,
                    Cycle = 6.5345f,
                    Weight = 0.5f,
                },
                new BreathParameterData
                {
                    ParameterId = IdParamAngleY,
                    Offset = 0.0f,
                    Peak = 8.0f,
                    Cycle = 3.5345f,
                    Weight = 0.5f,
                },
                new BreathParameterData
                {
                    ParameterId = IdParamAngleZ,
                    Offset = 0.0f,
                    Peak = 10.0f,
                    Cycle = 5.5345f,
                    Weight = 0.5f,
                },
                new BreathParameterData
                {
                    ParameterId = IdParamBodyAngleX,
                    Offset = 0.0f,
                    Peak = 4.0f,
                    Cycle = 15.5345f,
                    Weight = 0.5f,
                },
                new BreathParameterData
                {
                    ParameterId = IdParamBreath,
                    Offset = 0.5f,
                    Peak = 0.5f,
                    Cycle = 3.2345f,
                    Weight = 0.5f,
                },
            ],
        };
    }

    /// <summary>
    ///     レンダラを再構築する
    /// </summary>
    public void ReloadRenderer()
    {
        DeleteRenderer();

        CreateRenderer(new CubismRenderer_OpenGLES2(_lapp.GL, Model));

        SetupTextures();
    }

    /// <summary>
    ///     モデルの更新処理。モデルのパラメータから描画状態を決定する。
    /// </summary>
    public void Update()
    {
        var deltaTimeSeconds = LAppPal.DeltaTime;
        UserTimeSeconds += deltaTimeSeconds;

        _dragManager.Update(deltaTimeSeconds);
        _dragX = _dragManager.FaceX;
        _dragY = _dragManager.FaceY;

        // モーションによるパラメータ更新の有無
        var motionUpdated = false;

        //-----------------------------------------------------------------
        Model.LoadParameters(); // 前回セーブされた状態をロード
        if (_motionManager.IsFinished() && RandomMotion)
        {
            // モーションの再生がない場合、待機モーションの中からランダムで再生する
            StartRandomMotion(LAppDefine.MotionGroupIdle, MotionPriority.PriorityIdle);
        }
        else
        {
            motionUpdated = _motionManager.UpdateMotion(Model, deltaTimeSeconds); // モーションを更新
        }

        Model.SaveParameters(); // 状態を保存

        //-----------------------------------------------------------------

        // 不透明度
        Opacity = Model.GetModelOpacity();

        // まばたき
        if (!motionUpdated)
        {
            // メインモーションの更新がないとき
            // _eyeBlink?.UpdateParameters(Model, deltaTimeSeconds); // 目パチ
        }

        _expressionManager?.UpdateMotion(Model, deltaTimeSeconds); // 表情でパラメータ更新（相対変化）

        if (CustomValueUpdate)
        {
            ValueUpdate?.Invoke(this);
        }
        else
        {
            //ドラッグによる変化
            //ドラッグによる顔の向きの調整
            Model.AddParameterValue(IdParamAngleX, _dragX * 30); // -30から30の値を加える
            Model.AddParameterValue(IdParamAngleY, _dragY * 30);
            Model.AddParameterValue(IdParamAngleZ, _dragX * _dragY * -30);

            //ドラッグによる体の向きの調整
            Model.AddParameterValue(IdParamBodyAngleX, _dragX * 10); // -10から10の値を加える

            //ドラッグによる目の向きの調整
            Model.AddParameterValue(IdParamEyeBallX, _dragX); // -1から1の値を加える
            Model.AddParameterValue(IdParamEyeBallY, _dragY);
        }

        // 呼吸など
        // _breath?.UpdateParameters(Model, deltaTimeSeconds);

        // 物理演算の設定
        _physics?.Evaluate(Model, deltaTimeSeconds);

        // リップシンクの設定
        // if ( _lipSync )
        // {
        //     // リアルタイムでリップシンクを行う場合、システムから音量を取得して0〜1の範囲で値を入力します。
        //     var value = 0.0f;
        //
        //     // 状態更新/RMS値取得
        //     _wavFileHandler.Update(deltaTimeSeconds);
        //     value = (float)_wavFileHandler.GetRms();
        //
        //     var weight = 3f; // Configure it as needed.
        //
        //     for ( var i = 0; i < _lipSyncIds.Count; ++i )
        //     {
        //         Model.SetParameterValue(_lipSyncIds[i], value * weight);
        //     }
        // }

        // ポーズの設定
        _pose?.UpdateParameters(Model, deltaTimeSeconds);

        Model.Update();
    }

    /// <summary>
    ///     モデルを描画する処理。モデルを描画する空間のView-Projection行列を渡す。
    /// </summary>
    /// <param name="matrix">View-Projection行列</param>
    public void Draw(CubismMatrix44 matrix)
    {
        if (Model == null)
        {
            return;
        }

        matrix.MultiplyByMatrix(ModelMatrix);
        if (Renderer is CubismRenderer_OpenGLES2 ren)
        {
            ren.ClearColor = _lapp.BGColor;
            ren.SetMvpMatrix(matrix);
        }

        DoDraw();
    }

    /// <summary>
    ///     Starts playing the motion specified by the argument.
    /// </summary>
    /// <param name="group">Motion Group Name</param>
    /// <param name="no">Number in group</param>
    /// <param name="priority">Priority</param>
    /// <param name="onFinishedMotionHandler">The callback function that is called when the motion playback ends. If NULL, it will not be called.</param>
    /// <returns>Returns the identification number of the started motion. Used as an argument for IsFinished() to determine whether an individual motion has finished. Returns "-1" if the motion cannot be started.</returns>
    public CubismMotionQueueEntry? StartMotion(
        string name,
        MotionPriority priority,
        FinishedMotionCallback? onFinishedMotionHandler = null
    )
    {
        var temp = name.Split("_");
        if (temp.Length != 2)
        {
            throw new Exception("motion name error");
        }

        return StartMotion(temp[0], int.Parse(temp[1]), priority, onFinishedMotionHandler);
    }

    public CubismMotionQueueEntry? StartMotion(
        string group,
        int no,
        MotionPriority priority,
        FinishedMotionCallback? onFinishedMotionHandler = null
    )
    {
        if (priority == MotionPriority.PriorityForce)
        {
            _motionManager.ReservePriority = priority;
        }
        else if (!_motionManager.ReserveMotion(priority))
        {
            CubismLog.Debug("[Live2D]can't start motion.");

            return null;
        }

        var item = _modelSetting.FileReferences.Motions[group][no];

        //ex) idle_0
        var name = $"{group}_{no}";

        CubismMotion motion;
        if (!_motions.TryGetValue(name, out var value))
        {
            var path = item.File;
            path = Path.GetFullPath(_modelHomeDir + path);
            if (!File.Exists(path))
            {
                return null;
            }

            motion = new CubismMotion(path, onFinishedMotionHandler);
            var fadeTime = item.FadeInTime;
            if (fadeTime >= 0.0f)
            {
                motion.FadeInSeconds = fadeTime;
            }

            fadeTime = item.FadeOutTime;
            if (fadeTime >= 0.0f)
            {
                motion.FadeOutSeconds = fadeTime;
            }

            motion.SetEffectIds(_eyeBlinkIds, _lipSyncIds);
        }
        else
        {
            motion = (value as CubismMotion)!;
            motion.OnFinishedMotion = onFinishedMotionHandler;
        }

        //voice
        var voice = item.Sound;
        if (!string.IsNullOrWhiteSpace(voice))
        {
            var path = voice;
            path = _modelHomeDir + path;
            _wavFileHandler.Start(path);
        }

        CubismLog.Debug($"[Live2D]start motion: [{group}_{no}]");

        return _motionManager.StartMotionPriority(motion, priority);
    }

    /// <summary>
    ///     Starts playing a randomly selected motion.
    /// </summary>
    /// <param name="group">Motion Group Name</param>
    /// <param name="priority">Priority</param>
    /// <param name="onFinishedMotionHandler">A callback function that is called when the priority motion playback ends. If NULL, it will not be called.</param>
    /// <returns>Returns the identification number of the started motion. Used as an argument for IsFinished() to determine whether an individual motion has finished. Returns "-1" if the motion cannot be started.</returns>
    public CubismMotionQueueEntry? StartRandomMotion(
        string group,
        MotionPriority priority,
        FinishedMotionCallback? onFinishedMotionHandler = null
    )
    {
        if (_modelSetting.FileReferences?.Motions?.ContainsKey(group) == true)
        {
            var no = _random.Next() % _modelSetting.FileReferences.Motions[group].Count;

            return StartMotion(group, no, priority, onFinishedMotionHandler);
        }

        return null;
    }

    public bool IsMotionFinished(CubismMotionQueueEntry entry)
    {
        return _motionManager.IsFinished(entry);
    }

    /// <summary>
    ///     Sets the facial motion specified by the argument.
    /// </summary>
    /// <param name="expressionID">Facial expression motion ID</param>
    public void SetExpression(string expressionID)
    {
        var motion = _expressions[expressionID];
        CubismLog.Debug($"[Live2D]expression: [{expressionID}]");

        if (motion != null)
        {
            _expressionManager.StartMotionPriority(motion, MotionPriority.PriorityForce);
        }
        else
        {
            CubismLog.Debug($"[Live2D]expression[{expressionID}] is null ");
        }
    }

    /// <summary>
    ///     Set a randomly selected facial expression motion
    /// </summary>
    public void SetRandomExpression()
    {
        if (_expressions.Count == 0)
        {
            return;
        }

        var no = _random.Next() % _expressions.Count;
        var i = 0;
        foreach (var item in _expressions)
        {
            if (i == no)
            {
                SetExpression(item.Key);

                return;
            }

            i++;
        }
    }

    /// <summary>
    ///     イベントの発火を受け取る
    /// </summary>
    /// <param name="eventValue"></param>
    protected override void MotionEventFired(string eventValue)
    {
        CubismLog.Debug($"[Live2D]{eventValue} is fired on LAppModel!!");
        Motion?.Invoke(this, eventValue);
    }

    /// <summary>
    ///     当たり判定テスト。
    ///     指定IDの頂点リストから矩形を計算し、座標が矩形範囲内か判定する。
    /// </summary>
    /// <param name="hitAreaName">当たり判定をテストする対象のID</param>
    /// <param name="x">判定を行うX座標</param>
    /// <param name="y">判定を行うY座標</param>
    /// <returns></returns>
    public bool HitTest(string hitAreaName, float x, float y)
    {
        // 透明時は当たり判定なし。
        if (Opacity < 1)
        {
            return false;
        }

        if (_modelSetting.HitAreas?.Count > 0)
        {
            for (var i = 0; i < _modelSetting.HitAreas?.Count; i++)
            {
                if (_modelSetting.HitAreas[i].Name == hitAreaName)
                {
                    var id = CubismFramework.CubismIdManager.GetId(_modelSetting.HitAreas[i].Id);

                    return IsHit(id, x, y);
                }
            }
        }

        return false; // 存在しない場合はfalse
    }

    /// <summary>
    ///     モデルを描画する処理。モデルを描画する空間のView-Projection行列を渡す。
    /// </summary>
    protected void DoDraw()
    {
        if (Model == null)
        {
            return;
        }

        (Renderer as CubismRenderer_OpenGLES2)?.DrawModel();
    }

    /// <summary>
    ///     モーションデータをグループ名から一括で解放する。
    ///     モーションデータの名前は内部でModelSettingから取得する。
    /// </summary>
    /// <param name="group">モーションデータのグループ名</param>
    private void ReleaseMotionGroup(string group)
    {
        var list = _modelSetting.FileReferences.Motions[group];
        for (var i = 0; i < list.Count; i++)
        {
            var voice = list[i].Sound;
        }
    }

    /// <summary>
    ///     OpenGLのテクスチャユニットにテクスチャをロードする
    /// </summary>
    private void SetupTextures()
    {
        if (_modelSetting.FileReferences?.Textures?.Count > 0)
        {
            for (
                var modelTextureNumber = 0;
                modelTextureNumber < _modelSetting.FileReferences.Textures.Count;
                modelTextureNumber++
            )
            {
                //OpenGLのテクスチャユニットにテクスチャをロードする
                var texturePath = _modelSetting.FileReferences.Textures[modelTextureNumber];
                if (string.IsNullOrWhiteSpace(texturePath))
                {
                    continue;
                }

                texturePath = Path.GetFullPath(_modelHomeDir + texturePath);

                var texture = _lapp.TextureManager.CreateTextureFromPngFile(texturePath);
                var glTextueNumber = texture.ID;

                //OpenGL
                (Renderer as CubismRenderer_OpenGLES2)?.BindTexture(
                    modelTextureNumber,
                    glTextueNumber
                );
            }
        }
    }

    /// <summary>
    ///     モーションデータをグループ名から一括でロードする。
    ///     モーションデータの名前は内部でModelSettingから取得する。
    /// </summary>
    /// <param name="group">モーションデータのグループ名</param>
    private void PreloadMotionGroup(string group)
    {
        // グループに登録されているモーション数を取得
        var list = _modelSetting.FileReferences.Motions[group];

        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            //ex) idle_0
            // モーションのファイル名とパスの取得
            var name = $"{group}_{i}";
            var path = Path.GetFullPath(_modelHomeDir + item.File);

            // モーションデータの読み込み
            var tmpMotion = new CubismMotion(path);

            // フェードインの時間を取得
            var fadeTime = item.FadeInTime;
            if (fadeTime >= 0.0f)
            {
                tmpMotion.FadeInSeconds = fadeTime;
            }

            // フェードアウトの時間を取得
            fadeTime = item.FadeOutTime;
            if (fadeTime >= 0.0f)
            {
                tmpMotion.FadeOutSeconds = fadeTime;
            }

            tmpMotion.SetEffectIds(_eyeBlinkIds, _lipSyncIds);

            if (_motions.ContainsKey(name))
            {
                _motions[name] = tmpMotion;
            }
            else
            {
                _motions.Add(name, tmpMotion);
            }
        }
    }
}
