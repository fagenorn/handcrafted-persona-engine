using PersonaEngine.Lib.Utils;
using Xunit;

namespace PersonaEngine.Lib.Tests.Utils;

public class FloatRingBufferTests
{
    [Fact]
    public void NewBuffer_HeadIsZero_CapacityMatches()
    {
        var buf = new FloatRingBuffer(4);
        Assert.Equal(0, buf.Head);
        Assert.Equal(4, buf.Capacity);
        Assert.Equal(4, buf.Values.Length);
    }

    [Fact]
    public void Push_AdvancesHead_Modulo_Capacity()
    {
        var buf = new FloatRingBuffer(3);
        buf.Push(1f);
        buf.Push(2f);
        buf.Push(3f);
        Assert.Equal(0, buf.Head);
        buf.Push(4f);
        Assert.Equal(1, buf.Head);
    }

    [Fact]
    public void Push_OverwritesOldestValue_WhenFull()
    {
        var buf = new FloatRingBuffer(3);
        buf.Push(1f);
        buf.Push(2f);
        buf.Push(3f);
        buf.Push(4f); // overwrites slot 0 (the 1f)
        // Values are the raw buffer in slot order: {4, 2, 3}
        Assert.Equal(4f, buf.Values[0]);
        Assert.Equal(2f, buf.Values[1]);
        Assert.Equal(3f, buf.Values[2]);
    }

    [Fact]
    public void Values_ReflectsPushedData_InRawSlotOrder()
    {
        var buf = new FloatRingBuffer(4);
        buf.Push(0.1f);
        buf.Push(0.2f);
        Assert.Equal(0.1f, buf.Values[0]);
        Assert.Equal(0.2f, buf.Values[1]);
        Assert.Equal(0f, buf.Values[2]); // untouched
        Assert.Equal(0f, buf.Values[3]);
    }
}
