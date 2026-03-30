using System.Text.Json.Nodes;
using PersonaEngine.Lib.Live2D.Framework.Model;

namespace PersonaEngine.Lib.Live2D.Framework.Effect;

/// <summary>
///     パーツの不透明度の管理と設定を行う。
/// </summary>
public class CubismPose
{
    public const float Epsilon = 0.001f;

    public const float DefaultFadeInSeconds = 0.5f;

    // Pose.jsonのタグ
    public const string FadeIn = "FadeInTime";

    public const string Link = "Link";

    public const string Groups = "Groups";

    public const string Id = "Id";

    /// <summary>
    ///     フェード時間[秒]
    /// </summary>
    private readonly float _fadeTimeSeconds = DefaultFadeInSeconds;

    /// <summary>
    ///     それぞれのパーツグループの個数
    /// </summary>
    private readonly List<int> _partGroupCounts = [];

    /// <summary>
    ///     パーツグループ
    /// </summary>
    private readonly List<PartData> _partGroups = [];

    /// <summary>
    ///     前回操作したモデル
    /// </summary>
    private CubismModel? _lastModel;

    /// <summary>
    ///     インスタンスを作成する。
    /// </summary>
    /// <param name="pose3json">pose3.jsonのデータ</param>
    public CubismPose(string pose3json)
    {
        using var stream = File.Open(
            pose3json,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite
        );
        var json = JsonNode.Parse(stream)?.AsObject() ?? throw new Exception("Pose json is error");

        // フェード時間の指定
        if (json.ContainsKey(FadeIn))
        {
            var item = json[FadeIn];
            _fadeTimeSeconds = item == null ? DefaultFadeInSeconds : (float)item;

            if (_fadeTimeSeconds < 0.0f)
            {
                _fadeTimeSeconds = DefaultFadeInSeconds;
            }
        }

        // パーツグループ
        if (json[Groups] is not JsonArray poseListInfo)
        {
            return;
        }

        foreach (var item in poseListInfo)
        {
            var idCount = item!.AsArray().Count;
            var groupCount = 0;

            for (var groupIndex = 0; groupIndex < idCount; ++groupIndex)
            {
                var partInfo = item[groupIndex]!;
                PartData partData = new()
                {
                    PartId = CubismFramework.CubismIdManager.GetId(partInfo[Id]!.ToString()),
                };

                // リンクするパーツの設定
                if (partInfo[Link] != null)
                {
                    var linkListInfo = partInfo[Link]!;
                    var linkCount = linkListInfo.AsArray().Count;

                    for (var linkIndex = 0; linkIndex < linkCount; ++linkIndex)
                    {
                        partData.Link.Add(
                            new PartData
                            {
                                PartId = CubismFramework.CubismIdManager.GetId(
                                    linkListInfo[linkIndex]!.ToString()
                                ),
                            }
                        );
                    }
                }

                _partGroups.Add(partData);

                ++groupCount;
            }

            _partGroupCounts.Add(groupCount);
        }
    }

    /// <summary>
    ///     モデルのパラメータを更新する。
    /// </summary>
    /// <param name="model">対象のモデル</param>
    /// <param name="deltaTimeSeconds">デルタ時間[秒]</param>
    public void UpdateParameters(CubismModel model, float deltaTimeSeconds)
    {
        // 前回のモデルと同じではないときは初期化が必要
        if (model.Model != _lastModel?.Model)
        {
            // パラメータインデックスの初期化
            Reset(model);
        }

        _lastModel = model;

        // 設定から時間を変更すると、経過時間がマイナスになることがあるので、経過時間0として対応。
        if (deltaTimeSeconds < 0.0f)
        {
            deltaTimeSeconds = 0.0f;
        }

        var beginIndex = 0;

        foreach (var item in _partGroupCounts)
        {
            DoFade(model, deltaTimeSeconds, beginIndex, item);

            beginIndex += item;
        }

        CopyPartOpacities(model);
    }

    /// <summary>
    ///     表示を初期化する。
    ///     不透明度の初期値が0でないパラメータは、不透明度を1に設定する。
    /// </summary>
    /// <param name="model">対象のモデル</param>
    public void Reset(CubismModel model)
    {
        var beginIndex = 0;

        foreach (var item in _partGroupCounts)
        {
            for (var j = beginIndex; j < beginIndex + item; ++j)
            {
                _partGroups[j].Initialize(model);

                var partsIndex = _partGroups[j].PartIndex;
                var paramIndex = _partGroups[j].ParameterIndex;

                if (partsIndex < 0)
                {
                    continue;
                }

                model.SetPartOpacity(partsIndex, j == beginIndex ? 1.0f : 0.0f);
                model.SetParameterValue(paramIndex, j == beginIndex ? 1.0f : 0.0f);

                for (var k = 0; k < _partGroups[j].Link.Count; ++k)
                {
                    _partGroups[j].Link[k].Initialize(model);
                }
            }

            beginIndex += item;
        }
    }

    /// <summary>
    ///     パーツの不透明度をコピーし、リンクしているパーツへ設定する。
    /// </summary>
    /// <param name="model">対象のモデル</param>
    private void CopyPartOpacities(CubismModel model)
    {
        foreach (var item in _partGroups)
        {
            if (item.Link.Count == 0)
            {
                continue; // 連動するパラメータはない
            }

            var partIndex = item.PartIndex;
            var opacity = model.GetPartOpacity(partIndex);

            foreach (var item1 in item.Link)
            {
                var linkPartIndex = item1.PartIndex;

                if (linkPartIndex < 0)
                {
                    continue;
                }

                model.SetPartOpacity(linkPartIndex, opacity);
            }
        }
    }

    /// <summary>
    ///     パーツのフェード操作を行う。
    /// </summary>
    /// <param name="model">対象のモデル</param>
    /// <param name="deltaTimeSeconds">デルタ時間[秒]</param>
    /// <param name="beginIndex">フェード操作を行うパーツグループの先頭インデックス</param>
    /// <param name="partGroupCount">フェード操作を行うパーツグループの個数</param>
    private void DoFade(
        CubismModel model,
        float deltaTimeSeconds,
        int beginIndex,
        int partGroupCount
    )
    {
        var visiblePartIndex = -1;
        var newOpacity = 1.0f;

        var Phi = 0.5f;
        var BackOpacityThreshold = 0.15f;

        // 現在、表示状態になっているパーツを取得
        for (var i = beginIndex; i < beginIndex + partGroupCount; ++i)
        {
            var partIndex = _partGroups[i].PartIndex;
            var paramIndex = _partGroups[i].ParameterIndex;

            if (model.GetParameterValue(paramIndex) > Epsilon)
            {
                if (visiblePartIndex >= 0)
                {
                    break;
                }

                visiblePartIndex = i;
                newOpacity = model.GetPartOpacity(partIndex);

                // 新しい不透明度を計算
                newOpacity += deltaTimeSeconds / _fadeTimeSeconds;

                if (newOpacity > 1.0f)
                {
                    newOpacity = 1.0f;
                }
            }
        }

        if (visiblePartIndex < 0)
        {
            visiblePartIndex = 0;
            newOpacity = 1.0f;
        }

        //  表示パーツ、非表示パーツの不透明度を設定する
        for (var i = beginIndex; i < beginIndex + partGroupCount; ++i)
        {
            var partsIndex = _partGroups[i].PartIndex;

            //  表示パーツの設定
            if (visiblePartIndex == i)
            {
                model.SetPartOpacity(partsIndex, newOpacity); // 先に設定
            }
            // 非表示パーツの設定
            else
            {
                var opacity = model.GetPartOpacity(partsIndex);
                float a1; // 計算によって求められる不透明度

                if (newOpacity < Phi)
                {
                    a1 = newOpacity * (Phi - 1) / Phi + 1.0f; // (0,1),(phi,phi)を通る直線式
                }
                else
                {
                    a1 = (1 - newOpacity) * Phi / (1.0f - Phi); // (1,0),(phi,phi)を通る直線式
                }

                // 背景の見える割合を制限する場合
                var backOpacity = (1.0f - a1) * (1.0f - newOpacity);

                if (backOpacity > BackOpacityThreshold)
                {
                    a1 = 1.0f - BackOpacityThreshold / (1.0f - newOpacity);
                }

                if (opacity > a1)
                {
                    opacity = a1; // 計算の不透明度よりも大きければ（濃ければ）不透明度を上げる
                }

                model.SetPartOpacity(partsIndex, opacity);
            }
        }
    }
}
