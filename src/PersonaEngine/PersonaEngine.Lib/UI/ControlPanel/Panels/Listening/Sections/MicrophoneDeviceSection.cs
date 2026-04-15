using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

/// <summary>
///     Microphone device selection card. Exposes the system input devices as a combo
///     with a Refresh button, plus a Test Microphone round-trip that records 3 s of
///     mic audio and plays it back through <see cref="IOneShotPlayer" />.
/// </summary>
public sealed class MicrophoneDeviceSection : IDisposable
{
    private const string DefaultDeviceLabel = "(Default Device)";
    private const int TestSampleRate = 16000;
    private const int TestDurationSamples = TestSampleRate * 3; // 3 seconds
    private const float TestDurationSeconds = 3f;

    private readonly IConfigWriter _configWriter;
    private readonly IMicrophone _microphone;
    private readonly IOneShotPlayer _oneShotPlayer;
    private readonly IDisposable? _changeSubscription;

    private MicrophoneConfiguration _mic;
    private string[] _devices = Array.Empty<string>();
    private bool _initialized;

    // Test state machine
    private enum TestState
    {
        Idle,
        Recording,
        Playing,
    }

    private TestState _testState = TestState.Idle;
    private float[]? _testBuffer;
    private int _testWriteIndex;
    private float _testElapsed;
    private CancellationTokenSource? _testCts;
    private Task? _testPlayTask;
    private AudioSamplesHandler? _testRecordHandler;
    private OneShotAnimation _testButtonPress;

    public MicrophoneDeviceSection(
        IOptionsMonitor<MicrophoneConfiguration> monitor,
        IMicrophone microphone,
        IConfigWriter configWriter,
        IOneShotPlayer oneShotPlayer
    )
    {
        _microphone = microphone;
        _configWriter = configWriter;
        _oneShotPlayer = oneShotPlayer;
        _mic = monitor.CurrentValue;
        _changeSubscription = monitor.OnChange((updated, _) => _mic = updated);
    }

    public void Dispose()
    {
        StopTest();
        _changeSubscription?.Dispose();
    }

    public void Render(float dt)
    {
        if (!_initialized)
        {
            RefreshDevices();
            _initialized = true;
        }

        using (Ui.Card("##mic_device", padding: 12f))
        {
            RenderHeader();
            RenderDevicePicker();
            ImGui.Spacing();
            RenderTestButton(dt);
        }

        UpdateTestState(dt);
    }

    // ── Header ────────────────────────────────────────────────────────────────

    private static void RenderHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
        ImGui.TextUnformatted("Microphone");
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted("Which audio input to capture your voice from");
        ImGui.PopStyleColor();

        ImGui.Spacing();
    }

    // ── Device Picker ─────────────────────────────────────────────────────────

    private void RenderDevicePicker()
    {
        var currentIndex = 0;
        if (!string.IsNullOrWhiteSpace(_mic.DeviceName))
        {
            for (var i = 1; i < _devices.Length; i++)
            {
                if (string.Equals(_devices[i], _mic.DeviceName, StringComparison.Ordinal))
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        ImGui.SetNextItemWidth(-110f); // leave room for Refresh button
        if (ImGui.Combo("##MicDevice", ref currentIndex, _devices, _devices.Length))
        {
            var selected = currentIndex == 0 ? null : _devices[currentIndex];
            _mic = _mic with { DeviceName = selected };
            _configWriter.Write(_mic);
        }

        ImGuiHelpers.HandCursorOnHover();
        ImGui.SameLine();

        if (ImGui.Button("Refresh", new Vector2(100f, 0f)))
        {
            RefreshDevices();
        }

        ImGuiHelpers.HandCursorOnHover();
        ImGuiHelpers.Tooltip("Re-scan for available audio input devices.");
    }

    private void RefreshDevices()
    {
        var discovered = _microphone.GetAvailableDevices().ToArray();
        _devices = new string[discovered.Length + 1];
        _devices[0] = DefaultDeviceLabel;
        Array.Copy(discovered, 0, _devices, 1, discovered.Length);
    }

    // ── Test Microphone ───────────────────────────────────────────────────────

    private void RenderTestButton(float dt)
    {
        switch (_testState)
        {
            case TestState.Idle:
                if (
                    ImGuiHelpers.PrimaryButtonWithFeedback(
                        "Test Microphone",
                        ref _testButtonPress,
                        dt
                    )
                )
                {
                    StartRecording();
                }
                break;

            case TestState.Recording:
            {
                var remaining = (int)MathF.Ceiling(TestDurationSeconds - _testElapsed);
                if (remaining < 1)
                    remaining = 1;
                ImGui.BeginDisabled();
                ImGui.PushStyleColor(ImGuiCol.Button, Theme.Error with { W = 0.6f });
                ImGui.Button($"● Recording {remaining}…");
                ImGui.PopStyleColor();
                ImGui.EndDisabled();
                break;
            }

            case TestState.Playing:
                ImGui.BeginDisabled();
                ImGui.PushStyleColor(ImGuiCol.Button, Theme.AccentPrimary with { W = 0.6f });
                ImGui.Button("▶ Playing back…");
                ImGui.PopStyleColor();
                ImGui.EndDisabled();
                break;
        }
    }

    private void StartRecording()
    {
        _testBuffer ??= new float[TestDurationSamples];
        Array.Clear(_testBuffer, 0, _testBuffer.Length);
        _testWriteIndex = 0;
        _testElapsed = 0f;
        _testState = TestState.Recording;

        _testRecordHandler = OnTestSamples;
        _microphone.SamplesAvailable += _testRecordHandler;
    }

    private void OnTestSamples(ReadOnlySpan<float> samples, int sampleRate)
    {
        // Current mic is 16 kHz mono by configuration; guard anyway.
        if (sampleRate != TestSampleRate || _testBuffer is null)
            return;

        var remaining = _testBuffer.Length - _testWriteIndex;
        if (remaining <= 0)
            return;

        var take = Math.Min(remaining, samples.Length);
        samples.Slice(0, take).CopyTo(_testBuffer.AsSpan(_testWriteIndex));
        _testWriteIndex += take;
    }

    private void UpdateTestState(float dt)
    {
        if (_testState == TestState.Recording)
        {
            _testElapsed += dt;
            if (_testWriteIndex >= TestDurationSamples || _testElapsed >= TestDurationSeconds)
            {
                DetachRecordHandler();
                PlayRecordedBuffer();
            }
        }
        else if (_testState == TestState.Playing)
        {
            if (_testPlayTask is not null && _testPlayTask.IsCompleted)
            {
                _testPlayTask = null;
                _testCts?.Dispose();
                _testCts = null;
                _testState = TestState.Idle;
            }
        }
    }

    private void PlayRecordedBuffer()
    {
        if (_testBuffer is null || _testWriteIndex == 0)
        {
            _testState = TestState.Idle;
            return;
        }

        _testState = TestState.Playing;
        _testCts = new CancellationTokenSource();
        var memory = _testBuffer.AsMemory(0, _testWriteIndex);
        _testPlayTask = _oneShotPlayer.PlayAsync(memory, TestSampleRate, _testCts.Token);
    }

    private void DetachRecordHandler()
    {
        if (_testRecordHandler is not null)
        {
            _microphone.SamplesAvailable -= _testRecordHandler;
            _testRecordHandler = null;
        }
    }

    private void StopTest()
    {
        DetachRecordHandler();
        if (_testCts is not null)
        {
            _testCts.Cancel();
            _testCts.Dispose();
            _testCts = null;
        }
        _testPlayTask = null;
        _testState = TestState.Idle;
    }
}
