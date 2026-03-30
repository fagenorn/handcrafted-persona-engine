using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.TTS.Profanity;

/// <summary>
///     A profanity detector that uses an ONNX model under the hood.
///     It supports DI for logging, configurable filter threshold, and a benchmarking utility.
/// </summary>
public class ProfanityDetector : IDisposable
{
    private readonly ILogger<ProfanityDetector> _logger;

    private readonly ProfanityDetectorOnnx _onnxDetector;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProfanityDetector" /> class.
    /// </summary>
    /// <param name="logger">The logger injected via dependency injection.</param>
    /// <param name="modelPath">Optional path to the ONNX model file.</param>
    /// <param name="vocabPath">Optional path to the vocabulary file for tokenization.</param>
    public ProfanityDetector(ILogger<ProfanityDetector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Initializing ProfanityDetector");
        _onnxDetector = new ProfanityDetectorOnnx();
    }

    /// <summary>
    ///     Gets or sets the threshold for detecting any profanity.
    ///     If the model's score is below this threshold, the sentence is considered clean.
    /// </summary>
    public float FilterThreshold { get; set; } = 0.5f;

    /// <summary>
    ///     Gets or sets the threshold for moderate profanity.
    /// </summary>
    public float ModerateThreshold { get; set; } = 0.75f;

    /// <summary>
    ///     Gets or sets the threshold for severe profanity.
    /// </summary>
    public float SevereThreshold { get; set; } = 0.9f;

    public void Dispose()
    {
        _onnxDetector.Dispose();
    }

    /// <summary>
    ///     Evaluates the given sentence and determines the severity of profanity.
    /// </summary>
    /// <param name="sentence">The sentence to evaluate.</param>
    /// <returns>
    ///     A <see cref="ProfanitySeverity" /> enum value indicating the level of profanity:
    ///     Clean, Mild, Moderate, or Severe.
    /// </returns>
    public ProfanitySeverity EvaluateProfanity(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            _logger.LogWarning("Empty or whitespace sentence provided.");

            return ProfanitySeverity.Clean;
        }

        var score = _onnxDetector.Run(sentence);
        _logger.LogDebug("Sentence: \"{sentence}\" scored {score}.", sentence, score);

        if (score < FilterThreshold)
        {
            return ProfanitySeverity.Clean;
        }

        if (score < ModerateThreshold)
        {
            return ProfanitySeverity.Mild;
        }

        if (score < SevereThreshold)
        {
            return ProfanitySeverity.Moderate;
        }

        return ProfanitySeverity.Severe;
    }

    /// <summary>
    ///     Benchmarks the profanity filter using provided test data.
    ///     Each test item consists of a sentence and the expected outcome (true if profane, false otherwise).
    ///     For benchmarking purposes, any result other than <see cref="ProfanitySeverity.Clean" /> is considered profane.
    ///     Returns useful statistics to help calibrate the filter thresholds.
    /// </summary>
    /// <param name="testData">A collection of test sentences with their expected results.</param>
    /// <returns>A <see cref="BenchmarkResult" /> instance containing detailed metrics.</returns>
    public BenchmarkResult Benchmark(
        IEnumerable<(string Sentence, bool ExpectedIsProfane)> testData
    )
    {
        if (testData == null)
        {
            throw new ArgumentNullException(nameof(testData));
        }

        int total = 0,
            truePositives = 0,
            falsePositives = 0,
            trueNegatives = 0,
            falseNegatives = 0;

        foreach (var (sentence, expected) in testData)
        {
            total++;
            var severity = EvaluateProfanity(sentence);
            // For benchmarking, any severity other than Clean is considered profane.
            var actual = severity != ProfanitySeverity.Clean;

            if (expected && actual)
            {
                truePositives++;
            }
            else if (!expected && actual)
            {
                falsePositives++;
            }
            else if (!expected && !actual)
            {
                trueNegatives++;
            }
            else if (expected && !actual)
            {
                falseNegatives++;
            }

            _logger.LogDebug(
                "Benchmarking sentence: \"{sentence}\" | Severity: {severity} | Expected: {expected} | Actual: {actual}",
                sentence,
                severity,
                expected,
                actual
            );
        }

        var accuracy = total > 0 ? (double)(truePositives + trueNegatives) / total : 0;
        var precision =
            truePositives + falsePositives > 0
                ? (double)truePositives / (truePositives + falsePositives)
                : 0;
        var recall =
            truePositives + falseNegatives > 0
                ? (double)truePositives / (truePositives + falseNegatives)
                : 0;

        var result = new BenchmarkResult
        {
            Total = total,
            TruePositives = truePositives,
            FalsePositives = falsePositives,
            TrueNegatives = trueNegatives,
            FalseNegatives = falseNegatives,
            Accuracy = accuracy,
            Precision = precision,
            Recall = recall,
        };

        _logger.LogInformation(
            "Benchmark results: {result} [Thresholds: FilterThreshold={FilterThreshold}, ModerateThreshold={ModerateThreshold}, SevereThreshold={SevereThreshold}]",
            result,
            FilterThreshold,
            ModerateThreshold,
            SevereThreshold
        );

        return result;
    }
}
