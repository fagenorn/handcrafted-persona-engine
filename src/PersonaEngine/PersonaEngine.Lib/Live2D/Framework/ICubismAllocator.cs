namespace PersonaEngine.Lib.Live2D.Framework;

/// <summary>
///     メモリアロケーションを抽象化したクラス.
///     メモリ確保・解放処理をプラットフォーム側で実装して
///     フレームワークから呼び出すためのインターフェース。
/// </summary>
public interface ICubismAllocator
{
    /// <summary>
    ///     アラインメント制約なしのヒープ・メモリーを確保します。
    /// </summary>
    /// <param name="size">確保するバイト数</param>
    /// <returns>成功すると割り当てられたメモリのアドレス。 そうでなければ '0'を返す。</returns>
    IntPtr Allocate(int size);

    /// <summary>
    ///     アラインメント制約なしのヒープ・メモリーを解放します。
    /// </summary>
    /// <param name="memory">解放するメモリのアドレス</param>
    void Deallocate(IntPtr memory);

    /// <summary>
    ///     アラインメント制約ありのヒープ・メモリーを確保します。
    /// </summary>
    /// <param name="size">確保するバイト数</param>
    /// <param name="alignment">メモリーブロックのアラインメント幅</param>
    /// <returns>成功すると割り当てられたメモリのアドレス。 そうでなければ '0'を返す。</returns>
    IntPtr AllocateAligned(int size, int alignment);

    /// <summary>
    ///     アラインメント制約ありのヒープ・メモリーを解放します。
    /// </summary>
    /// <param name="alignedMemory">解放するメモリのアドレス</param>
    void DeallocateAligned(IntPtr alignedMemory);
}
