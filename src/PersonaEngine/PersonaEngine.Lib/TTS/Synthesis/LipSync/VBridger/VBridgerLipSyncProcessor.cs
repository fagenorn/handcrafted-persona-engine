using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.VBridger;

/// <summary>
///     Produces a <see cref="LipSyncTimeline" /> from phoneme timing data embedded in
///     <see cref="AudioSegment.Tokens" /> using a hardcoded IPA-to-pose lookup table.
///     No ONNX inference — purely a mapping from phoneme strings to mouth poses.
/// </summary>
public sealed class VBridgerLipSyncProcessor : ILipSyncProcessor
{
    private static readonly HashSet<char> StressMarks = ['ˈ', 'ˌ', 'ː'];

    private readonly Dictionary<string, PhonemePose> _phonemeMap;

    public VBridgerLipSyncProcessor()
    {
        _phonemeMap = InitializeMisakiPhonemeMap();
    }

    /// <inheritdoc />
    public LipSyncEngine EngineId => LipSyncEngine.VBridger;

    /// <inheritdoc />
    public LipSyncTimeline Process(AudioSegment segment)
    {
        var frames = new List<LipSyncFrame>();

        foreach (var token in segment.Tokens)
        {
            if (
                string.IsNullOrEmpty(token.Phonemes)
                || !token.StartTs.HasValue
                || !token.EndTs.HasValue
            )
            {
                continue;
            }

            var phonemes = SplitPhonemes(token.Phonemes);
            if (phonemes.Count == 0)
            {
                continue;
            }

            var startTime = token.StartTs.Value;
            var duration = token.EndTs.Value - startTime;
            var phonemeDuration = duration / phonemes.Count;

            for (var i = 0; i < phonemes.Count; i++)
            {
                var phoneme = phonemes[i];
                var timestamp = startTime + i * phonemeDuration;

                if (!_phonemeMap.TryGetValue(phoneme, out var pose))
                {
                    pose = PhonemePose.Neutral;
                }

                frames.Add(
                    new LipSyncFrame
                    {
                        MouthOpenY = pose.MouthOpenY,
                        JawOpen = pose.JawOpen,
                        MouthForm = pose.MouthForm,
                        MouthShrug = pose.MouthShrug,
                        MouthFunnel = pose.MouthFunnel,
                        MouthPuckerWiden = pose.MouthPuckerWiden,
                        MouthPressLipOpen = pose.MouthPressLipOpen,
                        MouthX = pose.MouthX,
                        CheekPuffC = pose.CheekPuffC,
                        Timestamp = timestamp,
                    }
                );
            }
        }

        frames.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        return new LipSyncTimeline(frames.ToArray(), segment.DurationInSeconds);
    }

    /// <inheritdoc />
    public void Reset()
    {
        // Stateless — nothing to reset.
    }

    /// <summary>
    ///     Splits an IPA phoneme string into individual phoneme characters,
    ///     skipping stress and length marks (ˈ, ˌ, ː).
    /// </summary>
    private static List<string> SplitPhonemes(string phonemeString)
    {
        var result = new List<string>();

        foreach (var c in phonemeString)
        {
            if (StressMarks.Contains(c))
            {
                continue;
            }

            result.Add(c.ToString());
        }

        return result;
    }

    /// <summary>
    ///     Builds the IPA phoneme → mouth pose lookup table.
    ///     Copied from <c>VBridgerLipSyncService.InitializeMisakiPhonemeMap()</c>.
    /// </summary>
    private static Dictionary<string, PhonemePose> InitializeMisakiPhonemeMap()
    {
        // Phoneme to pose mapping based on VBridger parameter definitions:
        // MouthForm: -1 (Frown) to +1 (Smile)
        // MouthPuckerWiden: -1 (Wide) to +1 (Pucker)
        // MouthPressLipOpen: -1 (Pressed Thin) to +1 (Separated/Teeth)

        var map = new Dictionary<string, PhonemePose>();

        // --- Neutral ---
        map.Add("SIL", PhonemePose.Neutral);

        // --- Shared IPA Consonants ---
        // Plosives (b, p, d, t, g, k) - Focus on closure and puff
        map.Add("b", new PhonemePose(pressLip: -1.0f, cheekPuff: 0.6f));
        map.Add("p", new PhonemePose(pressLip: -1.0f, cheekPuff: 0.8f));
        map.Add("d", new PhonemePose(0.05f, 0.05f, pressLip: 0.0f, cheekPuff: 0.2f));
        map.Add("t", new PhonemePose(0.05f, 0.05f, pressLip: 0.0f, cheekPuff: 0.3f));
        map.Add("ɡ", new PhonemePose(0.1f, 0.15f, pressLip: 0.2f, cheekPuff: 0.5f));
        map.Add("k", new PhonemePose(0.1f, 0.15f, pressLip: 0.2f, cheekPuff: 0.4f));

        // Fricatives (f, v, s, z, h, ʃ, ʒ, ð, θ) - Focus on partial closure/airflow shapes
        map.Add("f", new PhonemePose(0.05f, pressLip: -0.2f, form: -0.2f, puckerWiden: -0.1f));
        map.Add("v", new PhonemePose(0.05f, pressLip: -0.1f, form: -0.2f, puckerWiden: -0.1f));
        map.Add(
            "s",
            new PhonemePose(jawOpen: 0.0f, pressLip: 0.9f, form: 0.3f, puckerWiden: -0.6f)
        );
        map.Add(
            "z",
            new PhonemePose(jawOpen: 0.0f, pressLip: 0.8f, form: 0.2f, puckerWiden: -0.5f)
        );
        map.Add("h", new PhonemePose(0.2f, 0.2f, pressLip: 0.5f));
        map.Add("ʃ", new PhonemePose(0.1f, funnel: 0.9f, puckerWiden: 0.6f, pressLip: 0.2f));
        map.Add("ʒ", new PhonemePose(0.1f, funnel: 0.8f, puckerWiden: 0.5f, pressLip: 0.2f));
        map.Add("ð", new PhonemePose(0.05f, pressLip: 0.1f, puckerWiden: -0.2f));
        map.Add("θ", new PhonemePose(0.05f, pressLip: 0.2f, puckerWiden: -0.3f));

        // Nasals (m, n, ŋ) - Focus on closure or near-closure
        map.Add("m", new PhonemePose(pressLip: -1.0f));
        map.Add("n", new PhonemePose(0.05f, 0.05f, pressLip: 0.0f));
        map.Add("ŋ", new PhonemePose(0.15f, 0.2f, pressLip: 0.4f));

        // Liquids/Glides (l, ɹ, w, j) - Varied shapes
        map.Add("l", new PhonemePose(0.2f, 0.2f, puckerWiden: -0.3f, pressLip: 0.6f));
        map.Add(
            "ɹ",
            new PhonemePose(0.15f, 0.15f, funnel: 0.4f, puckerWiden: 0.2f, pressLip: 0.3f)
        );
        map.Add("w", new PhonemePose(0.1f, 0.1f, funnel: 1.0f, puckerWiden: 0.9f, pressLip: -0.3f));
        map.Add("j", new PhonemePose(0.1f, 0.1f, 0.6f, 0.3f, puckerWiden: -0.8f, pressLip: 0.8f));

        // --- Shared Consonant Clusters ---
        map.Add(
            "ʤ",
            new PhonemePose(0.1f, funnel: 0.8f, puckerWiden: 0.5f, pressLip: 0.2f, cheekPuff: 0.3f)
        );
        map.Add(
            "ʧ",
            new PhonemePose(0.1f, funnel: 0.9f, puckerWiden: 0.6f, pressLip: 0.2f, cheekPuff: 0.4f)
        );

        // --- Shared IPA Vowels ---
        map.Add("ə", new PhonemePose(0.3f, 0.3f, pressLip: 0.5f));
        map.Add("i", new PhonemePose(0.1f, 0.1f, 0.7f, 0.4f, puckerWiden: -0.9f, pressLip: 0.9f));
        map.Add(
            "u",
            new PhonemePose(0.15f, 0.15f, funnel: 1.0f, puckerWiden: 1.0f, pressLip: -0.2f)
        );
        map.Add("ɑ", new PhonemePose(0.9f, 1.0f, pressLip: 0.8f));
        map.Add("ɔ", new PhonemePose(0.6f, 0.7f, funnel: 0.5f, puckerWiden: 0.3f, pressLip: 0.7f));
        map.Add("ɛ", new PhonemePose(0.5f, 0.5f, puckerWiden: -0.5f, pressLip: 0.7f));
        map.Add("ɜ", new PhonemePose(0.4f, 0.4f, pressLip: 0.6f));
        map.Add("ɪ", new PhonemePose(0.2f, 0.2f, 0.2f, puckerWiden: -0.6f, pressLip: 0.8f));
        map.Add("ʊ", new PhonemePose(0.2f, 0.2f, funnel: 0.8f, puckerWiden: 0.7f, pressLip: 0.1f));
        map.Add("ʌ", new PhonemePose(0.6f, 0.6f, pressLip: 0.7f));

        // --- Shared Diphthong Vowels (Targeting the end-shape's characteristics) ---
        map.Add("A", new PhonemePose(0.3f, 0.3f, 0.4f, puckerWiden: -0.7f, pressLip: 0.8f));
        map.Add("I", new PhonemePose(0.4f, 0.4f, 0.3f, puckerWiden: -0.6f, pressLip: 0.8f));
        map.Add("W", new PhonemePose(0.3f, 0.3f, funnel: 0.9f, puckerWiden: 0.8f, pressLip: 0.0f));
        map.Add("Y", new PhonemePose(0.3f, 0.3f, 0.2f, puckerWiden: -0.5f, pressLip: 0.8f));

        // --- Shared Custom Vowel ---
        map.Add("ᵊ", new PhonemePose(0.1f, 0.1f, pressLip: 0.2f));

        // --- American-only ---
        map.Add("æ", new PhonemePose(0.7f, 0.7f, 0.3f, puckerWiden: -0.8f, pressLip: 0.9f));
        map.Add("O", new PhonemePose(0.3f, 0.3f, funnel: 0.8f, puckerWiden: 0.6f, pressLip: 0.1f));
        map.Add("ᵻ", new PhonemePose(0.15f, 0.15f, puckerWiden: -0.2f, pressLip: 0.6f));
        map.Add("ɾ", new PhonemePose(0.05f, 0.05f, pressLip: 0.3f));

        // --- British-only ---
        map.Add("a", new PhonemePose(0.7f, 0.7f, puckerWiden: -0.4f, pressLip: 0.8f));
        map.Add("Q", new PhonemePose(0.3f, 0.3f, funnel: 0.7f, puckerWiden: 0.5f, pressLip: 0.1f));
        map.Add("ɒ", new PhonemePose(0.8f, 0.9f, funnel: 0.2f, puckerWiden: 0.1f, pressLip: 0.8f));

        return map;
    }

    /// <summary>
    ///     Lightweight value type for internal phoneme-to-pose lookup.
    ///     Fields mirror <see cref="LipSyncFrame" />'s mouth parameters.
    /// </summary>
    private readonly struct PhonemePose
    {
        public static readonly PhonemePose Neutral = new();

        public readonly float MouthOpenY;
        public readonly float JawOpen;
        public readonly float MouthForm;
        public readonly float MouthShrug;
        public readonly float MouthFunnel;
        public readonly float MouthPuckerWiden;
        public readonly float MouthPressLipOpen;
        public readonly float MouthX;
        public readonly float CheekPuffC;

        public PhonemePose(
            float openY = 0,
            float jawOpen = 0,
            float form = 0,
            float shrug = 0,
            float funnel = 0,
            float puckerWiden = 0,
            float pressLip = 0,
            float mouthX = 0,
            float cheekPuff = 0
        )
        {
            MouthOpenY = openY;
            JawOpen = jawOpen;
            MouthForm = form;
            MouthShrug = shrug;
            MouthFunnel = funnel;
            MouthPuckerWiden = puckerWiden;
            MouthPressLipOpen = pressLip;
            MouthX = mouthX;
            CheekPuffC = cheekPuff;
        }
    }
}
