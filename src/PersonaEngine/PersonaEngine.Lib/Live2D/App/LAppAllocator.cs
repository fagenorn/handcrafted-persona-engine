using System.Runtime.InteropServices;
using PersonaEngine.Lib.Live2D.Framework;

namespace PersonaEngine.Lib.Live2D.App;

/// <summary>
///     メモリ確保・解放処理のインターフェースの実装。
///     フレームワークから呼び出される。
/// </summary>
public class LAppAllocator : ICubismAllocator
{
    /// <summary>
    ///     メモリ領域を割り当てる。
    /// </summary>
    /// <param name="size">割り当てたいサイズ。</param>
    /// <returns>指定したメモリ領域</returns>
    public IntPtr Allocate(int size)
    {
        return Marshal.AllocHGlobal(size);
    }

    /// <summary>
    ///     メモリ領域を解放する
    /// </summary>
    /// <param name="memory">解放するメモリ。</param>
    public void Deallocate(IntPtr memory)
    {
        Marshal.FreeHGlobal(memory);
    }

    /// <summary>
    /// </summary>
    /// <param name="size">割り当てたいサイズ。</param>
    /// <param name="alignment">割り当てたいサイズ。</param>
    /// <returns>alignedAddress</returns>
    public unsafe IntPtr AllocateAligned(int size, int alignment)
    {
        IntPtr offset,
            shift,
            alignedAddress;

        IntPtr allocation;
        void** preamble;

        offset = alignment - 1 + sizeof(void*);

        allocation = Allocate((int)(size + offset));

        alignedAddress = allocation + sizeof(void*);

        shift = alignedAddress % alignment;

        if (shift != 0)
        {
            alignedAddress += alignment - shift;
        }

        preamble = (void**)alignedAddress;
        preamble[-1] = (void*)allocation;

        return alignedAddress;
    }

    /// <summary>
    /// </summary>
    /// <param name="alignedMemory">解放するメモリ。</param>
    public unsafe void DeallocateAligned(IntPtr alignedMemory)
    {
        var preamble = (void**)alignedMemory;

        Deallocate(new IntPtr(preamble[-1]));
    }
}
