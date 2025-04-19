using PersonaEngine.Lib.Live2D.Framework.Model;

namespace PersonaEngine.Lib.Live2D.Framework.Motion;

/// <summary>
///     モーション再生の管理用クラス。CubismMotionモーションなどACubismMotionのサブクラスを再生するために使用する。
///     再生中に別のモーションが StartMotion()された場合は、新しいモーションに滑らかに変化し旧モーションは中断する。
///     表情用モーション、体用モーションなどを分けてモーション化した場合など、
///     複数のモーションを同時に再生させる場合は、複数のCubismMotionQueueManagerインスタンスを使用する。
/// </summary>
public class CubismMotionQueueManager
{
    private readonly List<CubismMotionQueueEntry> _remove = [];

    /// <summary>
    ///     モーション
    /// </summary>
    protected readonly List<CubismMotionQueueEntry> Motions = [];

    /// <summary>
    ///     コールバック関数ポインタ
    /// </summary>
    private CubismMotionEventFunction? _eventCallback;

    /// <summary>
    ///     コールバックに戻されるデータ
    /// </summary>
    private CubismUserModel? _eventCustomData;

    /// <summary>
    ///     デルタ時間の積算値[秒]
    /// </summary>
    protected float UserTimeSeconds;

    /// <summary>
    ///     指定したモーションを開始する。同じタイプのモーションが既にある場合は、既存のモーションに終了フラグを立て、フェードアウトを開始させる。
    /// </summary>
    /// <param name="motion">開始するモーション</param>
    /// <returns>開始したモーションの識別番号を返す。個別のモーションが終了したか否かを判定するIsFinished()の引数で使用する。開始できない時は「-1」</returns>
    public CubismMotionQueueEntry StartMotion(ACubismMotion motion)
    {
        CubismMotionQueueEntry motionQueueEntry;

        // 既にモーションがあれば終了フラグを立てる
        for ( var i = 0; i < Motions.Count; ++i )
        {
            motionQueueEntry = Motions[0];
            if ( motionQueueEntry == null )
            {
                continue;
            }

            motionQueueEntry.SetFadeout(motionQueueEntry.Motion.FadeOutSeconds);
        }

        motionQueueEntry = new CubismMotionQueueEntry { Motion = motion };

        Motions.Add(motionQueueEntry);

        return motionQueueEntry;
    }

    /// <summary>
    ///     指定したモーションを開始する。同じタイプのモーションが既にある場合は、既存のモーションに終了フラグを立て、フェードアウトを開始させる。
    /// </summary>
    /// <param name="motion">開始するモーション</param>
    /// <param name="autoDelete">再生が終了したモーションのインスタンスを削除するなら true</param>
    /// <param name="userTimeSeconds">デルタ時間の積算値[秒]</param>
    /// <returns>開始したモーションの識別番号を返す。個別のモーションが終了したか否かを判定するIsFinished()の引数で使用する。開始できない時は「-1」</returns>
    [Obsolete("Please use StartMotion(ACubismMotion motion")]
    public CubismMotionQueueEntry StartMotion(ACubismMotion motion, float userTimeSeconds)
    {
        CubismLog.Warning("StartMotion(ACubismMotion motion, float userTimeSeconds) is a deprecated function. Please use StartMotion(ACubismMotion motion).");

        CubismMotionQueueEntry motionQueueEntry;

        // 既にモーションがあれば終了フラグを立てる
        for ( var i = 0; i < Motions.Count; ++i )
        {
            motionQueueEntry = Motions[i];
            if ( motionQueueEntry == null )
            {
                continue;
            }

            motionQueueEntry.SetFadeout(motionQueueEntry.Motion.FadeOutSeconds);
        }

        motionQueueEntry = new CubismMotionQueueEntry { Motion = motion }; // 終了時に破棄する

        Motions.Add(motionQueueEntry);

        return motionQueueEntry;
    }

    /// <summary>
    ///     すべてのモーションが終了しているかどうか。
    /// </summary>
    /// <returns>
    ///     true    すべて終了している
    ///     false   終了していない
    /// </returns>
    public bool IsFinished()
    {
        // ------- 処理を行う --------
        // 既にモーションがあれば終了フラグを立てる
        foreach ( var item in Motions )
        {
            // ----- 終了済みの処理があれば削除する ------
            if ( !item.Finished )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     指定したモーションが終了しているかどうか。
    /// </summary>
    /// <param name="motionQueueEntryNumber">モーションの識別番号</param>
    /// <returns>
    ///     true    指定したモーションは終了している
    ///     false   終了していない
    /// </returns>
    public bool IsFinished(object motionQueueEntryNumber)
    {
        // 既にモーションがあれば終了フラグを立てる

        foreach ( var item in Motions )
        {
            if ( item == null )
            {
                continue;
            }

            if ( item == motionQueueEntryNumber && !item.Finished )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     すべてのモーションを停止する。
    /// </summary>
    public void StopAllMotions()
    {
        // ------- 処理を行う --------
        // 既にモーションがあれば終了フラグを立てる

        Motions.Clear();
    }

    /// <summary>
    ///     指定したCubismMotionQueueEntryを取得する。
    /// </summary>
    /// <param name="motionQueueEntryNumber">モーションの識別番号</param>
    /// <returns>
    ///     指定したCubismMotionQueueEntryへのポインタ
    ///     NULL   見つからなかった
    /// </returns>
    public CubismMotionQueueEntry? GetCubismMotionQueueEntry(object motionQueueEntryNumber)
    {
        //------- 処理を行う --------
        //既にモーションがあれば終了フラグを立てる

        foreach ( var item in Motions )
        {
            if ( item == motionQueueEntryNumber )
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    ///     イベントを受け取るCallbackの登録をする。
    /// </summary>
    /// <param name="callback">コールバック関数</param>
    /// <param name="customData">コールバックに返されるデータ</param>
    public void SetEventCallback(CubismMotionEventFunction callback, CubismUserModel customData)
    {
        _eventCallback   = callback;
        _eventCustomData = customData;
    }

    /// <summary>
    ///     モーションを更新して、モデルにパラメータ値を反映する。
    /// </summary>
    /// <param name="model">対象のモデル</param>
    /// <param name="userTimeSeconds">デルタ時間の積算値[秒]</param>
    /// <returns>
    ///     true    モデルへパラメータ値の反映あり
    ///     false   モデルへパラメータ値の反映なし(モーションの変化なし)
    /// </returns>
    public virtual bool DoUpdateMotion(CubismModel model, float userTimeSeconds)
    {
        var updated = false;

        // ------- 処理を行う --------
        // 既にモーションがあれば終了フラグを立てる

        _remove.Clear();

        for ( var motionIndex = Motions.Count - 1; motionIndex >= 0; motionIndex-- )
        {
            var item   = Motions[motionIndex];
            var motion = item.Motion;

            // ------ 値を反映する ------
            motion.UpdateParameters(model, item, userTimeSeconds);
            updated = true;

            // ------ ユーザトリガーイベントを検査する ----
            var firedList = motion.GetFiredEvent(
                                                 item.LastEventCheckSeconds - item.StartTime,
                                                 userTimeSeconds - item.StartTime);

            for ( var i = 0; i < firedList.Count; ++i )
            {
                _eventCallback?.Invoke(_eventCustomData, firedList[i]);
            }

            item.LastEventCheckSeconds = userTimeSeconds;

            // ----- 終了済みの処理があれば削除する ------
            if ( item.Finished )
            {
                _remove.Add(item); // 削除
            }
            else
            {
                if ( item.IsTriggeredFadeOut )
                {
                    item.StartFadeout(item.FadeOutSeconds, userTimeSeconds);
                }
            }
        }

        foreach ( var item in _remove )
        {
            Motions.Remove(item);
        }

        return updated;
    }
}