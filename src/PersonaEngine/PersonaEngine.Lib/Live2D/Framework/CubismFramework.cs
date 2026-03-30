using PersonaEngine.Lib.Live2D.Framework.Core;
using PersonaEngine.Lib.Live2D.Framework.Id;

namespace PersonaEngine.Lib.Live2D.Framework;

/// <summary>
///     Live2D Cubism Original Workflow SDKのエントリポイント
///     利用開始時はCubismFramework.Initialize()を呼び、CubismFramework.Dispose()で終了する。
/// </summary>
public static class CubismFramework
{
    /// <summary>
    ///     メッシュ頂点のオフセット値
    /// </summary>
    public const int VertexOffset = 0;

    /// <summary>
    ///     メッシュ頂点のステップ値
    /// </summary>
    public const int VertexStep = 2;

    private static ICubismAllocator? s_allocator;

    private static Option? s_option;

    /// <summary>
    ///     IDマネージャのインスタンスを取得する。
    /// </summary>
    public static CubismIdManager CubismIdManager { get; private set; } = new();

    public static bool IsInitialized { get; private set; }

    public static bool IsStarted { get; private set; }

    /// <summary>
    ///     Cubism FrameworkのAPIを使用可能にする。
    ///     APIを実行する前に必ずこの関数を実行すること。
    ///     引数に必ずメモリアロケータを渡してください。
    ///     一度準備が完了して以降は、再び実行しても内部処理がスキップされます。
    /// </summary>
    /// <param name="allocator">ICubismAllocatorクラスのインスタンス</param>
    /// <param name="option">Optionクラスのインスタンス</param>
    /// <returns>準備処理が完了したらtrueが返ります。</returns>
    public static bool StartUp(ICubismAllocator allocator, Option option)
    {
        if (IsStarted)
        {
            CubismLog.Info("[Live2D SDK]CubismFramework.StartUp() is already done.");

            return IsStarted;
        }

        s_option = option;
        if (s_option != null)
        {
            CubismCore.SetLogFunction(s_option.LogFunction);
        }

        if (allocator == null)
        {
            CubismLog.Warning(
                "[Live2D SDK]CubismFramework.StartUp() failed, need allocator instance."
            );
            IsStarted = false;
        }
        else
        {
            s_allocator = allocator;
            IsStarted = true;
        }

        //Live2D Cubism Coreバージョン情報を表示
        if (IsStarted)
        {
            var version = CubismCore.GetVersion();

            var major = (version & 0xFF000000) >> 24;
            var minor = (version & 0x00FF0000) >> 16;
            var patch = version & 0x0000FFFF;
            var versionNumber = version;

            CubismLog.Info(
                $"[Live2D SDK]Cubism Core version: {major:#0}.{minor:0}.{patch:0000} ({versionNumber})"
            );
        }

        CubismLog.Info("[Live2D SDK]CubismFramework.StartUp() is complete.");

        return IsStarted;
    }

    /// <summary>
    ///     StartUp()で初期化したCubismFrameworkの各パラメータをクリアします。
    ///     Dispose()したCubismFrameworkを再利用する際に利用してください。
    /// </summary>
    public static void CleanUp()
    {
        IsStarted = false;
        IsInitialized = false;
    }

    /// <summary>
    ///     Cubism Framework内のリソースを初期化してモデルを表示可能な状態にします。
    ///     再度Initialize()するには先にDispose()を実行する必要があります。
    /// </summary>
    public static void Initialize()
    {
        if (!IsStarted)
        {
            CubismLog.Warning("[Live2D SDK]CubismFramework is not started.");

            return;
        }

        // --- s_isInitializedによる連続初期化ガード ---
        // 連続してリソース確保が行われないようにする。
        // 再度Initialize()するには先にDispose()を実行する必要がある。
        if (IsInitialized)
        {
            CubismLog.Warning(
                "[Live2D SDK]CubismFramework.Initialize() skipped, already initialized."
            );

            return;
        }

        IsInitialized = true;

        CubismLog.Info("[Live2D SDK]CubismFramework.Initialize() is complete.");
    }

    /// <summary>
    ///     Cubism Framework内の全てのリソースを解放します。
    ///     ただし、外部で確保されたリソースについては解放しません。
    ///     外部で適切に破棄する必要があります。
    /// </summary>
    public static void Dispose()
    {
        if (!IsStarted)
        {
            CubismLog.Warning("[Live2D SDK]CubismFramework is not started.");

            return;
        }

        // --- s_isInitializedによる未初期化解放ガード ---
        // Dispose()するには先にInitialize()を実行する必要がある。
        if (!IsInitialized) // false...リソース未確保の場合
        {
            CubismLog.Warning("[Live2D SDK]CubismFramework.Dispose() skipped, not initialized.");

            return;
        }

        IsInitialized = false;

        CubismLog.Info("[Live2D SDK]CubismFramework.Dispose() is complete.");
    }

    /// <summary>
    ///     Core APIにバインドしたログ関数を実行する
    /// </summary>
    /// <param name="data">ログメッセージ</param>
    public static void CoreLogFunction(string data)
    {
        CubismCore.GetLogFunction()?.Invoke(data);
    }

    /// <summary>
    ///     現在のログ出力レベル設定の値を返す。
    /// </summary>
    /// <returns>現在のログ出力レベル設定の値</returns>
    public static LogLevel GetLoggingLevel()
    {
        if (s_option != null)
        {
            return s_option.LoggingLevel;
        }

        return LogLevel.Off;
    }

    public static IntPtr Allocate(int size)
    {
        return s_allocator!.Allocate(size);
    }

    public static IntPtr AllocateAligned(int size, int alignment)
    {
        return s_allocator!.AllocateAligned(size, alignment);
    }

    public static void Deallocate(IntPtr address)
    {
        s_allocator!.Deallocate(address);
    }

    public static void DeallocateAligned(IntPtr address)
    {
        s_allocator!.DeallocateAligned(address);
    }
}
