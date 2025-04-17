using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.Live2D.App;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Live2D.Behaviour.LipSync;

/// <summary>
///     LipSync service for VBridger parameter conventions.
/// </summary>
public sealed class VBridgerLipSyncService : ILive2DAnimationService
{
    public VBridgerLipSyncService(ILogger<VBridgerLipSyncService> logger, IAudioProgressNotifier audioProgressNotifier)
    {
        _logger                = logger;
        _audioProgressNotifier = audioProgressNotifier;
        _phonemeMap            = InitializeMisakiPhonemeMap();

        SubscribeToAudioProgressNotifier();
    }

    private void InitializeCurrentParameters()
    {
        if ( _model == null )
        {
            return;
        }

        var cubismModel = _model.Model;

        // Fetch initial values from the model
        _currentParameterValues[ParamMouthOpenY]        = cubismModel.GetParameterValue(ParamMouthOpenY);
        _currentParameterValues[ParamJawOpen]           = cubismModel.GetParameterValue(ParamJawOpen);
        _currentParameterValues[ParamMouthForm]         = cubismModel.GetParameterValue(ParamMouthForm);
        _currentParameterValues[ParamMouthShrug]        = cubismModel.GetParameterValue(ParamMouthShrug);
        _currentParameterValues[ParamMouthFunnel]       = cubismModel.GetParameterValue(ParamMouthFunnel);
        _currentParameterValues[ParamMouthPuckerWiden]  = cubismModel.GetParameterValue(ParamMouthPuckerWiden);
        _currentParameterValues[ParamMouthPressLipOpen] = cubismModel.GetParameterValue(ParamMouthPressLipOpen);
        _currentParameterValues[ParamMouthX]            = cubismModel.GetParameterValue(ParamMouthX);
        _currentParameterValues[ParamCheekPuffC]        = 0f;
    }

    #region Configuration Constants

    private const float SMOOTHING_FACTOR = 35.0f; // How quickly parameters move towards target (higher = faster)

    private const float NEUTRAL_RETURN_FACTOR = 15.0f; // How quickly parameters return to neutral when idle

    private const float CHEEK_PUFF_DECAY_FACTOR = 80.0f; // How quickly CheekPuff returns to 0

    private const float NEUTRAL_THRESHOLD = 0.02f; // Threshold for considering a value as neutral

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

    #endregion

    #region Dependencies and State

    private LAppModel? _model;

    private readonly IAudioProgressNotifier _audioProgressNotifier;

    private readonly List<TimedPhoneme> _activePhonemes = new();

    private int _currentPhonemeIndex = -1;

    private bool _isPlaying = false;

    private bool _isStarted = false;

    private bool _disposed = false;

    private PhonemePose _currentTargetPose = PhonemePose.Neutral;

    private PhonemePose _nextTargetPose = PhonemePose.Neutral;

    private float _interpolationT = 0f;

    private readonly Dictionary<string, float> _currentParameterValues = new();

    private readonly Dictionary<string, PhonemePose> _phonemeMap;

    private readonly HashSet<char> _phonemeShapeIgnoreChars = ['ˈ', 'ˌ', 'ː']; // Ignore stress/length marks

    private readonly ILogger<VBridgerLipSyncService> _logger;

    #endregion

    #region ILipSyncService Implementation

    private void SubscribeToAudioProgressNotifier()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UnsubscribeFromCurrentNotifier();

        _audioProgressNotifier.ChunkPlaybackStarted += HandleChunkStarted;
        _audioProgressNotifier.ChunkPlaybackEnded   += HandleChunkEnded;
        _audioProgressNotifier.PlaybackProgress     += HandleProgress;
    }

    private void UnsubscribeFromCurrentNotifier()
    {
        _audioProgressNotifier.ChunkPlaybackStarted -= HandleChunkStarted;
        _audioProgressNotifier.ChunkPlaybackEnded   -= HandleChunkEnded;
        _audioProgressNotifier.PlaybackProgress     -= HandleProgress;

        ResetState();
    }

    public void Start(LAppModel model)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _model = model;

        InitializeCurrentParameters();

        _isStarted = true;

        _logger.LogInformation("Started lip syncing.");
    }

    public void Stop()
    {
        _isStarted = false;
        ResetState();
        _logger.LogInformation("Stopped lip syncing.");
    }

    #endregion

    #region Update Logic

    public void Update(float deltaTime)
    {
        if ( deltaTime <= 0.0f || _disposed || !_isStarted || _model == null )
        {
            return;
        }

        if ( !_isPlaying )
        {
            SmoothParametersToNeutral(deltaTime);

            return;
        }

        var easedT          = EaseInOutQuad(_interpolationT);
        var frameTargetPose = PhonemePose.Lerp(_currentTargetPose, _nextTargetPose, easedT);
        SmoothParametersToTarget(frameTargetPose, deltaTime);
        ApplySmoothedParameters();
    }

    private void UpdateTargetPoses(float currentTime)
    {
        if ( _model == null || !_isPlaying || _activePhonemes.Count == 0 )
        {
            if ( _currentTargetPose != PhonemePose.Neutral || _nextTargetPose != PhonemePose.Neutral )
            {
                _currentTargetPose = GetPoseFromCurrentValues();
                _nextTargetPose    = PhonemePose.Neutral;
                _interpolationT    = 0f;
                _logger.LogTrace("Targeting Neutral Pose.");
            }

            _currentPhonemeIndex = -1;

            return;
        }

        var foundIndex = FindPhonemeIndexAtTime(currentTime);

        if ( foundIndex != -1 )
        {
            if ( foundIndex != _currentPhonemeIndex )
            {
                _logger.LogTrace("Phoneme changed: {PhonemeIndex} -> {FoundIndex} ({Phoneme}) at T={CurrentTime:F3}", _currentPhonemeIndex, foundIndex, _activePhonemes[foundIndex].Phoneme, currentTime);

                if ( _currentPhonemeIndex == -1 )
                {
                    _currentTargetPose = GetPoseFromCurrentValues();
                }
                else
                {
                    _currentTargetPose = GetPoseForPhoneme(_activePhonemes[_currentPhonemeIndex].Phoneme);
                }

                _nextTargetPose      = GetPoseForPhoneme(_activePhonemes[foundIndex].Phoneme);
                _currentPhonemeIndex = foundIndex;
            }

            var currentPh = _activePhonemes[_currentPhonemeIndex];
            var duration  = currentPh.EndTime - currentPh.StartTime;
            _interpolationT = duration > 0.001
                                  ? (float)Math.Clamp((currentTime - currentPh.StartTime) / duration, 0.0, 1.0)
                                  : 1.0f;
        }
        else
        {
            if ( _currentTargetPose != PhonemePose.Neutral || _nextTargetPose != PhonemePose.Neutral )
            {
                _currentTargetPose = GetPoseFromCurrentValues();
                _nextTargetPose    = PhonemePose.Neutral;
                _interpolationT    = 0f;
                _logger.LogTrace("Targeting Neutral Pose (Gap/End).");
            }

            if ( _activePhonemes.Count > 0 &&
                 (currentTime < _activePhonemes.First().StartTime - 0.1 ||
                  currentTime > _activePhonemes.Last().EndTime + 0.1) )
            {
                _currentPhonemeIndex = -1;
            }
        }
    }

    private int FindPhonemeIndexAtTime(float currentTime)
    {
        if ( _currentPhonemeIndex >= 0 && _currentPhonemeIndex < _activePhonemes.Count )
        {
            var ph = _activePhonemes[_currentPhonemeIndex];
            if ( currentTime >= ph.StartTime && currentTime < ph.EndTime + 0.001 )
            {
                return _currentPhonemeIndex;
            }
        }

        var searchStartIndex = Math.Max(0, _currentPhonemeIndex - 1);
        for ( var i = searchStartIndex; i < _activePhonemes.Count; i++ )
        {
            var ph = _activePhonemes[i];
            if ( currentTime >= ph.StartTime && currentTime < ph.EndTime + 0.001 )
            {
                return i;
            }
        }

        if ( _activePhonemes.Count > 0 )
        {
            if ( currentTime < _activePhonemes[0].StartTime )
            {
                return -1;
            }

            if ( Math.Abs(currentTime - _activePhonemes.Last().EndTime) < 0.01 )
            {
                return _activePhonemes.Count - 1;
            }
        }

        return -1;
    }

    private PhonemePose GetPoseFromCurrentValues()
    {
        return new PhonemePose(
                               _currentParameterValues[ParamMouthOpenY],
                               _currentParameterValues[ParamJawOpen],
                               _currentParameterValues[ParamMouthForm],
                               _currentParameterValues[ParamMouthShrug],
                               _currentParameterValues[ParamMouthFunnel],
                               _currentParameterValues[ParamMouthPuckerWiden],
                               _currentParameterValues[ParamMouthPressLipOpen],
                               _currentParameterValues[ParamMouthX],
                               _currentParameterValues[ParamCheekPuffC]
                              );
    }

    #endregion

    #region Parameter Smoothing

    private void SmoothParametersToTarget(PhonemePose targetPose, float deltaTime)
    {
        var smoothFactor = SMOOTHING_FACTOR * deltaTime;

        _currentParameterValues[ParamMouthOpenY]        = Lerp(_currentParameterValues[ParamMouthOpenY], targetPose.MouthOpenY, smoothFactor);
        _currentParameterValues[ParamJawOpen]           = Lerp(_currentParameterValues[ParamJawOpen], targetPose.JawOpen, smoothFactor);
        _currentParameterValues[ParamMouthForm]         = Lerp(_currentParameterValues[ParamMouthForm], targetPose.MouthForm, smoothFactor);
        _currentParameterValues[ParamMouthShrug]        = Lerp(_currentParameterValues[ParamMouthShrug], targetPose.MouthShrug, smoothFactor);
        _currentParameterValues[ParamMouthFunnel]       = Lerp(_currentParameterValues[ParamMouthFunnel], targetPose.MouthFunnel, smoothFactor);
        _currentParameterValues[ParamMouthPuckerWiden]  = Lerp(_currentParameterValues[ParamMouthPuckerWiden], targetPose.MouthPuckerWiden, smoothFactor);
        _currentParameterValues[ParamMouthPressLipOpen] = Lerp(_currentParameterValues[ParamMouthPressLipOpen], targetPose.MouthPressLipOpen, smoothFactor);
        _currentParameterValues[ParamMouthX]            = Lerp(_currentParameterValues[ParamMouthX], targetPose.MouthX, smoothFactor);

        if ( targetPose.CheekPuffC > NEUTRAL_THRESHOLD )
        {
            _currentParameterValues[ParamCheekPuffC] = Lerp(_currentParameterValues[ParamCheekPuffC], targetPose.CheekPuffC, smoothFactor);
        }
        else
        {
            var decayFactor = CHEEK_PUFF_DECAY_FACTOR * deltaTime;
            _currentParameterValues[ParamCheekPuffC] = Lerp(_currentParameterValues[ParamCheekPuffC], 0.0f, decayFactor);
        }
    }

    private void SmoothParametersToNeutral(float deltaTime)
    {
        var smoothFactor = NEUTRAL_RETURN_FACTOR * deltaTime;
        var neutral      = PhonemePose.Neutral;

        _currentParameterValues[ParamMouthOpenY]        = Lerp(_currentParameterValues[ParamMouthOpenY], neutral.MouthOpenY, smoothFactor);
        _currentParameterValues[ParamJawOpen]           = Lerp(_currentParameterValues[ParamJawOpen], neutral.JawOpen, smoothFactor);
        _currentParameterValues[ParamMouthForm]         = Lerp(_currentParameterValues[ParamMouthForm], neutral.MouthForm, smoothFactor);
        _currentParameterValues[ParamMouthShrug]        = Lerp(_currentParameterValues[ParamMouthShrug], neutral.MouthShrug, smoothFactor);
        _currentParameterValues[ParamMouthFunnel]       = Lerp(_currentParameterValues[ParamMouthFunnel], neutral.MouthFunnel, smoothFactor);
        _currentParameterValues[ParamMouthPuckerWiden]  = Lerp(_currentParameterValues[ParamMouthPuckerWiden], neutral.MouthPuckerWiden, smoothFactor);
        _currentParameterValues[ParamMouthPressLipOpen] = Lerp(_currentParameterValues[ParamMouthPressLipOpen], neutral.MouthPressLipOpen, smoothFactor);
        _currentParameterValues[ParamMouthX]            = Lerp(_currentParameterValues[ParamMouthX], neutral.MouthX, smoothFactor);

        var decayFactor = CHEEK_PUFF_DECAY_FACTOR * deltaTime;
        _currentParameterValues[ParamCheekPuffC] = Lerp(_currentParameterValues[ParamCheekPuffC], 0.0f, decayFactor);

        ApplySmoothedParameters();
    }

    private bool IsApproximatelyNeutral()
    {
        var neutral = PhonemePose.Neutral;

        return Math.Abs(_currentParameterValues[ParamMouthOpenY] - neutral.MouthOpenY) < NEUTRAL_THRESHOLD &&
               Math.Abs(_currentParameterValues[ParamJawOpen] - neutral.JawOpen) < NEUTRAL_THRESHOLD &&
               Math.Abs(_currentParameterValues[ParamMouthForm] - neutral.MouthForm) < NEUTRAL_THRESHOLD &&
               Math.Abs(_currentParameterValues[ParamMouthShrug] - neutral.MouthShrug) < NEUTRAL_THRESHOLD &&
               Math.Abs(_currentParameterValues[ParamMouthFunnel] - neutral.MouthFunnel) < NEUTRAL_THRESHOLD &&
               Math.Abs(_currentParameterValues[ParamMouthPuckerWiden] - neutral.MouthPuckerWiden) < NEUTRAL_THRESHOLD &&
               Math.Abs(_currentParameterValues[ParamMouthPressLipOpen] - neutral.MouthPressLipOpen) < NEUTRAL_THRESHOLD &&
               Math.Abs(_currentParameterValues[ParamMouthX] - neutral.MouthX) < NEUTRAL_THRESHOLD &&
               Math.Abs(_currentParameterValues[ParamCheekPuffC] - neutral.CheekPuffC) < NEUTRAL_THRESHOLD;
    }

    private void ApplySmoothedParameters()
    {
        if ( _model == null )
        {
            _logger.LogWarning("Attempted to apply parameters but model is null.");

            return;
        }

        try
        {
            var cubismModel = _model.Model;
            cubismModel.SetParameterValue(ParamMouthOpenY, _currentParameterValues[ParamMouthOpenY]);
            cubismModel.SetParameterValue(ParamJawOpen, _currentParameterValues[ParamJawOpen]);
            cubismModel.SetParameterValue(ParamMouthForm, _currentParameterValues[ParamMouthForm]);
            cubismModel.SetParameterValue(ParamMouthShrug, _currentParameterValues[ParamMouthShrug]);
            cubismModel.SetParameterValue(ParamMouthFunnel, _currentParameterValues[ParamMouthFunnel]);
            cubismModel.SetParameterValue(ParamMouthPuckerWiden, _currentParameterValues[ParamMouthPuckerWiden]);
            cubismModel.SetParameterValue(ParamMouthPressLipOpen, _currentParameterValues[ParamMouthPressLipOpen]);
            cubismModel.SetParameterValue(ParamMouthX, _currentParameterValues[ParamMouthX]);
            cubismModel.SetParameterValue(ParamCheekPuffC, _currentParameterValues[ParamCheekPuffC]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying Live2D parameters");
        }
    }

    #endregion

    #region Event Handlers and State Management

    private void HandleChunkStarted(object? sender, AudioChunkPlaybackStartedEvent e)
    {
        if ( !_isStarted )
        {
            return;
        }

        _logger.LogTrace("Audio Chunk Playback Started.");
        ProcessAudioSegment(e.Chunk);
        _isPlaying           = true;
        _currentPhonemeIndex = -1;

        var initialTime = _activePhonemes.Count != 0 ? (float)_activePhonemes.First().StartTime : 0f;
        UpdateTargetPoses(initialTime);
    }

    private void HandleChunkEnded(object? sender, AudioChunkPlaybackEndedEvent e)
    {
        if ( !_isStarted )
        {
            return;
        }

        _logger.LogTrace("Audio Chunk Playback Ended.");
        _isPlaying = false;
    }

    private void HandleProgress(object? sender, AudioPlaybackProgressEvent e)
    {
        if ( !_isStarted || !_isPlaying )
        {
            return;
        }

        UpdateTargetPoses((float)e.CurrentPlaybackTime.TotalSeconds);
    }

    private void ResetState()
    {
        _activePhonemes.Clear();
        _currentPhonemeIndex = -1;
        _isPlaying           = false;
        _currentTargetPose   = PhonemePose.Neutral;
        _nextTargetPose      = PhonemePose.Neutral;
        _interpolationT      = 0f;
        InitializeCurrentParameters();
        _logger.LogTrace("LipSync state reset.");
    }

    #endregion

    #region Phoneme Processing

    private void ProcessAudioSegment(AudioSegment segment)
    {
        _activePhonemes.Clear();

        var lastEndTime = 0.0;

        foreach ( var token in segment.Tokens )
        {
            if ( string.IsNullOrEmpty(token.Phonemes) || !token.StartTs.HasValue || !token.EndTs.HasValue || token.EndTs.Value <= token.StartTs.Value )
            {
                _logger.LogTrace("Skipping invalid token: Phonemes='{Phonemes}', Start={StartTs}, End={EndTs}", token.Phonemes, token.StartTs, token.EndTs);

                continue;
            }

            var tokenStartTime = Math.Max(lastEndTime, token.StartTs.Value);
            var tokenEndTime   = Math.Max(tokenStartTime + 0.001, token.EndTs.Value);

            var phonemeChars = SplitPhonemes(token.Phonemes);
            if ( phonemeChars.Count == 0 )
            {
                _logger.LogTrace("No valid phoneme characters found in token: '{Phonemes}'", token.Phonemes);

                continue;
            }

            var tokenDuration           = tokenEndTime - tokenStartTime;
            var timePerPhoneme          = tokenDuration / phonemeChars.Count;
            var currentPhonemeStartTime = tokenStartTime;

            foreach ( var phChar in phonemeChars )
            {
                var phonemeEndTime = currentPhonemeStartTime + timePerPhoneme;
                _activePhonemes.Add(new TimedPhoneme { Phoneme = phChar, StartTime = currentPhonemeStartTime, EndTime = phonemeEndTime });
                currentPhonemeStartTime = phonemeEndTime;
            }

            lastEndTime = currentPhonemeStartTime;
        }

        _activePhonemes.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        _logger.LogDebug("Processed segment into {Count} timed phonemes.", _activePhonemes.Count);
    }

    private List<string> SplitPhonemes(string phonemeString)
    {
        var result = new List<string>();
        if ( string.IsNullOrEmpty(phonemeString) )
        {
            return result;
        }

        foreach ( var c in phonemeString )
        {
            if ( !_phonemeShapeIgnoreChars.Contains(c) )
            {
                result.Add(c.ToString());
            }
        }

        return result;
    }

    private PhonemePose GetPoseForPhoneme(string phoneme)
    {
        if ( _phonemeMap.TryGetValue(phoneme, out var pose) )
        {
            return pose;
        }

        _logger.LogDebug("Phoneme '{Phoneme}' not found in map. Returning Neutral.", phoneme);

        return PhonemePose.Neutral;
    }

    #endregion

    #region Helper Functions

    private static float EaseInOutQuad(float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);

        return t < 0.5f ? 2.0f * t * t : 1.0f - (float)Math.Pow(-2.0 * t + 2.0, 2.0) / 2.0f;
    }

    private static float Lerp(float a, float b, float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);

        return a + (b - a) * t;
    }

    #endregion

    #region Structs and Maps

    public struct PhonemePose : IEquatable<PhonemePose>
    {
        public float MouthOpenY; // 0-1: How open the mouth is

        public float JawOpen; // 0-1: How open the jaw is

        public float MouthForm; // -1 (Frown) to +1 (Smile): Vertical lip corner movement

        public float MouthShrug; // 0-1: Upward lip shrug/tension

        public float MouthFunnel; // 0-1: Funnel shape (lips forward/pursed)

        public float MouthPuckerWiden; // -1 (Wide) to +1 (Pucker): Controls mouth width

        public float MouthPressLipOpen; // -1 (Pressed Thin) to 0 (Touching) to +1 (Separated/Teeth)

        public float MouthX; // -1 to 1: Horizontal mouth shift

        public float CheekPuffC; // 0-1: Cheek puff amount

        public static PhonemePose Neutral = new();

        public PhonemePose(float openY = 0, float jawOpen = 0, float form = 0, float shrug = 0, float funnel = 0, float puckerWiden = 0, float pressLip = 0, float mouthX = 0, float cheekPuff = 0)
        {
            MouthOpenY        = openY;
            JawOpen           = jawOpen;
            MouthForm         = form;
            MouthShrug        = shrug;
            MouthFunnel       = funnel;
            MouthPuckerWiden  = puckerWiden;
            MouthPressLipOpen = pressLip;
            MouthX            = mouthX;
            CheekPuffC        = cheekPuff;
        }

        public static PhonemePose Lerp(PhonemePose a, PhonemePose b, float t)
        {
            t = Math.Clamp(t, 0.0f, 1.0f);

            return new PhonemePose(
                                   VBridgerLipSyncService.Lerp(a.MouthOpenY, b.MouthOpenY, t),
                                   VBridgerLipSyncService.Lerp(a.JawOpen, b.JawOpen, t),
                                   VBridgerLipSyncService.Lerp(a.MouthForm, b.MouthForm, t),
                                   VBridgerLipSyncService.Lerp(a.MouthShrug, b.MouthShrug, t),
                                   VBridgerLipSyncService.Lerp(a.MouthFunnel, b.MouthFunnel, t),
                                   VBridgerLipSyncService.Lerp(a.MouthPuckerWiden, b.MouthPuckerWiden, t),
                                   VBridgerLipSyncService.Lerp(a.MouthPressLipOpen, b.MouthPressLipOpen, t),
                                   VBridgerLipSyncService.Lerp(a.MouthX, b.MouthX, t),
                                   VBridgerLipSyncService.Lerp(a.CheekPuffC, b.CheekPuffC, t)
                                  );
        }

        public bool Equals(PhonemePose other)
        {
            const float tolerance = 0.001f;

            return Math.Abs(MouthOpenY - other.MouthOpenY) < tolerance &&
                   Math.Abs(JawOpen - other.JawOpen) < tolerance &&
                   Math.Abs(MouthForm - other.MouthForm) < tolerance &&
                   Math.Abs(MouthShrug - other.MouthShrug) < tolerance &&
                   Math.Abs(MouthFunnel - other.MouthFunnel) < tolerance &&
                   Math.Abs(MouthPuckerWiden - other.MouthPuckerWiden) < tolerance &&
                   Math.Abs(MouthPressLipOpen - other.MouthPressLipOpen) < tolerance &&
                   Math.Abs(MouthX - other.MouthX) < tolerance &&
                   Math.Abs(CheekPuffC - other.CheekPuffC) < tolerance;
        }

        public override bool Equals(object? obj) { return obj is PhonemePose other && Equals(other); }

        public override int GetHashCode() { return HashCode.Combine(HashCode.Combine(MouthOpenY, JawOpen, MouthForm, MouthShrug, MouthFunnel, MouthPuckerWiden, MouthPressLipOpen, MouthX), CheekPuffC); }

        public static bool operator ==(PhonemePose left, PhonemePose right) { return left.Equals(right); }

        public static bool operator !=(PhonemePose left, PhonemePose right) { return !(left == right); }
    }

    private struct TimedPhoneme
    {
        public string Phoneme;

        public double StartTime;

        public double EndTime;
    }

    private Dictionary<string, PhonemePose> InitializeMisakiPhonemeMap()
    {
        // Phoneme to pose mapping based on VBridger parameter definitions:
        // MouthForm: -1 (Frown) to +1 (Smile)
        // MouthPuckerWiden: -1 (Wide) to +1 (Pucker)
        // MouthPressLipOpen: -1 (Pressed Thin) to +1 (Separated/Teeth)

        var map = new Dictionary<string, PhonemePose>();

        // --- Neutral ---
        map.Add("SIL", PhonemePose.Neutral); // Neutral: open=0, jaw=0, form=0, shrug=0, funnel=0, puckerWiden=0, pressLip=0, puff=0

        // --- Shared IPA Consonants ---
        // Plosives (b, p, d, t, g, k) - Focus on closure and puff
        map.Add("b", new PhonemePose(pressLip: -1.0f, cheekPuff: 0.6f));              // Pressed lips, puff
        map.Add("p", new PhonemePose(pressLip: -1.0f, cheekPuff: 0.8f));              // Pressed lips, strong puff
        map.Add("d", new PhonemePose(0.05f, 0.05f, pressLip: 0.0f, cheekPuff: 0.2f)); // Slight open, lips touch/nearly touch, slight puff
        map.Add("t", new PhonemePose(0.05f, 0.05f, pressLip: 0.0f, cheekPuff: 0.3f)); // Slight open, lips touch/nearly touch, moderate puff
        map.Add("ɡ", new PhonemePose(0.1f, 0.15f, pressLip: 0.2f, cheekPuff: 0.5f));  // Back sound, slight open, slight separation, puff
        map.Add("k", new PhonemePose(0.1f, 0.15f, pressLip: 0.2f, cheekPuff: 0.4f));  // Back sound, slight open, slight separation, moderate puff

        // Fricatives (f, v, s, z, h, ʃ, ʒ, ð, θ) - Focus on partial closure/airflow shapes
        map.Add("f", new PhonemePose(0.05f, pressLip: -0.2f, form: -0.2f, puckerWiden: -0.1f));       // Lower lip near upper teeth: Slight press, slight frown, slightly wide
        map.Add("v", new PhonemePose(0.05f, pressLip: -0.1f, form: -0.2f, puckerWiden: -0.1f));       // Voiced 'f': Less press?
        map.Add("s", new PhonemePose(jawOpen: 0.0f, pressLip: 0.9f, form: 0.3f, puckerWiden: -0.6f)); // Teeth close/showing, slight smile, wide
        map.Add("z", new PhonemePose(jawOpen: 0.0f, pressLip: 0.8f, form: 0.2f, puckerWiden: -0.5f)); // Voiced 's': Slightly less extreme?
        map.Add("h", new PhonemePose(0.2f, 0.2f, pressLip: 0.5f));                                    // Breathy open, lips separated
        map.Add("ʃ", new PhonemePose(0.1f, funnel: 0.9f, puckerWiden: 0.6f, pressLip: 0.2f));         // 'sh': Funnel, puckered but flatter than 'oo', slight separation
        map.Add("ʒ", new PhonemePose(0.1f, funnel: 0.8f, puckerWiden: 0.5f, pressLip: 0.2f));         // 'zh': Similar to 'sh'
        map.Add("ð", new PhonemePose(0.05f, pressLip: 0.1f, puckerWiden: -0.2f));                     // Soft 'th': Tongue tip, lips nearly touching, slightly wide
        map.Add("θ", new PhonemePose(0.05f, pressLip: 0.2f, puckerWiden: -0.3f));                     // Hard 'th': More airflow? More separation/width?

        // Nasals (m, n, ŋ) - Focus on closure or near-closure
        map.Add("m", new PhonemePose(pressLip: -1.0f));              // Pressed lips
        map.Add("n", new PhonemePose(0.05f, 0.05f, pressLip: 0.0f)); // Like 'd' position, lips touching
        map.Add("ŋ", new PhonemePose(0.15f, 0.2f, pressLip: 0.4f));  // 'ng': Back tongue, mouth open more, lips separated

        // Liquids/Glides (l, ɹ, w, j) - Varied shapes
        map.Add("l", new PhonemePose(0.2f, 0.2f, puckerWiden: -0.3f, pressLip: 0.6f));                // Tongue tip visible: Slightly open, slightly wide, lips separated
        map.Add("ɹ", new PhonemePose(0.15f, 0.15f, funnel: 0.4f, puckerWiden: 0.2f, pressLip: 0.3f)); // 'r': Some funneling, slight pucker, separation
        map.Add("w", new PhonemePose(0.1f, 0.1f, funnel: 1.0f, puckerWiden: 0.9f, pressLip: -0.3f));  // Like 'u': Strong funnel, strong pucker, lips maybe slightly pressed
        map.Add("j", new PhonemePose(0.1f, 0.1f, 0.6f, 0.3f, puckerWiden: -0.8f, pressLip: 0.8f));    // 'y': Like 'i', smile, shrug, wide, teeth showing

        // --- Shared Consonant Clusters ---
        map.Add("ʤ", new PhonemePose(0.1f, funnel: 0.8f, puckerWiden: 0.5f, pressLip: 0.2f, cheekPuff: 0.3f)); // 'j'/'dg': Target 'ʒ' shape + puff
        map.Add("ʧ", new PhonemePose(0.1f, funnel: 0.9f, puckerWiden: 0.6f, pressLip: 0.2f, cheekPuff: 0.4f)); // 'ch': Target 'ʃ' shape + puff

        // --- Shared IPA Vowels ---
        map.Add("ə", new PhonemePose(0.3f, 0.3f, pressLip: 0.5f));                                     // Schwa: Neutral open, lips separated
        map.Add("i", new PhonemePose(0.1f, 0.1f, 0.7f, 0.4f, puckerWiden: -0.9f, pressLip: 0.9f));     // 'ee': Smile, shrug, wide, teeth showing
        map.Add("u", new PhonemePose(0.15f, 0.15f, funnel: 1.0f, puckerWiden: 1.0f, pressLip: -0.2f)); // 'oo': Funnel, puckered, slight press
        map.Add("ɑ", new PhonemePose(0.9f, 1.0f, pressLip: 0.8f));                                     // 'aa': Very open, lips separated
        map.Add("ɔ", new PhonemePose(0.6f, 0.7f, funnel: 0.5f, puckerWiden: 0.3f, pressLip: 0.7f));    // 'aw': Open, some funnel, slight pucker, separated
        map.Add("ɛ", new PhonemePose(0.5f, 0.5f, puckerWiden: -0.5f, pressLip: 0.7f));                 // 'eh': Mid open, somewhat wide, separated
        map.Add("ɜ", new PhonemePose(0.4f, 0.4f, pressLip: 0.6f));                                     // 'er': Mid open, neutral width, separated (blend with 'ɹ')
        map.Add("ɪ", new PhonemePose(0.2f, 0.2f, 0.2f, puckerWiden: -0.6f, pressLip: 0.8f));           // 'ih': Slight open, slight smile, wide, separated
        map.Add("ʊ", new PhonemePose(0.2f, 0.2f, funnel: 0.8f, puckerWiden: 0.7f, pressLip: 0.1f));    // 'uu': Slight open, funnel, pucker, near touch
        map.Add("ʌ", new PhonemePose(0.6f, 0.6f, pressLip: 0.7f));                                     // 'uh': Mid open, neutral width, separated

        // --- Shared Diphthong Vowels (Targeting the end-shape's characteristics) ---
        map.Add("A", new PhonemePose(0.3f, 0.3f, 0.4f, puckerWiden: -0.7f, pressLip: 0.8f));        // 'ay' (ends like ɪ/i): Mid-close, smile, wide, separated
        map.Add("I", new PhonemePose(0.4f, 0.4f, 0.3f, puckerWiden: -0.6f, pressLip: 0.8f));        // 'eye' (ends like ɪ/i): Mid-open, smile, wide, separated
        map.Add("W", new PhonemePose(0.3f, 0.3f, funnel: 0.9f, puckerWiden: 0.8f, pressLip: 0.0f)); // 'ow' (ends like ʊ/u): Mid-close, funnel, pucker, touching
        map.Add("Y", new PhonemePose(0.3f, 0.3f, 0.2f, puckerWiden: -0.5f, pressLip: 0.8f));        // 'oy' (ends like ɪ/i): Mid-close, smile, wide, separated

        // --- Shared Custom Vowel ---
        map.Add("ᵊ", new PhonemePose(0.1f, 0.1f, pressLip: 0.2f)); // Small schwa: Minimal open, slight separation

        // --- American-only ---
        map.Add("æ", new PhonemePose(0.7f, 0.7f, 0.3f, puckerWiden: -0.8f, pressLip: 0.9f));        // 'ae': Open, slight smile, wide, teeth showing
        map.Add("O", new PhonemePose(0.3f, 0.3f, funnel: 0.8f, puckerWiden: 0.6f, pressLip: 0.1f)); // US 'oh' (ends like ʊ/u): Mid-close, funnel, pucker, near touch
        map.Add("ᵻ", new PhonemePose(0.15f, 0.15f, puckerWiden: -0.2f, pressLip: 0.6f));            // Between ə/ɪ: Slightly open, neutral-wide, separated
        map.Add("ɾ", new PhonemePose(0.05f, 0.05f, pressLip: 0.3f));                                // Flap 't': Very quick, slight separation

        // --- British-only ---
        map.Add("a", new PhonemePose(0.7f, 0.7f, puckerWiden: -0.4f, pressLip: 0.8f));              // UK 'ash': Open, less wide than US 'æ', separated
        map.Add("Q", new PhonemePose(0.3f, 0.3f, funnel: 0.7f, puckerWiden: 0.5f, pressLip: 0.1f)); // UK 'oh' (ends like ʊ/u): Mid-close, funnel, pucker, near touch
        map.Add("ɒ", new PhonemePose(0.8f, 0.9f, funnel: 0.2f, puckerWiden: 0.1f, pressLip: 0.8f)); // 'on': Open, slight funnel, slight pucker, separated

        return map;
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose() { Dispose(true); }

    private void Dispose(bool disposing)
    {
        if ( _disposed )
        {
            return;
        }

        if ( disposing )
        {
            _logger.LogDebug("Disposing...");
            Stop();
            UnsubscribeFromCurrentNotifier();
            _activePhonemes.Clear();
            _currentParameterValues.Clear();
        }

        _disposed = true;
        _logger.LogInformation("Disposed");
    }

    #endregion
}