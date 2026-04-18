using Microsoft.SemanticKernel;

namespace PersonaEngine.Lib.LLM.Connection;

/// <summary>
///     Owns the application's <see cref="Kernel" /> and rebuilds it whenever
///     <see cref="PersonaEngine.Lib.Configuration.LlmOptions" /> change, gated by
///     <see cref="IKernelReloadCoordinator.IsSafeToReloadNow" /> so that in-flight turns
///     always complete against the kernel they started with.
/// </summary>
public interface ILlmKernelProvider
{
    /// <summary>Gets the current active <see cref="Kernel" /> instance.</summary>
    Kernel Current { get; }

    /// <summary>
    ///     Raised after a new <see cref="Kernel" /> has been successfully built and
    ///     <see cref="Current" /> has been updated. Subscribers must be resilient to
    ///     thread-pool callbacks.
    /// </summary>
    event Action? KernelRebuilt;
}
