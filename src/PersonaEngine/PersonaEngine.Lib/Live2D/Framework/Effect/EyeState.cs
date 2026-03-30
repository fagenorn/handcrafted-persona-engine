namespace PersonaEngine.Lib.Live2D.Framework.Effect;

public enum EyeState
{
    /// <summary>
    ///     初期状態
    /// </summary>
    First = 0,

    /// <summary>
    ///     まばたきしていない状態
    /// </summary>
    Interval,

    /// <summary>
    ///     まぶたが閉じていく途中の状態
    /// </summary>
    Closing,

    /// <summary>
    ///     まぶたが閉じている状態
    /// </summary>
    Closed,

    /// <summary>
    ///     まぶたが開いていく途中の状態
    /// </summary>
    Opening,
}
