using Microsoft.ML.OnnxRuntime;
using PersonaEngine.Lib.Utils.Onnx;
using Xunit;

namespace PersonaEngine.Lib.Tests.Utils.Onnx;

public class OnnxSessionFactoryTests
{
    [Fact]
    public void CreateOptions_Default_SetsParallelExecutionWithFullCores()
    {
        var options = OnnxSessionFactory.CreateOptions(
            ExecutionProvider.Cpu,
            SessionProfile.Default
        );

        Assert.Equal(ExecutionMode.ORT_PARALLEL, options.ExecutionMode);
        Assert.Equal(GraphOptimizationLevel.ORT_ENABLE_ALL, options.GraphOptimizationLevel);
    }

    [Fact]
    public void CreateOptions_Sequential_SetsSequentialExecution()
    {
        var options = OnnxSessionFactory.CreateOptions(
            ExecutionProvider.Cpu,
            SessionProfile.Sequential
        );

        Assert.Equal(ExecutionMode.ORT_SEQUENTIAL, options.ExecutionMode);
    }

    [Fact]
    public void CreateOptions_LowLatency_SetsSingleThreaded()
    {
        var options = OnnxSessionFactory.CreateOptions(
            ExecutionProvider.Cpu,
            SessionProfile.LowLatency
        );

        Assert.Equal(ExecutionMode.ORT_PARALLEL, options.ExecutionMode);
    }

    [Fact]
    public void CreateOptions_WithConfigure_AppliesCallback()
    {
        var callbackInvoked = false;

        var options = OnnxSessionFactory.CreateOptions(
            ExecutionProvider.Cpu,
            SessionProfile.Default,
            configure: opts =>
            {
                callbackInvoked = true;
                opts.AddSessionConfigEntry("session.set_denormal_as_zero", "1");
            }
        );

        Assert.True(callbackInvoked);
    }

    [Fact]
    public void CreateOptions_Cpu_DoesNotThrow()
    {
        var options = OnnxSessionFactory.CreateOptions(
            ExecutionProvider.Cpu,
            SessionProfile.Default
        );

        Assert.NotNull(options);
    }
}
