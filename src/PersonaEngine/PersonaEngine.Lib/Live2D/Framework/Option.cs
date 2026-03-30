using PersonaEngine.Lib.Live2D.Framework.Core;

namespace PersonaEngine.Lib.Live2D.Framework;

/// <summary>
///     ログ出力のレベル
/// </summary>
public enum LogLevel
{
    /// <summary>
    ///     詳細ログ
    /// </summary>
    Verbose = 0,

    /// <summary>
    ///     デバッグログ
    /// </summary>
    Debug,

    /// <summary>
    ///     Infoログ
    /// </summary>
    Info,

    /// <summary>
    ///     警告ログ
    /// </summary>
    Warning,

    /// <summary>
    ///     エラーログ
    /// </summary>
    Error,

    /// <summary>
    ///     ログ出力無効
    /// </summary>
    Off,
}

/// <summary>
///     CubismFrameworkに設定するオプション要素を定義するクラス
/// </summary>
public class Option
{
    /// <summary>
    ///     ログ出力の関数ポイ
    /// </summary>
    public required LogFunction LogFunction;

    /// <summary>
    ///     ログ出力レベル設定
    /// </summary>
    public LogLevel LoggingLevel;
}
