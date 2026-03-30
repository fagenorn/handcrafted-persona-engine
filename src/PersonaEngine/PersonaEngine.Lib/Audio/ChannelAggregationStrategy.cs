namespace PersonaEngine.Lib.Audio;

/// <summary>
///     A strategy for aggregating multiple audio channels into a single channel.
/// </summary>
public interface IChannelAggregationStrategy
{
    /// <summary>
    ///     Aggregates the specified frame of audio samples.
    /// </summary>
    void Aggregate(ReadOnlyMemory<float> frame, Memory<float> destination);

    void Aggregate(ReadOnlyMemory<byte> frame, Memory<byte> destination, ushort bitsPerSample);

    void Aggregate(ReadOnlyMemory<byte> frame, Memory<float> destination, ushort bitsPerSample);

    void Aggregate(ReadOnlyMemory<float> frame, Memory<byte> destination, ushort bitsPerSample);
}

/// <summary>
///     Provides predefined strategies for aggregating float audio channels.
/// </summary>
public static class DefaultChannelAggregationStrategies
{
    /// <summary>
    ///     Gets the average aggregation strategy for float audio channels.
    /// </summary>
    public static IChannelAggregationStrategy Average { get; } =
        new AverageChannelAggregationStrategy();

    /// <summary>
    ///     Gets the max aggregation strategy for float audio channels.
    /// </summary>
    public static IChannelAggregationStrategy Sum { get; } = new SumChannelAggregationStrategy();

    /// <summary>
    ///     Gets the channel selection strategy for float audio channels.
    /// </summary>
    /// <param name="channelIndex">The index of the channel to use for aggregation.</param>
    /// <returns>The channel selection strategy.</returns>
    public static IChannelAggregationStrategy SelectChannel(int channelIndex)
    {
        return new SelectChannelAggregationStrategy(channelIndex);
    }
}

/// <summary>
///     Aggregates audio channels by computing the average of the samples.
/// </summary>
internal class AverageChannelAggregationStrategy : IChannelAggregationStrategy
{
    public void Aggregate(ReadOnlyMemory<float> frame, Memory<float> destination)
    {
        destination.Span[0] = GetAverageFloat(frame);
    }

    public void Aggregate(
        ReadOnlyMemory<byte> frame,
        Memory<byte> destination,
        ushort bitsPerSample
    )
    {
        var sampleValue = GetAverageFloat(frame, bitsPerSample);
        SampleSerializer.WriteSample(destination.Span, 0, sampleValue, bitsPerSample);
    }

    public void Aggregate(
        ReadOnlyMemory<byte> frame,
        Memory<float> destination,
        ushort bitsPerSample
    )
    {
        destination.Span[0] = GetAverageFloat(frame, bitsPerSample);
    }

    public void Aggregate(
        ReadOnlyMemory<float> frame,
        Memory<byte> destination,
        ushort bitsPerSample
    )
    {
        var sampleValue = GetAverageFloat(frame);
        SampleSerializer.WriteSample(destination.Span, 0, sampleValue, bitsPerSample);
    }

    private static float GetAverageFloat(ReadOnlyMemory<float> frame)
    {
        var span = frame.Span;
        var sum = 0.0f;
        for (var i = 0; i < span.Length; i++)
        {
            sum += span[i];
        }

        return sum / span.Length;
    }

    private static float GetAverageFloat(ReadOnlyMemory<byte> frame, ushort bitsPerSample)
    {
        var index = 0;
        var sum = 0f;
        var span = frame.Span;
        while (index < frame.Length)
        {
            sum += SampleSerializer.ReadSample(span, ref index, bitsPerSample);
        }

        // We multiply by 8 first to avoid integer division
        return sum / (frame.Length * 8 / bitsPerSample);
    }
}

/// <summary>
///     Aggregates float audio channels by selecting the maximum sample.
/// </summary>
internal class SumChannelAggregationStrategy : IChannelAggregationStrategy
{
    public void Aggregate(ReadOnlyMemory<float> frame, Memory<float> destination)
    {
        destination.Span[0] = GetSumFloat(frame);
    }

    public void Aggregate(
        ReadOnlyMemory<byte> frame,
        Memory<byte> destination,
        ushort bitsPerSample
    )
    {
        var sampleValue = GetSumFloat(frame, bitsPerSample);
        SampleSerializer.WriteSample(destination.Span, 0, sampleValue, bitsPerSample);
    }

    public void Aggregate(
        ReadOnlyMemory<byte> frame,
        Memory<float> destination,
        ushort bitsPerSample
    )
    {
        destination.Span[0] = GetSumFloat(frame, bitsPerSample);
    }

    public void Aggregate(
        ReadOnlyMemory<float> frame,
        Memory<byte> destination,
        ushort bitsPerSample
    )
    {
        var sampleValue = GetSumFloat(frame);
        SampleSerializer.WriteSample(destination.Span, 0, sampleValue, bitsPerSample);
    }

    private static float GetSumFloat(ReadOnlyMemory<float> frame)
    {
        var span = frame.Span;
        var sum = 0.0f;
        for (var i = 0; i < span.Length; i++)
        {
            sum += span[i];
        }

        // Ensure the float value is within the range (-1, 1)
        if (sum > 1)
        {
            sum = 1;
        }
        else if (sum < -1)
        {
            sum = -1;
        }

        return sum;
    }

    private static float GetSumFloat(ReadOnlyMemory<byte> frame, ushort bitsPerSample)
    {
        var index = 0;
        var sum = 0f;
        var span = frame.Span;
        while (index < frame.Length)
        {
            sum += SampleSerializer.ReadSample(span, ref index, bitsPerSample);
        }

        // Ensure the float value is within the range (-1, 1)
        if (sum > 1)
        {
            sum = 1;
        }
        else if (sum < -1)
        {
            sum = -1;
        }

        return sum;
    }
}

/// <summary>
///     Aggregates float audio channels by selecting a specific channel.
/// </summary>
/// <param name="channelIndex">The index of the channel to use for aggregation.</param>
internal class SelectChannelAggregationStrategy(int channelIndex) : IChannelAggregationStrategy
{
    public void Aggregate(ReadOnlyMemory<float> frame, Memory<float> destination)
    {
        destination.Span[0] = GetChannelFloat(frame, channelIndex);
    }

    public void Aggregate(
        ReadOnlyMemory<byte> frame,
        Memory<byte> destination,
        ushort bitsPerSample
    )
    {
        var sampleValue = GetChannelFloat(frame, channelIndex, bitsPerSample);
        SampleSerializer.WriteSample(destination.Span, 0, sampleValue, bitsPerSample);
    }

    public void Aggregate(
        ReadOnlyMemory<byte> frame,
        Memory<float> destination,
        ushort bitsPerSample
    )
    {
        destination.Span[0] = GetChannelFloat(frame, channelIndex, bitsPerSample);
    }

    public void Aggregate(
        ReadOnlyMemory<float> frame,
        Memory<byte> destination,
        ushort bitsPerSample
    )
    {
        var sampleValue = GetChannelFloat(frame, channelIndex);
        SampleSerializer.WriteSample(destination.Span, 0, sampleValue, bitsPerSample);
    }

    private static float GetChannelFloat(ReadOnlyMemory<float> frame, int channelIndex)
    {
        return frame.Span[channelIndex];
    }

    private static float GetChannelFloat(
        ReadOnlyMemory<byte> frame,
        int channelIndex,
        ushort bitsPerSample
    )
    {
        var byteIndex = channelIndex * bitsPerSample / 8;

        return SampleSerializer.ReadSample(frame.Span, ref byteIndex, bitsPerSample);
    }
}
