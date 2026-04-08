using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.Live2D.App;
using PersonaEngine.Lib.TTS.Synthesis.LipSync;

namespace PersonaEngine.Lib.Live2D.Behaviour.LipSync;

/// <summary>
///     Reads pre-computed <see cref="LipSyncTimeline" /> from audio chunk events and
///     drives Live2D parameters each frame via smoothed interpolation.
/// </summary>
public sealed class LipSyncAnimationService : ILive2DAnimationService
{
    #region Configuration Constants

    private const float SmoothingFactor = 35.0f;

    private const float NeutralReturnFactor = 15.0f;

    private const float CheekPuffDecayFactor = 80.0f;

    #endregion

    #region Parameter Names

    private static readonly string ParamMouthOpenY = "ParamMouthOpenY";

    private static readonly string ParamJawOpen = "ParamJawOpen";

    private static readonly string ParamMouthForm = "ParamMouthForm";

    private static readonly string ParamMouthShrug = "ParamMouthShrug";

    private static readonly string ParamMouthFunnel = "ParamMouthFunnel";

    private static readonly string ParamMouthPuckerWiden = "ParamMouthPuckerWiden";

    private static readonly string ParamMouthPressLipOpen = "ParamMouthPressLipOpen";

    private static readonly string ParamMouthX = "ParamMouthX";

    private static readonly string ParamCheekPuffC = "ParamCheekPuffC";

    private static readonly string ParamEyeLOpen = "ParamEyeLOpen";

    private static readonly string ParamEyeROpen = "ParamEyeROpen";

    private static readonly string ParamBrowLY = "ParamBrowLY";

    private static readonly string ParamBrowRY = "ParamBrowRY";

    #endregion

    #region Dependencies and State

    private readonly ILogger<LipSyncAnimationService> _logger;

    private readonly IAudioProgressNotifier _audioProgressNotifier;

    private LAppModel? _model;

    private bool _isStarted;

    private bool _isPlaying;

    private bool _disposed;

    private LipSyncTimeline? _activeTimeline;

    private Guid _currentSentenceId;

    private double _cumulativeTimeOffset;

    private double _currentPlaybackTime;

    private LipSyncFrame _currentValues;

    #endregion

    public LipSyncAnimationService(
        ILogger<LipSyncAnimationService> logger,
        IAudioProgressNotifier audioProgressNotifier
    )
    {
        _logger = logger;
        _audioProgressNotifier = audioProgressNotifier;

        _audioProgressNotifier.ChunkPlaybackStarted += HandleChunkStarted;
        _audioProgressNotifier.ChunkPlaybackEnded += HandleChunkEnded;
        _audioProgressNotifier.PlaybackProgress += HandleProgress;
    }

    #region ILive2DAnimationService Implementation

    public void Start(LAppModel model)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _model = model;
        _isStarted = true;

        _logger.LogInformation("Started LipSyncAnimationService.");
    }

    public void Stop()
    {
        _isStarted = false;
        ResetState();
        _logger.LogInformation("Stopped LipSyncAnimationService.");
    }

    public void Update(float deltaTime)
    {
        if (deltaTime <= 0.0f || _disposed || !_isStarted || _model == null)
        {
            return;
        }

        if (!_isPlaying || _activeTimeline == null)
        {
            SmoothToNeutral(deltaTime);
            return;
        }

        var effectiveTime = _cumulativeTimeOffset + _currentPlaybackTime;
        var target = _activeTimeline.GetFrameAtTime(effectiveTime);

        SmoothToTarget(target, deltaTime);
        ApplyToModel();
    }

    #endregion

    #region Smoothing

    private void SmoothToTarget(in LipSyncFrame target, float deltaTime)
    {
        var sf = SmoothingFactor * deltaTime;

        _currentValues.MouthOpenY = Lerp(_currentValues.MouthOpenY, target.MouthOpenY, sf);
        _currentValues.JawOpen = Lerp(_currentValues.JawOpen, target.JawOpen, sf);
        _currentValues.MouthForm = Lerp(_currentValues.MouthForm, target.MouthForm, sf);
        _currentValues.MouthShrug = Lerp(_currentValues.MouthShrug, target.MouthShrug, sf);
        _currentValues.MouthFunnel = Lerp(_currentValues.MouthFunnel, target.MouthFunnel, sf);
        _currentValues.MouthPuckerWiden = Lerp(
            _currentValues.MouthPuckerWiden,
            target.MouthPuckerWiden,
            sf
        );
        _currentValues.MouthPressLipOpen = Lerp(
            _currentValues.MouthPressLipOpen,
            target.MouthPressLipOpen,
            sf
        );
        _currentValues.MouthX = Lerp(_currentValues.MouthX, target.MouthX, sf);

        if (target.CheekPuffC > 0.02f)
        {
            _currentValues.CheekPuffC = Lerp(_currentValues.CheekPuffC, target.CheekPuffC, sf);
        }
        else
        {
            var decayFactor = CheekPuffDecayFactor * deltaTime;
            _currentValues.CheekPuffC = Lerp(_currentValues.CheekPuffC, 0.0f, decayFactor);
        }

        // Optional eye/brow params
        if (target.EyeLOpen.HasValue)
        {
            _currentValues.EyeLOpen = Lerp(
                _currentValues.EyeLOpen ?? 1.0f,
                target.EyeLOpen.Value,
                sf
            );
        }

        if (target.EyeROpen.HasValue)
        {
            _currentValues.EyeROpen = Lerp(
                _currentValues.EyeROpen ?? 1.0f,
                target.EyeROpen.Value,
                sf
            );
        }

        if (target.BrowLY.HasValue)
        {
            _currentValues.BrowLY = Lerp(_currentValues.BrowLY ?? 0.0f, target.BrowLY.Value, sf);
        }

        if (target.BrowRY.HasValue)
        {
            _currentValues.BrowRY = Lerp(_currentValues.BrowRY ?? 0.0f, target.BrowRY.Value, sf);
        }
    }

    private void SmoothToNeutral(float deltaTime)
    {
        var sf = NeutralReturnFactor * deltaTime;

        _currentValues.MouthOpenY = Lerp(_currentValues.MouthOpenY, 0.0f, sf);
        _currentValues.JawOpen = Lerp(_currentValues.JawOpen, 0.0f, sf);
        _currentValues.MouthForm = Lerp(_currentValues.MouthForm, 0.0f, sf);
        _currentValues.MouthShrug = Lerp(_currentValues.MouthShrug, 0.0f, sf);
        _currentValues.MouthFunnel = Lerp(_currentValues.MouthFunnel, 0.0f, sf);
        _currentValues.MouthPuckerWiden = Lerp(_currentValues.MouthPuckerWiden, 0.0f, sf);
        _currentValues.MouthPressLipOpen = Lerp(_currentValues.MouthPressLipOpen, 0.0f, sf);
        _currentValues.MouthX = Lerp(_currentValues.MouthX, 0.0f, sf);

        var decayFactor = CheekPuffDecayFactor * deltaTime;
        _currentValues.CheekPuffC = Lerp(_currentValues.CheekPuffC, 0.0f, decayFactor);

        ApplyToModel();
    }

    #endregion

    #region Model Application

    private void ApplyToModel()
    {
        if (_model == null)
        {
            return;
        }

        try
        {
            var cubismModel = _model.Model;
            cubismModel.SetParameterValue(ParamMouthOpenY, _currentValues.MouthOpenY);
            cubismModel.SetParameterValue(ParamJawOpen, _currentValues.JawOpen);
            cubismModel.SetParameterValue(ParamMouthForm, _currentValues.MouthForm);
            cubismModel.SetParameterValue(ParamMouthShrug, _currentValues.MouthShrug);
            cubismModel.SetParameterValue(ParamMouthFunnel, _currentValues.MouthFunnel);
            cubismModel.SetParameterValue(ParamMouthPuckerWiden, _currentValues.MouthPuckerWiden);
            cubismModel.SetParameterValue(ParamMouthPressLipOpen, _currentValues.MouthPressLipOpen);
            cubismModel.SetParameterValue(ParamMouthX, _currentValues.MouthX);
            cubismModel.SetParameterValue(ParamCheekPuffC, _currentValues.CheekPuffC);

            if (_currentValues.EyeLOpen.HasValue)
            {
                cubismModel.SetParameterValue(ParamEyeLOpen, _currentValues.EyeLOpen.Value);
            }

            if (_currentValues.EyeROpen.HasValue)
            {
                cubismModel.SetParameterValue(ParamEyeROpen, _currentValues.EyeROpen.Value);
            }

            if (_currentValues.BrowLY.HasValue)
            {
                cubismModel.SetParameterValue(ParamBrowLY, _currentValues.BrowLY.Value);
            }

            if (_currentValues.BrowRY.HasValue)
            {
                cubismModel.SetParameterValue(ParamBrowRY, _currentValues.BrowRY.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying Live2D parameters");
        }
    }

    #endregion

    #region Event Handlers

    private void HandleChunkStarted(object? sender, AudioChunkPlaybackStartedEvent e)
    {
        if (!_isStarted)
        {
            return;
        }

        var chunk = e.Chunk;
        var isNewSentence = chunk.SentenceId != _currentSentenceId;

        if (isNewSentence)
        {
            _logger.LogTrace("New sentence started: {SentenceId}", chunk.SentenceId);
            _currentSentenceId = chunk.SentenceId;
            _cumulativeTimeOffset = 0.0;
            _activeTimeline = null;
        }

        _activeTimeline = chunk.LipSync;
        _isPlaying = true;
    }

    private void HandleChunkEnded(object? sender, AudioChunkPlaybackEndedEvent e)
    {
        if (!_isStarted)
        {
            return;
        }

        if (e.Chunk.SentenceId == _currentSentenceId)
        {
            _cumulativeTimeOffset += e.Chunk.DurationInSeconds;
        }
    }

    private void HandleProgress(object? sender, AudioPlaybackProgressEvent e)
    {
        if (!_isStarted || !_isPlaying)
        {
            return;
        }

        _currentPlaybackTime = e.CurrentPlaybackTime.TotalSeconds;
    }

    #endregion

    #region State Management

    private void ResetState()
    {
        _activeTimeline = null;
        _isPlaying = false;
        _currentSentenceId = Guid.Empty;
        _cumulativeTimeOffset = 0.0;
        _currentPlaybackTime = 0.0;
        _currentValues = LipSyncFrame.Neutral;
        _logger.LogTrace("LipSyncAnimationService state reset.");
    }

    #endregion

    #region Helpers

    private static float Lerp(float a, float b, float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        return a + (b - a) * t;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("Disposing LipSyncAnimationService...");
        Stop();

        _audioProgressNotifier.ChunkPlaybackStarted -= HandleChunkStarted;
        _audioProgressNotifier.ChunkPlaybackEnded -= HandleChunkEnded;
        _audioProgressNotifier.PlaybackProgress -= HandleProgress;

        _disposed = true;
        _logger.LogInformation("LipSyncAnimationService disposed.");
    }

    #endregion
}
