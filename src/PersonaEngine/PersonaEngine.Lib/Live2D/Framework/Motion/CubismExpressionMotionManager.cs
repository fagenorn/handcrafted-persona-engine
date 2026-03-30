using PersonaEngine.Lib.Live2D.Framework.Math;
using PersonaEngine.Lib.Live2D.Framework.Model;

namespace PersonaEngine.Lib.Live2D.Framework.Motion;

/// <summary>
///     パラメータに適用する表情の値を持たせる構造体
/// </summary>
public record ExpressionParameterValue
{
    /// <summary>
    ///     加算値
    /// </summary>
    public float AdditiveValue;

    /// <summary>
    ///     乗算値
    /// </summary>
    public float MultiplyValue;

    /// <summary>
    ///     上書き値
    /// </summary>
    public float OverwriteValue;

    /// <summary>
    ///     パラメータID
    /// </summary>
    public string ParameterId;
}

public class CubismExpressionMotionManager : CubismMotionQueueManager
{
    // モデルに適用する各パラメータの値
    private readonly List<ExpressionParameterValue> _expressionParameterValues = [];

    // 再生中の表情のウェイト
    private readonly List<float> _fadeWeights = [];

    /// <summary>
    ///     現在再生中のモーションの優先度
    /// </summary>
    public MotionPriority CurrentPriority { get; private set; }

    /// <summary>
    ///     再生予定のモーションの優先度。再生中は0になる。モーションファイルを別スレッドで読み込むときの機能。
    /// </summary>
    public MotionPriority ReservePriority { get; set; }

    /// <summary>
    ///     優先度を設定して表情モーションを開始する。
    /// </summary>
    /// <param name="motion">モーション</param>
    /// <param name="priority">優先度</param>
    /// <returns>開始したモーションの識別番号を返す。個別のモーションが終了したか否かを判定するIsFinished()の引数で使用する。開始できない時は「-1」</returns>
    public CubismMotionQueueEntry StartMotionPriority(ACubismMotion motion, MotionPriority priority)
    {
        if (priority == ReservePriority)
        {
            ReservePriority = 0; // 予約を解除
        }

        CurrentPriority = priority; // 再生中モーションの優先度を設定

        _fadeWeights.Add(0.0f);

        return StartMotion(motion, UserTimeSeconds);
    }

    /// <summary>
    ///     表情モーションを更新して、モデルにパラメータ値を反映する。
    /// </summary>
    /// <param name="model">対象のモデル</param>
    /// <param name="deltaTimeSeconds"> デルタ時間[秒]</param>
    /// <returns>
    ///     true    更新されている
    ///     false   更新されていない
    /// </returns>
    public bool UpdateMotion(CubismModel model, float deltaTimeSeconds)
    {
        UserTimeSeconds += deltaTimeSeconds;
        var updated = false;
        var motions = Motions;

        var expressionWeight = 0.0f;
        var expressionIndex = 0;

        // If there is already a motion, set the end flag
        var list = new List<CubismMotionQueueEntry>();
        foreach (var item in motions)
        {
            if (item.Motion is not CubismExpressionMotion expressionMotion)
            {
                list.Add(item);

                continue;
            }

            var expressionParameters = expressionMotion.Parameters;
            if (item.Available)
            {
                // 再生中のExpressionが参照しているパラメータをすべてリストアップ
                for (var i = 0; i < expressionParameters.Count; ++i)
                {
                    if (expressionParameters[i].ParameterId == null)
                    {
                        continue;
                    }

                    var index = -1;
                    // リストにパラメータIDが存在するか検索
                    for (var j = 0; j < _expressionParameterValues.Count; ++j)
                    {
                        if (
                            _expressionParameterValues[j].ParameterId
                            != expressionParameters[i].ParameterId
                        )
                        {
                            continue;
                        }

                        index = j;

                        break;
                    }

                    if (index >= 0)
                    {
                        continue;
                    }

                    // If the parameter does not exist in the list, add it.
                    ExpressionParameterValue item1 = new()
                    {
                        ParameterId = expressionParameters[i].ParameterId,
                        AdditiveValue = CubismExpressionMotion.DefaultAdditiveValue,
                        MultiplyValue = CubismExpressionMotion.DefaultMultiplyValue,
                    };
                    item1.OverwriteValue = model.GetParameterValue(item1.ParameterId);
                    _expressionParameterValues.Add(item1);
                }
            }

            // ------ 値を計算する ------
            expressionMotion.SetupMotionQueueEntry(item, UserTimeSeconds);
            _fadeWeights[expressionIndex] = expressionMotion.UpdateFadeWeight(
                item,
                UserTimeSeconds
            );
            expressionMotion.CalculateExpressionParameters(
                model,
                UserTimeSeconds,
                item,
                _expressionParameterValues,
                expressionIndex,
                _fadeWeights[expressionIndex]
            );

            expressionWeight +=
                expressionMotion.FadeInSeconds == 0.0f
                    ? 1.0f
                    : CubismMath.GetEasingSine(
                        (UserTimeSeconds - item.FadeInStartTime) / expressionMotion.FadeInSeconds
                    );

            updated = true;

            if (item.IsTriggeredFadeOut)
            {
                // フェードアウト開始
                item.StartFadeout(item.FadeOutSeconds, UserTimeSeconds);
            }

            ++expressionIndex;
        }

        // ----- 最新のExpressionのフェードが完了していればそれ以前を削除する ------
        if (motions.Count > 1)
        {
            var latestFadeWeight = _fadeWeights[_fadeWeights.Count - 1];
            if (latestFadeWeight >= 1.0f)
            {
                // 配列の最後の要素は削除しない
                for (var i = motions.Count - 2; i >= 0; i--)
                {
                    motions.RemoveAt(i);
                    _fadeWeights.RemoveAt(i);
                }
            }
        }

        if (expressionWeight > 1.0f)
        {
            expressionWeight = 1.0f;
        }

        // モデルに各値を適用
        for (var i = 0; i < _expressionParameterValues.Count; ++i)
        {
            model.SetParameterValue(
                _expressionParameterValues[i].ParameterId,
                (
                    _expressionParameterValues[i].OverwriteValue
                    + _expressionParameterValues[i].AdditiveValue
                ) * _expressionParameterValues[i].MultiplyValue,
                expressionWeight
            );

            _expressionParameterValues[i].AdditiveValue =
                CubismExpressionMotion.DefaultAdditiveValue;
            _expressionParameterValues[i].MultiplyValue =
                CubismExpressionMotion.DefaultMultiplyValue;
        }

        return updated;
    }

    /// <summary>
    ///     現在の表情のフェードのウェイト値を取得する。
    /// </summary>
    /// <param name="index">取得する表情モーションのインデックス</param>
    /// <returns>表情のフェードのウェイト値</returns>
    public float GetFadeWeight(int index)
    {
        return _fadeWeights[index];
    }
}
