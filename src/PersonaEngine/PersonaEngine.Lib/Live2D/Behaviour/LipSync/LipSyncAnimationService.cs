using System.Threading;
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

    private const float NeutralReturnFactor = 15.0f;

    #endregion

    #region Parameter Names

    private const string ParamMouthOpenY = "ParamMouthOpenY";

    private const string ParamJawOpen = "ParamJawOpen";

    private const string ParamMouthForm = "ParamMouthForm";

    private const string ParamMouthShrug = "ParamMouthShrug";

    private const string ParamMouthFunnel = "ParamMouthFunnel";

    private const string ParamMouthPuckerWiden = "ParamMouthPuckerWiden";

    private const string ParamMouthPressLipOpen = "ParamMouthPressLipOpen";

    private const string ParamMouthX = "ParamMouthX";

    private const string ParamCheekPuffC = "ParamCheekPuffC";

    private const string ParamCheekPuffL = "ParamCheekPuffL";

    private const string ParamCheekPuffR = "ParamCheekPuffR";

    private const string ParamMouthOpenYRaw = "ParamMouthOpenY_Raw";

    private const string ParamMouthFormHalfPos = "ParamMouthForm_HalfPos";

    private const string ParamEyeLOpen = "ParamEyeLOpen";

    private const string ParamEyeROpen = "ParamEyeROpen";

    private const string ParamEyeLSmile = "ParamEyeLSmile";

    private const string ParamEyeRSmile = "ParamEyeRSmile";

    private const string ParamEyeLSquint = "ParamEyeLSquint";

    private const string ParamEyeRSquint = "ParamEyeRSquint";

    private const string ParamBrowLY = "ParamBrowLY";

    private const string ParamBrowRY = "ParamBrowRY";

    private const string ParamBrowInnerUpC = "ParamBrowInnerUpC";

    private const string ParamBrowInnerUpL = "ParamBrowInnerUpL";

    private const string ParamBrowInnerUpR = "ParamBrowInnerUpR";

    private const string ParamCheek = "ParamCheek";

    private const string ParamEyeBallX = "ParamEyeBallX";

    private const string ParamEyeBallY = "ParamEyeBallY";

    #endregion

    #region Dependencies and State

    private readonly ILogger<LipSyncAnimationService> _logger;

    private readonly IAudioProgressNotifier _audioProgressNotifier;

    private LAppModel? _model;

    private bool _isStarted;

    private volatile bool _isPlaying;

    private bool _disposed;

    private LipSyncTimeline? _activeTimeline;

    private Guid _currentSentenceId;

    private long _currentPlaybackTimeTicks;

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

        var timeline = Volatile.Read(ref _activeTimeline);

        if (!_isPlaying || timeline == null)
        {
            SmoothToNeutral(deltaTime);
            return;
        }

        var ticks = Interlocked.Read(ref _currentPlaybackTimeTicks);
        var effectiveTime = new TimeSpan(ticks).TotalSeconds;
        var target = timeline.GetFrameAtTime(effectiveTime);

        // Apply target values directly — the timeline already provides
        // interpolated frames (GetFrameAtTime lerps between keyframes)
        // and the processor's ParamSmoother already applied per-parameter
        // EMA.  Adding consumer-side smoothing on top causes visible lag.
        _currentValues = target;
        ApplyToModel();
    }

    #endregion

    #region Smoothing

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
        _currentValues.CheekPuffC = Lerp(_currentValues.CheekPuffC, 0.0f, sf);

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

            // Derived: duplicate cheek puff to L/R, mouth variants
            cubismModel.SetParameterValue(ParamCheekPuffL, _currentValues.CheekPuffC);
            cubismModel.SetParameterValue(ParamCheekPuffR, _currentValues.CheekPuffC);
            cubismModel.SetParameterValue(ParamMouthOpenYRaw, _currentValues.MouthOpenY);
            cubismModel.SetParameterValue(
                ParamMouthFormHalfPos,
                Math.Max(0f, _currentValues.MouthForm)
            );

            if (_currentValues.EyeLOpen.HasValue)
            {
                cubismModel.SetParameterValue(ParamEyeLOpen, _currentValues.EyeLOpen.Value);
            }

            if (_currentValues.EyeROpen.HasValue)
            {
                cubismModel.SetParameterValue(ParamEyeROpen, _currentValues.EyeROpen.Value);
            }

            if (_currentValues.EyeSquintL.HasValue)
            {
                cubismModel.SetParameterValue(ParamEyeLSmile, _currentValues.EyeSquintL.Value);
                cubismModel.SetParameterValue(ParamEyeLSquint, _currentValues.EyeSquintL.Value);
            }

            if (_currentValues.EyeSquintR.HasValue)
            {
                cubismModel.SetParameterValue(ParamEyeRSmile, _currentValues.EyeSquintR.Value);
                cubismModel.SetParameterValue(ParamEyeRSquint, _currentValues.EyeSquintR.Value);
            }

            if (_currentValues.BrowLY.HasValue)
            {
                cubismModel.SetParameterValue(ParamBrowLY, _currentValues.BrowLY.Value);
            }

            if (_currentValues.BrowRY.HasValue)
            {
                cubismModel.SetParameterValue(ParamBrowRY, _currentValues.BrowRY.Value);
            }

            if (_currentValues.BrowInnerUp.HasValue)
            {
                cubismModel.SetParameterValue(ParamBrowInnerUpC, _currentValues.BrowInnerUp.Value);
                cubismModel.SetParameterValue(ParamBrowInnerUpL, _currentValues.BrowInnerUp.Value);
                cubismModel.SetParameterValue(ParamBrowInnerUpR, _currentValues.BrowInnerUp.Value);
            }

            if (_currentValues.Cheek.HasValue)
            {
                cubismModel.SetParameterValue(ParamCheek, _currentValues.Cheek.Value);
            }

            if (_currentValues.EyeBallX.HasValue)
            {
                cubismModel.SetParameterValue(ParamEyeBallX, _currentValues.EyeBallX.Value);
            }

            if (_currentValues.EyeBallY.HasValue)
            {
                cubismModel.SetParameterValue(ParamEyeBallY, _currentValues.EyeBallY.Value);
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
            Volatile.Write(ref _activeTimeline, null);
        }

        Volatile.Write(ref _activeTimeline, chunk.LipSync);
        _isPlaying = true;
    }

    private void HandleProgress(object? sender, AudioPlaybackProgressEvent e)
    {
        if (!_isStarted || !_isPlaying)
        {
            return;
        }

        Interlocked.Exchange(ref _currentPlaybackTimeTicks, e.CurrentPlaybackTime.Ticks);
    }

    #endregion

    #region State Management

    private void ResetState()
    {
        Volatile.Write(ref _activeTimeline, null);
        _isPlaying = false;
        _currentSentenceId = Guid.Empty;
        Interlocked.Exchange(ref _currentPlaybackTimeTicks, 0L);
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
        _audioProgressNotifier.PlaybackProgress -= HandleProgress;

        _disposed = true;
        _logger.LogInformation("LipSyncAnimationService disposed.");
    }

    #endregion
}
