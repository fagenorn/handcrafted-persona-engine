namespace PersonaEngine.Lib.Live2D.App;

/// <summary>
///     プラットフォーム依存機能を抽象化する Cubism Platform Abstraction Layer.
///     ファイル読み込みや時刻取得等のプラットフォームに依存する関数をまとめる
/// </summary>
public static class LAppPal
{
    /// <summary>
    ///     デルタ時間（前回フレームとの差分）を取得する
    /// </summary>
    /// <returns>デルタ時間[ms]</returns>
    public static float DeltaTime { get; set; }
}
