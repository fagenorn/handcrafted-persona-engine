using PersonaEngine.Lib.Live2D.Framework.Model;

namespace PersonaEngine.Lib.Live2D.Framework.Rendering.OpenGL;

public class CubismClippingManager_OpenGLES2(OpenGLApi gl)
    : CubismClippingManager(RenderType.OpenGL)
{
    /// <summary>
    ///     クリッピングコンテキストを作成する。モデル描画時に実行する。
    /// </summary>
    /// <param name="model">モデルのインスタンス</param>
    /// <param name="renderer">レンダラのインスタンス</param>
    /// <param name="lastFBO">フレームバッファ</param>
    /// <param name="lastViewport">ビューポート</param>
    internal unsafe void SetupClippingContext(
        CubismModel model,
        CubismRenderer_OpenGLES2 renderer,
        int lastFBO,
        int[] lastViewport
    )
    {
        // 全てのクリッピングを用意する
        // 同じクリップ（複数の場合はまとめて１つのクリップ）を使う場合は１度だけ設定する
        var usingClipCount = 0;
        for (var clipIndex = 0; clipIndex < ClippingContextListForMask.Count; clipIndex++)
        {
            // １つのクリッピングマスクに関して
            var cc = ClippingContextListForMask[clipIndex];

            // このクリップを利用する描画オブジェクト群全体を囲む矩形を計算
            CalcClippedDrawTotalBounds(model, cc!);

            if (cc!.IsUsing)
            {
                usingClipCount++; //使用中としてカウント
            }
        }

        if (usingClipCount <= 0)
        {
            return;
        }

        // マスク作成処理
        // 生成したOffscreenSurfaceと同じサイズでビューポートを設定
        gl.Viewport(0, 0, (int)ClippingMaskBufferSize.X, (int)ClippingMaskBufferSize.Y);

        // 後の計算のためにインデックスの最初をセット
        CurrentMaskBuffer = renderer.GetMaskBuffer(0);
        // ----- マスク描画処理 -----
        CurrentMaskBuffer.BeginDraw(lastFBO);

        renderer.PreDraw(); // バッファをクリアする

        // 各マスクのレイアウトを決定していく
        SetupLayoutBounds(usingClipCount);

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
            var allClippedDrawRect = clipContext!.AllClippedDrawRect; //このマスクを使う、全ての描画オブジェクトの論理座標上の囲み矩形
            var layoutBoundsOnTex01 = clipContext.LayoutBounds; //この中にマスクを収める
            var MARGIN = 0.05f;

            // clipContextに設定したオフスクリーンサーフェイスをインデックスで取得
            var clipContextOffscreenSurface = renderer.GetMaskBuffer(clipContext.BufferIndex);

            // 現在のオフスクリーンサーフェイスがclipContextのものと異なる場合
            if (CurrentMaskBuffer != clipContextOffscreenSurface)
            {
                CurrentMaskBuffer.EndDraw();
                CurrentMaskBuffer = clipContextOffscreenSurface;
                // マスク用RenderTextureをactiveにセット
                CurrentMaskBuffer.BeginDraw(lastFBO);

                // バッファをクリアする。
                renderer.PreDraw();
            }

            // モデル座標上の矩形を、適宜マージンを付けて使う
            TmpBoundsOnModel.SetRect(allClippedDrawRect);
            TmpBoundsOnModel.Expand(
                allClippedDrawRect.Width * MARGIN,
                allClippedDrawRect.Height * MARGIN
            );
            //########## 本来は割り当てられた領域の全体を使わず必要最低限のサイズがよい
            // シェーダ用の計算式を求める。回転を考慮しない場合は以下のとおり
            // movePeriod' = movePeriod * scaleX + offX     [[ movePeriod' = (movePeriod - tmpBoundsOnModel.movePeriod)*scale + layoutBoundsOnTex01.movePeriod ]]
            var scaleX = layoutBoundsOnTex01.Width / TmpBoundsOnModel.Width;
            var scaleY = layoutBoundsOnTex01.Height / TmpBoundsOnModel.Height;

            // マスク生成時に使う行列を求める
            CreateMatrixForMask(false, layoutBoundsOnTex01, scaleX, scaleY);

            clipContext.MatrixForMask.SetMatrix(TmpMatrixForMask.Tr);
            clipContext.MatrixForDraw.SetMatrix(TmpMatrixForDraw.Tr);

            // 実際の描画を行う
            var clipDrawCount = clipContext.ClippingIdCount;
            for (var i = 0; i < clipDrawCount; i++)
            {
                var clipDrawIndex = clipContext.ClippingIdList[i];

                // 頂点情報が更新されておらず、信頼性がない場合は描画をパスする
                if (!model.GetDrawableDynamicFlagVertexPositionsDidChange(clipDrawIndex))
                {
                    continue;
                }

                renderer.IsCulling = model.GetDrawableCulling(clipDrawIndex);

                // マスクがクリアされていないなら処理する
                if (!ClearedMaskBufferFlags[clipContext.BufferIndex])
                {
                    // マスクをクリアする
                    // 1が無効（描かれない）領域、0が有効（描かれる）領域。（シェーダーCd*Csで0に近い値をかけてマスクを作る。1をかけると何も起こらない）
                    gl.ClearColor(1.0f, 1.0f, 1.0f, 1.0f);
                    gl.Clear(gl.GL_COLOR_BUFFER_BIT);
                    ClearedMaskBufferFlags[clipContext.BufferIndex] = true;
                }

                // 今回専用の変換を適用して描く
                // チャンネルも切り替える必要がある(A,R,G,B)
                renderer.ClippingContextBufferForMask = clipContext;

                renderer.DrawMeshOpenGL(model, clipDrawIndex);
            }
        }

        // --- 後処理 ---
        CurrentMaskBuffer.EndDraw();
        renderer.ClippingContextBufferForMask = null;
        gl.Viewport(lastViewport[0], lastViewport[1], lastViewport[2], lastViewport[3]);
    }
}
