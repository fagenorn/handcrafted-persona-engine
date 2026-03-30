using PersonaEngine.Lib.Live2D.Framework;
using PersonaEngine.Lib.Live2D.Framework.Math;
using PersonaEngine.Lib.Live2D.Framework.Model;
using PersonaEngine.Lib.Live2D.Framework.Motion;

namespace PersonaEngine.Lib.Live2D.App;

/// <summary>
///     サンプルアプリケーションにおいてCubismModelを管理するクラス
///     モデル生成と破棄、タップイベントの処理、モデル切り替えを行う。
/// </summary>
/// <remarks>
///     コンストラクタ
/// </remarks>
public class LAppLive2DManager(LAppDelegate lapp) : IDisposable
{
    /// <summary>
    ///     モデルインスタンスのコンテナ
    /// </summary>
    private readonly List<LAppModel> _models = [];

    private readonly CubismMatrix44 _projection = new();

    /// <summary>
    ///     モデル描画に用いるView行列
    /// </summary>
    public CubismMatrix44 ViewMatrix { get; } = new();

    public void Dispose()
    {
        ReleaseAllModel();
    }

    public event Action<CubismModel, ACubismMotion>? MotionFinished;

    /// <summary>
    ///     現在のシーンで保持しているモデルを返す
    /// </summary>
    /// <param name="no">モデルリストのインデックス値</param>
    /// <returns>モデルのインスタンスを返す。インデックス値が範囲外の場合はNULLを返す。</returns>
    public LAppModel GetModel(int no)
    {
        return _models[no];
    }

    /// <summary>
    ///     現在のシーンで保持しているすべてのモデルを解放する
    /// </summary>
    public void ReleaseAllModel()
    {
        for (var i = 0; i < _models.Count; i++)
        {
            _models[i].Dispose();
        }

        _models.Clear();
    }

    /// <summary>
    ///     画面をドラッグしたときの処理
    /// </summary>
    /// <param name="x">画面のX座標</param>
    /// <param name="y">画面のY座標</param>
    public void OnDrag(float x, float y)
    {
        for (var i = 0; i < _models.Count; i++)
        {
            var model = GetModel(i);

            model.SetDragging(x, y);
        }
    }

    /// <summary>
    ///     画面をタップしたときの処理
    /// </summary>
    /// <param name="x">画面のX座標</param>
    /// <param name="y">画面のY座標</param>
    public void OnTap(float x, float y)
    {
        CubismLog.Debug($"[Live2D]tap point: x:{x:0.00} y:{y:0.00}");

        for (var i = 0; i < _models.Count; i++)
        {
            if (_models[i].HitTest(LAppDefine.HitAreaNameHead, x, y))
            {
                CubismLog.Debug($"[Live2D]hit area: [{LAppDefine.HitAreaNameHead}]");
                _models[i].SetRandomExpression();
            }
            else if (_models[i].HitTest(LAppDefine.HitAreaNameBody, x, y))
            {
                CubismLog.Debug($"[Live2D]hit area: [{LAppDefine.HitAreaNameBody}]");
                _models[i]
                    .StartRandomMotion(
                        LAppDefine.MotionGroupTapBody,
                        MotionPriority.PriorityNormal,
                        OnFinishedMotion
                    );
            }
        }
    }

    private void OnFinishedMotion(CubismModel model, ACubismMotion self)
    {
        CubismLog.Info($"[Live2D]Motion Finished: {self}");
        MotionFinished?.Invoke(model, self);
    }

    /// <summary>
    ///     画面を更新するときの処理
    ///     モデルの更新処理および描画処理を行う
    /// </summary>
    public void OnUpdate()
    {
        lapp.GL.GetWindowSize(out var width, out var height);

        var modelCount = _models.Count;
        foreach (var model in _models)
        {
            _projection.LoadIdentity();

            if (model.Model.GetCanvasWidth() > 1.0f && width < height)
            {
                // 横に長いモデルを縦長ウィンドウに表示する際モデルの横サイズでscaleを算出する
                model.ModelMatrix.SetWidth(2.0f);
                _projection.Scale(1.0f, (float)width / height);
            }
            else
            {
                _projection.Scale((float)height / width, 1.0f);
            }

            // 必要があればここで乗算
            if (ViewMatrix != null)
            {
                _projection.MultiplyByMatrix(ViewMatrix);
            }

            model.Update();
            model.Draw(_projection); // 参照渡しなのでprojectionは変質する
        }
    }

    public LAppModel LoadModel(string dir, string name)
    {
        CubismLog.Debug($"[Live2D]model load: {name}");

        // ModelDir[]に保持したディレクトリ名から
        // model3.jsonのパスを決定する.
        // ディレクトリ名とmodel3.jsonの名前を一致させておくこと.
        if (!dir.EndsWith('\\') && !dir.EndsWith('/'))
        {
            dir = Path.GetFullPath(dir + '/');
        }

        var modelJsonName = Path.GetFullPath($"{dir}{name}");
        if (!File.Exists(modelJsonName))
        {
            modelJsonName = Path.GetFullPath($"{dir}{name}.model3.json");
        }

        if (!File.Exists(modelJsonName))
        {
            dir = Path.GetFullPath(dir + name + '/');
            modelJsonName = Path.GetFullPath($"{dir}{name}.model3.json");
        }

        if (!File.Exists(modelJsonName))
        {
            throw new Exception($"[Live2D]File not found: {modelJsonName}");
        }

        var model = new LAppModel(lapp, dir, modelJsonName);
        _models.Add(model);

        return model;
    }

    public void RemoveModel(int index)
    {
        if (_models.Count > index)
        {
            var model = _models[index];
            _models.RemoveAt(index);
            model.Dispose();
        }
    }

    /// <summary>
    ///     モデル個数を得る
    /// </summary>
    /// <returns>所持モデル個数</returns>
    public int GetModelNum()
    {
        return _models.Count;
    }

    public async void StartSpeaking(string filePath)
    {
        for (var i = 0; i < _models.Count; i++)
        {
            _models[i]._wavFileHandler.Start(filePath);
            await _models[i]._wavFileHandler.LoadWavFile(filePath);
            _models[i]
                .StartMotion(
                    LAppDefine.MotionGroupIdle,
                    0,
                    MotionPriority.PriorityIdle,
                    OnFinishedMotion
                );
        }
    }
}
