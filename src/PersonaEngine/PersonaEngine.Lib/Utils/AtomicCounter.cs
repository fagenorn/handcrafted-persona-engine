using System.Runtime.CompilerServices;

namespace PersonaEngine.Lib.Utils;

/// <summary>
///     Thread-safe counter implementation.
/// </summary>
public class AtomicCounter
{
    private uint _value;

    public uint GetValue()
    {
        return _value;
    }

    public uint Increment()
    {
        return (uint)Interlocked.Increment(ref Unsafe.As<uint, int>(ref _value));
    }
}
