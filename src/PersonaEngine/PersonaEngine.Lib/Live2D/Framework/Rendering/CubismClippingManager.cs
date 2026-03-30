using System.Numerics;
using PersonaEngine.Lib.Live2D.Framework.Math;
using PersonaEngine.Lib.Live2D.Framework.Model;
using PersonaEngine.Lib.Live2D.Framework.Rendering.OpenGL;
using PersonaEngine.Lib.Live2D.Framework.Type;

namespace PersonaEngine.Lib.Live2D.Framework.Rendering;

public class CubismClippingManager
{
    /// <summary>
    ///     実験時に1チャンネルの場合は1、RGBだけの場合は3、アルファも含める場合は4
    /// </summary>
    public const int ColorChannelCount = 4;

    /// <summary>
    ///     通常のフレームバッファ1枚あたりのマスク最大数
    /// </summary>
    public const int ClippingMaskMaxCountOnDefault = 36;

    /// <summary>
    ///     フレームバッファが2枚以上ある場合のフレームバッファ1枚あたりのマスク最大数
    /// </summary>
    public const int ClippingMaskMaxCountOnMultiRenderTexture = 32;

    private readonly RenderType _renderType;

    protected List<CubismTextureColor> ChannelColors = [];

    /// <summary>
    ///     マスクのクリアフラグの配列
    /// </summary>
    protected List<bool> ClearedMaskBufferFlags = [];

    /// <summary>
    ///     マスク用クリッピングコンテキストのリスト
    /// </summary>
    protected List<CubismClippingContext> ClippingContextListForMask = [];

    /// <summary>
    ///     オフスクリーンサーフェイスのアドレス
    /// </summary>
    protected CubismOffscreenSurface_OpenGLES2 CurrentMaskBuffer;

    /// <summary>
    ///     マスク配置計算用の矩形
    /// </summary>
    protected RectF TmpBoundsOnModel = new();

    /// <summary>
    ///     マスク計算用の行列
    /// </summary>
    protected CubismMatrix44 TmpMatrix = new();

    /// <summary>
    ///     マスク計算用の行列
    /// </summary>
    protected CubismMatrix44 TmpMatrixForDraw = new();

    /// <summary>
    ///     マスク計算用の行列
    /// </summary>
    protected CubismMatrix44 TmpMatrixForMask = new();

    public CubismClippingManager(RenderType render)
    {
        _renderType = render;
        ClippingMaskBufferSize = new Vector2(256, 256);

        ChannelColors.Add(new CubismTextureColor(1.0f, 0f, 0f, 0f));
        ChannelColors.Add(new CubismTextureColor(0f, 1.0f, 0f, 0f));
        ChannelColors.Add(new CubismTextureColor(0f, 0f, 1.0f, 0f));
        ChannelColors.Add(new CubismTextureColor(0f, 0f, 0f, 1.0f));
    }

    /// <summary>
    ///     描画用クリッピングコンテキストのリスト
    /// </summary>
    public List<CubismClippingContext?> ClippingContextListForDraw { get; init; } = [];

    /// <summary>
    ///     クリッピングマスクのバッファサイズ（初期値:256）
    /// </summary>
    public Vector2 ClippingMaskBufferSize { get; private set; }

    /// <summary>
    ///     生成するレンダーテクスチャの枚数
    /// </summary>
    public int RenderTextureCount { get; private set; }

    public void Close()
    {
        ClippingContextListForDraw.Clear();
        ClippingContextListForMask.Clear();
        ChannelColors.Clear();
        ClearedMaskBufferFlags.Clear();
    }

    /// <summary>
    ///     マネージャの初期化処理
    ///     クリッピングマスクを使う描画オブジェクトの登録を行う
    /// </summary>
    /// <param name="model">モデルのインスタンス</param>
    /// <param name="maskBufferCount">バッファの生成数</param>
    public unsafe void Initialize(CubismModel model, int maskBufferCount)
    {
        RenderTextureCount = maskBufferCount;

        // レンダーテクスチャのクリアフラグの設定
        for (var i = 0; i < RenderTextureCount; ++i)
        {
            ClearedMaskBufferFlags.Add(false);
        }

        //クリッピングマスクを使う描画オブジェクトを全て登録する
        //クリッピングマスクは、通常数個程度に限定して使うものとする
        for (var i = 0; i < model.GetDrawableCount(); i++)
        {
            if (model.GetDrawableMaskCounts()[i] <= 0)
            {
                //クリッピングマスクが使用されていないアートメッシュ（多くの場合使用しない）
                ClippingContextListForDraw.Add(null);

                continue;
            }

            // 既にあるClipContextと同じかチェックする
            var cc = FindSameClip(model.GetDrawableMasks()[i], model.GetDrawableMaskCounts()[i]);
            if (cc == null)
            {
                // 同一のマスクが存在していない場合は生成する
                if (_renderType == RenderType.OpenGL)
                {
                    cc = new CubismClippingContext_OpenGLES2(
                        this,
                        model,
                        model.GetDrawableMasks()[i],
                        model.GetDrawableMaskCounts()[i]
                    );
                }
                else
                {
                    throw new Exception("Only OpenGL");
                }

                ClippingContextListForMask.Add(cc);
            }

            cc.AddClippedDrawable(i);

            ClippingContextListForDraw.Add(cc);
        }
    }

    /// <summary>
    ///     既にマスクを作っているかを確認。
    ///     作っているようであれば該当するクリッピングマスクのインスタンスを返す。
    ///     作っていなければNULLを返す
    /// </summary>
    /// <param name="drawableMasks">描画オブジェクトをマスクする描画オブジェクトのリスト</param>
    /// <param name="drawableMaskCounts">描画オブジェクトをマスクする描画オブジェクトの数</param>
    /// <returns>該当するクリッピングマスクが存在すればインスタンスを返し、なければNULLを返す。</returns>
    private unsafe CubismClippingContext? FindSameClip(int* drawableMasks, int drawableMaskCounts)
    {
        // 作成済みClippingContextと一致するか確認
        for (var i = 0; i < ClippingContextListForMask.Count; i++)
        {
            var cc = ClippingContextListForMask[i];
            var count = cc.ClippingIdCount;
            if (count != drawableMaskCounts)
            {
                continue; //個数が違う場合は別物
            }

            var samecount = 0;

            // 同じIDを持つか確認。配列の数が同じなので、一致した個数が同じなら同じ物を持つとする。
            for (var j = 0; j < count; j++)
            {
                var clipId = cc.ClippingIdList[j];
                for (var k = 0; k < count; k++)
                {
                    if (drawableMasks[k] == clipId)
                    {
                        samecount++;

                        break;
                    }
                }
            }

            if (samecount == count)
            {
                return cc;
            }
        }

        return null; //見つからなかった
    }

    /// <summary>
    ///     高精細マスク処理用の行列を計算する
    /// </summary>
    /// <param name="model">モデルのインスタンス</param>
    /// <param name="isRightHanded">処理が右手系であるか</param>
    public void SetupMatrixForHighPrecision(CubismModel model, bool isRightHanded)
    {
        // 全てのクリッピングを用意する
        // 同じクリップ（複数の場合はまとめて１つのクリップ）を使う場合は１度だけ設定する
        var usingClipCount = 0;
        for (var clipIndex = 0; clipIndex < ClippingContextListForMask.Count; clipIndex++)
        {
            // １つのクリッピングマスクに関して
            var cc = ClippingContextListForMask[clipIndex];

            // このクリップを利用する描画オブジェクト群全体を囲む矩形を計算
            CalcClippedDrawTotalBounds(model, cc);

            if (cc.IsUsing)
            {
                usingClipCount++; //使用中としてカウント
            }
        }

        if (usingClipCount <= 0)
        {
            return;
        }

        // マスク行列作成処理
        SetupLayoutBounds(0);

        // サイズがレンダーテクスチャの枚数と合わない場合は合わせる
        if (ClearedMaskBufferFlags.Count != RenderTextureCount)
        {
            ClearedMaskBufferFlags.Clear();

            for (var i = 0; i < RenderTextureCount; ++i)
            {
                ClearedMaskBufferFlags.Add(false);
            }
        }
        else
        {
            // マスクのクリアフラグを毎フレーム開始時に初期化
            for (var i = 0; i < RenderTextureCount; ++i)
            {
                ClearedMaskBufferFlags[i] = false;
            }
        }

        // 実際にマスクを生成する
        // 全てのマスクをどの様にレイアウトして描くかを決定し、ClipContext , ClippedDrawContext に記憶する
        for (var clipIndex = 0; clipIndex < ClippingContextListForMask.Count; clipIndex++)
        {
            // --- 実際に１つのマスクを描く ---
            var clipContext = ClippingContextListForMask[clipIndex];
            var allClippedDrawRect = clipContext.AllClippedDrawRect; //このマスクを使う、全ての描画オブジェクトの論理座標上の囲み矩形
            var layoutBoundsOnTex01 = clipContext.LayoutBounds; //この中にマスクを収める
            var MARGIN = 0.05f;
            float scaleX;
            float scaleY;
            var ppu = model.GetPixelsPerUnit();
            var maskPixelWidth = clipContext.Manager.ClippingMaskBufferSize.X;
            var maskPixelHeight = clipContext.Manager.ClippingMaskBufferSize.Y;
            var physicalMaskWidth = layoutBoundsOnTex01.Width * maskPixelWidth;
            var physicalMaskHeight = layoutBoundsOnTex01.Height * maskPixelHeight;

            TmpBoundsOnModel.SetRect(allClippedDrawRect);
            if (TmpBoundsOnModel.Width * ppu > physicalMaskWidth)
            {
                TmpBoundsOnModel.Expand(allClippedDrawRect.Width * MARGIN, 0.0f);
                scaleX = layoutBoundsOnTex01.Width / TmpBoundsOnModel.Width;
            }
            else
            {
                scaleX = ppu / physicalMaskWidth;
            }

            if (TmpBoundsOnModel.Height * ppu > physicalMaskHeight)
            {
                TmpBoundsOnModel.Expand(0.0f, allClippedDrawRect.Height * MARGIN);
                scaleY = layoutBoundsOnTex01.Height / TmpBoundsOnModel.Height;
            }
            else
            {
                scaleY = ppu / physicalMaskHeight;
            }

            // マスク生成時に使う行列を求める
            CreateMatrixForMask(isRightHanded, layoutBoundsOnTex01, scaleX, scaleY);

            clipContext.MatrixForMask.SetMatrix(TmpMatrixForMask.Tr);
            clipContext.MatrixForDraw.SetMatrix(TmpMatrixForDraw.Tr);
        }
    }

    /// <summary>
    ///     マスク作成・描画用の行列を作成する。
    /// </summary>
    /// <param name="isRightHanded">座標を右手系として扱うかを指定</param>
    /// <param name="layoutBoundsOnTex01">マスクを収める領域</param>
    /// <param name="scaleX">描画オブジェクトの伸縮率</param>
    /// <param name="scaleY">描画オブジェクトの伸縮率</param>
    protected void CreateMatrixForMask(
        bool isRightHanded,
        RectF layoutBoundsOnTex01,
        float scaleX,
        float scaleY
    )
    {
        TmpMatrix.LoadIdentity();
        {
            // Layout0..1 を -1..1に変換
            TmpMatrix.TranslateRelative(-1.0f, -1.0f);
            TmpMatrix.ScaleRelative(2.0f, 2.0f);
        }

        {
            // view to Layout0..1
            TmpMatrix.TranslateRelative(layoutBoundsOnTex01.X, layoutBoundsOnTex01.Y); //new = [translate]
            TmpMatrix.ScaleRelative(scaleX, scaleY); //new = [translate][scale]
            TmpMatrix.TranslateRelative(-TmpBoundsOnModel.X, -TmpBoundsOnModel.Y); //new = [translate][scale][translate]
        }

        // tmpMatrixForMask が計算結果
        TmpMatrixForMask.SetMatrix(TmpMatrix.Tr);

        TmpMatrix.LoadIdentity();
        {
            TmpMatrix.TranslateRelative(
                layoutBoundsOnTex01.X,
                layoutBoundsOnTex01.Y * (isRightHanded ? -1.0f : 1.0f)
            ); //new = [translate]
            TmpMatrix.ScaleRelative(scaleX, scaleY * (isRightHanded ? -1.0f : 1.0f)); //new = [translate][scale]
            TmpMatrix.TranslateRelative(-TmpBoundsOnModel.X, -TmpBoundsOnModel.Y); //new = [translate][scale][translate]
        }

        TmpMatrixForDraw.SetMatrix(TmpMatrix.Tr);
    }

    /// <summary>
    ///     クリッピングコンテキストを配置するレイアウト。
    ///     ひとつのレンダーテクスチャを極力いっぱいに使ってマスクをレイアウトする。
    ///     マスクグループの数が4以下ならRGBA各チャンネルに１つずつマスクを配置し、5以上6以下ならRGBAを2,2,1,1と配置する。
    /// </summary>
    /// <param name="usingClipCount">配置するクリッピングコンテキストの数</param>
    protected void SetupLayoutBounds(int usingClipCount)
    {
        var useClippingMaskMaxCount =
            RenderTextureCount <= 1
                ? ClippingMaskMaxCountOnDefault
                : ClippingMaskMaxCountOnMultiRenderTexture * RenderTextureCount;

        if (usingClipCount <= 0 || usingClipCount > useClippingMaskMaxCount)
        {
            if (usingClipCount > useClippingMaskMaxCount)
            {
                // マスクの制限数の警告を出す
                var count = usingClipCount - useClippingMaskMaxCount;
                CubismLog.Error(
                    "not supported mask count : %d\n[Details] render texture count: %d\n, mask count : %d",
                    count,
                    RenderTextureCount,
                    usingClipCount
                );
            }

            // この場合は一つのマスクターゲットを毎回クリアして使用する
            for (var index = 0; index < ClippingContextListForMask.Count; index++)
            {
                var cc = ClippingContextListForMask[index];
                cc.LayoutChannelIndex = 0; // どうせ毎回消すので固定で良い
                cc.LayoutBounds.X = 0.0f;
                cc.LayoutBounds.Y = 0.0f;
                cc.LayoutBounds.Width = 1.0f;
                cc.LayoutBounds.Height = 1.0f;
                cc.BufferIndex = 0;
            }

            return;
        }

        // レンダーテクスチャが1枚なら9分割する（最大36枚）
        var layoutCountMaxValue = RenderTextureCount <= 1 ? 9 : 8;

        // ひとつのRenderTextureを極力いっぱいに使ってマスクをレイアウトする
        // マスクグループの数が4以下ならRGBA各チャンネルに１つずつマスクを配置し、5以上6以下ならRGBAを2,2,1,1と配置する
        var countPerSheetDiv = (usingClipCount + RenderTextureCount - 1) / RenderTextureCount; // レンダーテクスチャ1枚あたり何枚割り当てるか（切り上げ）
        var reduceLayoutTextureCount = usingClipCount % RenderTextureCount; // レイアウトの数を1枚減らすレンダーテクスチャの数（この数だけのレンダーテクスチャが対象）

        // RGBAを順番に使っていく
        var divCount = countPerSheetDiv / ColorChannelCount; //１チャンネルに配置する基本のマスク個数
        var modCount = countPerSheetDiv % ColorChannelCount; //余り、この番号のチャンネルまでに１つずつ配分する

        // RGBAそれぞれのチャンネルを用意していく(0:R , 1:G , 2:B, 3:A, )
        var curClipIndex = 0; //順番に設定していく

        for (
            var renderTextureIndex = 0;
            renderTextureIndex < RenderTextureCount;
            renderTextureIndex++
        )
        {
            for (var channelIndex = 0; channelIndex < ColorChannelCount; channelIndex++)
            {
                // このチャンネルにレイアウトする数
                // NOTE: レイアウト数 = 1チャンネルに配置する基本のマスク + 余りのマスクを置くチャンネルなら1つ追加
                var layoutCount = divCount + (channelIndex < modCount ? 1 : 0);

                // レイアウトの数を1枚減らす場合にそれを行うチャンネルを決定
                // divが0の時は正常なインデックスの範囲内になるように調整
                var checkChannelIndex = modCount + (divCount < 1 ? -1 : 0);

                // 今回が対象のチャンネルかつ、レイアウトの数を1枚減らすレンダーテクスチャが存在する場合
                if (channelIndex == checkChannelIndex && reduceLayoutTextureCount > 0)
                {
                    // 現在のレンダーテクスチャが、対象のレンダーテクスチャであればレイアウトの数を1枚減らす
                    layoutCount -= !(renderTextureIndex < reduceLayoutTextureCount) ? 1 : 0;
                }

                // 分割方法を決定する
                if (layoutCount == 0)
                {
                    // 何もしない
                }
                else if (layoutCount == 1)
                {
                    //全てをそのまま使う
                    var cc = ClippingContextListForMask[curClipIndex++];
                    cc.LayoutChannelIndex = channelIndex;
                    cc.LayoutBounds.X = 0.0f;
                    cc.LayoutBounds.Y = 0.0f;
                    cc.LayoutBounds.Width = 1.0f;
                    cc.LayoutBounds.Height = 1.0f;
                    cc.BufferIndex = renderTextureIndex;
                }
                else if (layoutCount == 2)
                {
                    for (var i = 0; i < layoutCount; i++)
                    {
                        var xpos = i % 2;

                        var cc = ClippingContextListForMask[curClipIndex++];
                        cc.LayoutChannelIndex = channelIndex;

                        cc.LayoutBounds.X = xpos * 0.5f;
                        cc.LayoutBounds.Y = 0.0f;
                        cc.LayoutBounds.Width = 0.5f;
                        cc.LayoutBounds.Height = 1.0f;
                        cc.BufferIndex = renderTextureIndex;
                        //UVを2つに分解して使う
                    }
                }
                else if (layoutCount <= 4)
                {
                    //4分割して使う
                    for (var i = 0; i < layoutCount; i++)
                    {
                        var xpos = i % 2;
                        var ypos = i / 2;

                        var cc = ClippingContextListForMask[curClipIndex++];
                        cc.LayoutChannelIndex = channelIndex;

                        cc.LayoutBounds.X = xpos * 0.5f;
                        cc.LayoutBounds.Y = ypos * 0.5f;
                        cc.LayoutBounds.Width = 0.5f;
                        cc.LayoutBounds.Height = 0.5f;
                        cc.BufferIndex = renderTextureIndex;
                    }
                }
                else if (layoutCount <= layoutCountMaxValue)
                {
                    //9分割して使う
                    for (var i = 0; i < layoutCount; i++)
                    {
                        var xpos = i % 3;
                        var ypos = i / 3;

                        var cc = ClippingContextListForMask[curClipIndex++];
                        cc.LayoutChannelIndex = channelIndex;

                        cc.LayoutBounds.X = xpos / 3.0f;
                        cc.LayoutBounds.Y = ypos / 3.0f;
                        cc.LayoutBounds.Width = 1.0f / 3.0f;
                        cc.LayoutBounds.Height = 1.0f / 3.0f;
                        cc.BufferIndex = renderTextureIndex;
                    }
                }
                // マスクの制限枚数を超えた場合の処理
                else
                {
                    var count = usingClipCount - useClippingMaskMaxCount;

                    // 開発モードの場合は停止させる
                    throw new Exception(
                        $"not supported mask count : {count}\n[Details] render texture count: {RenderTextureCount}\n, mask count : {usingClipCount}"
                    );
                }
            }
        }
    }

    /// <summary>
    ///     マスクされる描画オブジェクト群全体を囲む矩形(モデル座標系)を計算する
    /// </summary>
    /// <param name="model">モデルのインスタンス</param>
    /// <param name="clippingContext">クリッピングマスクのコンテキスト</param>
    protected unsafe void CalcClippedDrawTotalBounds(
        CubismModel model,
        CubismClippingContext clippingContext
    )
    {
        // 被クリッピングマスク（マスクされる描画オブジェクト）の全体の矩形
        float clippedDrawTotalMinX = float.MaxValue,
            clippedDrawTotalMinY = float.MaxValue;

        float clippedDrawTotalMaxX = float.MinValue,
            clippedDrawTotalMaxY = float.MinValue;

        // このマスクが実際に必要か判定する
        // このクリッピングを利用する「描画オブジェクト」がひとつでも使用可能であればマスクを生成する必要がある

        var clippedDrawCount = clippingContext.ClippedDrawableIndexList.Count;
        for (
            var clippedDrawableIndex = 0;
            clippedDrawableIndex < clippedDrawCount;
            clippedDrawableIndex++
        )
        {
            // マスクを使用する描画オブジェクトの描画される矩形を求める
            var drawableIndex = clippingContext.ClippedDrawableIndexList[clippedDrawableIndex];

            var drawableVertexCount = model.GetDrawableVertexCount(drawableIndex);
            var drawableVertexes = model.GetDrawableVertices(drawableIndex);

            float minX = float.MaxValue,
                minY = float.MaxValue;

            float maxX = float.MinValue,
                maxY = float.MinValue;

            var loop = drawableVertexCount * CubismFramework.VertexStep;
            for (var pi = CubismFramework.VertexOffset; pi < loop; pi += CubismFramework.VertexStep)
            {
                var x = drawableVertexes[pi];
                var y = drawableVertexes[pi + 1];
                if (x < minX)
                {
                    minX = x;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }

            //
            if (minX == float.MaxValue)
            {
                continue; //有効な点がひとつも取れなかったのでスキップする
            }

            // 全体の矩形に反映
            if (minX < clippedDrawTotalMinX)
            {
                clippedDrawTotalMinX = minX;
            }

            if (minY < clippedDrawTotalMinY)
            {
                clippedDrawTotalMinY = minY;
            }

            if (maxX > clippedDrawTotalMaxX)
            {
                clippedDrawTotalMaxX = maxX;
            }

            if (maxY > clippedDrawTotalMaxY)
            {
                clippedDrawTotalMaxY = maxY;
            }
        }

        if (clippedDrawTotalMinX == float.MaxValue)
        {
            clippingContext.AllClippedDrawRect.X = 0.0f;
            clippingContext.AllClippedDrawRect.Y = 0.0f;
            clippingContext.AllClippedDrawRect.Width = 0.0f;
            clippingContext.AllClippedDrawRect.Height = 0.0f;
            clippingContext.IsUsing = false;
        }
        else
        {
            clippingContext.IsUsing = true;
            var w = clippedDrawTotalMaxX - clippedDrawTotalMinX;
            var h = clippedDrawTotalMaxY - clippedDrawTotalMinY;
            clippingContext.AllClippedDrawRect.X = clippedDrawTotalMinX;
            clippingContext.AllClippedDrawRect.Y = clippedDrawTotalMinY;
            clippingContext.AllClippedDrawRect.Width = w;
            clippingContext.AllClippedDrawRect.Height = h;
        }
    }

    /// <summary>
    ///     カラーチャンネル(RGBA)のフラグを取得する
    /// </summary>
    /// <param name="channelNo">カラーチャンネル(RGBA)の番号(0:R , 1:G , 2:B, 3:A)</param>
    /// <returns></returns>
    public CubismTextureColor GetChannelFlagAsColor(int channelNo)
    {
        return ChannelColors[channelNo];
    }

    /// <summary>
    ///     クリッピングマスクバッファのサイズを設定する
    /// </summary>
    /// <param name="width">クリッピングマスクバッファのサイズ</param>
    /// <param name="height">クリッピングマスクバッファのサイズ</param>
    public void SetClippingMaskBufferSize(float width, float height)
    {
        ClippingMaskBufferSize = new Vector2(width, height);
    }
}
