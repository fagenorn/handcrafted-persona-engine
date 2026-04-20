using PersonaEngine.Lib.Assets.Manifest;
using PersonaEngine.Lib.Bootstrapper.GpuPreflight;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Sources;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// Answer the user gives when the GPU preflight flags a problem before the
/// download starts. The non-interactive UI always returns
/// <see cref="Abort" />; the interactive UI prompts the user.
/// </summary>
public enum GpuWarningResponse
{
    Continue,
    Abort,
}

/// <summary>
/// Abstraction over the user-facing chrome (picker, progress, prompts) so the runner can be tested
/// without Spectre.Console and so headless modes can plug in a no-op implementation.
/// </summary>
public interface IBootstrapUserInterface
{
    /// <summary>Prompt the user to pick an install profile; called only when no lock exists or Reinstall mode.</summary>
    Task<ProfileTier> PickProfileAsync(IReadOnlyList<ProfileChoice> choices, CancellationToken ct);

    /// <summary>
    /// Show the user a GPU preflight failure and ask whether to continue anyway
    /// or abort the installer. Called only when <see cref="GpuStatus.Ok" /> is
    /// <c>false</c>; a pass short-circuits without touching the UI.
    /// </summary>
    Task<GpuWarningResponse> ShowGpuWarningAsync(GpuStatus status, CancellationToken ct);

    /// <summary>Show one-line summary banner before downloads start (e.g. "Installing 'Stream with it' — 8.0 GB").</summary>
    void ShowPlanSummary(AssetPlan plan);

    /// <summary>
    /// Run the download/verify loop, reporting per-asset progress. Implementations decide rendering.
    /// <para>
    /// <paramref name="items"/> contains only items that require network download
    /// (<see cref="AssetAction.Download"/> or <see cref="AssetAction.Redownload"/>).
    /// <see cref="BootstrapRunner"/> pre-filters the plan before calling this method, so
    /// implementations may call <paramref name="executeOne"/> unconditionally for every item.
    /// </para>
    /// </summary>
    Task<bool> RunWithProgressAsync(
        IReadOnlyList<AssetPlanItem> items,
        Func<AssetPlanItem, IProgress<DownloadProgress>, CancellationToken, Task> executeOne,
        CancellationToken ct
    );

    /// <summary>Show final outcome line(s).</summary>
    void ShowResult(BootstrapResult result);
}

/// <summary>Display data for one profile choice, ready to render in the picker.</summary>
public sealed record ProfileChoice
{
    public required ProfileTier Profile { get; init; }
    public required string Title { get; init; }
    public required string SizeLabel { get; init; }
    public required string Tagline { get; init; }
    public required IReadOnlyList<string> Bullets { get; init; }
}
