namespace PersonaEngine.Lib.TTS.Profanity;

/// <summary>
///     Holds benchmark statistics for a profanity detection run.
/// </summary>
public record BenchmarkResult
{
    public int Total { get; set; }

    public int TruePositives { get; set; }

    public int FalsePositives { get; set; }

    public int TrueNegatives { get; set; }

    public int FalseNegatives { get; set; }

    public double Accuracy { get; set; }

    public double Precision { get; set; }

    public double Recall { get; set; }

    public override string ToString()
    {
        return $"Total: {Total}, TP: {TruePositives}, FP: {FalsePositives}, TN: {TrueNegatives}, FN: {FalseNegatives}, "
            + $"Accuracy: {Accuracy:P2}, Precision: {Precision:P2}, Recall: {Recall:P2}";
    }
}
