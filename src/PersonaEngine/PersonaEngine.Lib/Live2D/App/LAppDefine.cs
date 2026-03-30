using PersonaEngine.Lib.Live2D.Framework;

namespace PersonaEngine.Lib.Live2D.App;

/// <summary>
///     アプリケーションクラス。
///     Cubism SDK の管理を行う。
/// </summary>
public static class LAppDefine
{
    // 画面
    public const float ViewScale = 1.0f;

    public const float ViewMaxScale = 2.0f;

    public const float ViewMinScale = 0.8f;

    public const float ViewLogicalLeft = -1.0f;

    public const float ViewLogicalRight = 1.0f;

    public const float ViewLogicalBottom = -1.0f;

    public const float ViewLogicalTop = -1.0f;

    public const float ViewLogicalMaxLeft = -2.0f;

    public const float ViewLogicalMaxRight = 2.0f;

    public const float ViewLogicalMaxBottom = -2.0f;

    public const float ViewLogicalMaxTop = 2.0f;

    // 外部定義ファイル(json)と合わせる
    public const string MotionGroupIdle = "Idle"; // アイドリング

    public const string MotionGroupTapBody = "TapBody"; // 体をタップしたとき

    // 外部定義ファイル(json)と合わせる
    public const string HitAreaNameHead = "Head";

    public const string HitAreaNameBody = "Body";

    // MOC3の整合性検証オプション
    public const bool MocConsistencyValidationEnable = true;

    // Frameworkから出力するログのレベル設定
    public const LogLevel CubismLoggingLevel = LogLevel.Verbose;
}
