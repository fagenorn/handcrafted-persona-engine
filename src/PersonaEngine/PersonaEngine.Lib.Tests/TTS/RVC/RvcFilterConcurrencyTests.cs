using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.IO;
using PersonaEngine.Lib.TTS.RVC;
using PersonaEngine.Lib.TTS.Synthesis;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.RVC;

public sealed class RvcFilterConcurrencyTests
{
    /// <summary>
    ///     Drives Process() on many threads while OnChange swaps options. With Enabled = false
    ///     Process returns early without touching the model, so this exercises the options-
    ///     change callback concurrency surface without needing real ONNX assets.
    ///     After the Task 12 lock fix, the same test should still pass (defensive regression).
    /// </summary>
    [Fact]
    public async Task Process_And_OptionsChange_Do_Not_Race()
    {
        var options = new RVCFilterOptions { Enabled = false };
        var monitor = new TestOptionsMonitor<RVCFilterOptions>(options);

        var modelProvider = Substitute.For<IModelProvider>();
        modelProvider.GetModelPath(Arg.Any<ModelId>()).Returns(string.Empty);

        var voiceProvider = Substitute.For<IRVCVoiceProvider>();
        voiceProvider.GetVoice(Arg.Any<string>()).Returns(new RvcVoiceInfo(string.Empty, 40000));

        using var filter = new RVCFilter(
            monitor,
            modelProvider,
            voiceProvider,
            NullLogger<RVCFilter>.Instance
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var processorTasks = Enumerable
            .Range(0, 8)
            .Select(_ =>
                Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        var segment = new AudioSegment(
                            new float[24000],
                            24000,
                            Array.Empty<Token>()
                        );
                        filter.Process(segment);
                    }
                })
            )
            .ToArray();

        var swapperTask = Task.Run(() =>
        {
            var i = 0;
            while (!cts.IsCancellationRequested)
            {
                monitor.Fire(
                    new RVCFilterOptions { Enabled = false, DefaultVoice = $"v{i++ % 3}" }
                );
            }
        });

        await Task.WhenAll(processorTasks.Append(swapperTask));
        // No exceptions => pass.
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly List<Action<T, string?>> _listeners = new();

        private T _value;

        public TestOptionsMonitor(T initial) => _value = initial;

        public T CurrentValue => _value;

        public T Get(string? name) => _value;

        public IDisposable OnChange(Action<T, string?> listener)
        {
            lock (_listeners)
                _listeners.Add(listener);

            return new Disposable(() =>
            {
                lock (_listeners)
                    _listeners.Remove(listener);
            });
        }

        public void Fire(T newValue)
        {
            _value = newValue;
            Action<T, string?>[] snapshot;
            lock (_listeners)
                snapshot = _listeners.ToArray();
            foreach (var l in snapshot)
                l(newValue, null);
        }

        private sealed class Disposable(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}
