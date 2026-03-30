using PersonaEngine.Lib.Live2D.Framework.Math;
using PersonaEngine.Lib.Live2D.Framework.Model;

namespace PersonaEngine.Lib.Live2D.Framework.Rendering;

/// <summary>
///     モデル描画を処理するレンダラ
///     サブクラスに環境依存の描画命令を記述する
/// </summary>
public abstract class CubismRenderer : IDisposable
{
    /// <summary>
    ///     Model-View-Projection 行列
    /// </summary>
    private readonly CubismMatrix44 _mvpMatrix4x4 = new();

    public CubismTextureColor ClearColor = new(0, 0, 0, 0);

    /// <summary>
    ///     モデル自体のカラー(RGBA)
    /// </summary>
    public CubismTextureColor ModelColor = new();

    /// <summary>
    ///     レンダラのインスタンスを生成して取得する
    /// </summary>
    public CubismRenderer(CubismModel model)
    {
        _mvpMatrix4x4.LoadIdentity();
        Model = model ?? throw new Exception("model is null");
    }

    /// <summary>
    ///     テクスチャの異方性フィルタリングのパラメータ
    /// </summary>
    public float Anisotropy { get; set; }

    /// <summary>
    ///     レンダリング対象のモデル
    /// </summary>
    public CubismModel Model { get; private set; }

    /// <summary>
    ///     乗算済みαならtrue
    /// </summary>
    public bool IsPremultipliedAlpha { get; set; }

    /// <summary>
    ///     カリングが有効ならtrue
    /// </summary>
    public bool IsCulling { get; set; }

    /// <summary>
    ///     falseの場合、マスクを纏めて描画する trueの場合、マスクはパーツ描画ごとに書き直す
    /// </summary>
    public bool UseHighPrecisionMask { get; set; }

    /// <summary>
    ///     レンダラのインスタンスを解放する
    /// </summary>
    public abstract void Dispose();

    /// <summary>
    ///     モデルを描画する
    /// </summary>
    public void DrawModel()
    {
        /**
         * DoDrawModelの描画前と描画後に以下の関数を呼んでください。
         * ・SaveProfile();
         * ・RestoreProfile();
         * これはレンダラの描画設定を保存・復帰させることで、
         * モデル描画直前の状態に戻すための処理です。
         */

        SaveProfile();

        DoDrawModel();

        RestoreProfile();
    }

    /// <summary>
    ///     Model-View-Projection 行列をセットする
    ///     配列は複製されるので元の配列は外で破棄して良い
    /// </summary>
    /// <param name="matrix4x4">Model-View-Projection 行列</param>
    public void SetMvpMatrix(CubismMatrix44 matrix4x4)
    {
        _mvpMatrix4x4.SetMatrix(matrix4x4.Tr);
    }

    /// <summary>
    ///     Model-View-Projection 行列を取得する
    /// </summary>
    /// <returns>Model-View-Projection 行列</returns>
    public CubismMatrix44 GetMvpMatrix()
    {
        return _mvpMatrix4x4;
    }

    /// <summary>
    ///     透明度を考慮したモデルの色を計算する。
    /// </summary>
    /// <param name="opacity">透明度</param>
    /// <returns>RGBAのカラー情報</returns>
    public CubismTextureColor GetModelColorWithOpacity(float opacity)
    {
        CubismTextureColor modelColorRGBA = new(ModelColor);
        modelColorRGBA.A *= opacity;
        if (IsPremultipliedAlpha)
        {
            modelColorRGBA.R *= modelColorRGBA.A;
            modelColorRGBA.G *= modelColorRGBA.A;
            modelColorRGBA.B *= modelColorRGBA.A;
        }

        return modelColorRGBA;
    }

    /// <summary>
    ///     モデルの色をセットする。
    ///     各色0.0f～1.0fの間で指定する(1.0fが標準の状態）。
    /// </summary>
    /// <param name="red">赤チャンネルの値</param>
    /// <param name="green">緑チャンネルの値</param>
    /// <param name="blue">青チャンネルの値</param>
    /// <param name="alpha">αチャンネルの値</param>
    public void SetModelColor(float red, float green, float blue, float alpha)
    {
        if (red < 0.0f)
        {
            red = 0.0f;
        }
        else if (red > 1.0f)
        {
            red = 1.0f;
        }

        if (green < 0.0f)
        {
            green = 0.0f;
        }
        else if (green > 1.0f)
        {
            green = 1.0f;
        }

        if (blue < 0.0f)
        {
            blue = 0.0f;
        }
        else if (blue > 1.0f)
        {
            blue = 1.0f;
        }

        if (alpha < 0.0f)
        {
            alpha = 0.0f;
        }
        else if (alpha > 1.0f)
        {
            alpha = 1.0f;
        }

        ModelColor.R = red;
        ModelColor.G = green;
        ModelColor.B = blue;
        ModelColor.A = alpha;
    }

    /// <summary>
    ///     モデル描画の実装
    /// </summary>
    protected abstract void DoDrawModel();

    /// <summary>
    ///     描画オブジェクト（アートメッシュ）を描画する。
    ///     ポリゴンメッシュとテクスチャ番号をセットで渡す。
    /// </summary>
    /// <param name="textureNo">描画するテクスチャ番号</param>
    /// <param name="indexCount">描画オブジェクトのインデックス値</param>
    /// <param name="vertexCount">ポリゴンメッシュの頂点数</param>
    /// <param name="indexArray">ポリゴンメッシュ頂点のインデックス配列</param>
    /// <param name="vertexArray">ポリゴンメッシュの頂点配列</param>
    /// <param name="uvArray">uv配列</param>
    /// <param name="opacity">不透明度</param>
    /// <param name="colorBlendMode">カラーブレンディングのタイプ</param>
    /// <param name="invertedMask">マスク使用時のマスクの反転使用</param>
    internal abstract unsafe void DrawMesh(
        int textureNo,
        int indexCount,
        int vertexCount,
        ushort* indexArray,
        float* vertexArray,
        float* uvArray,
        float opacity,
        CubismBlendMode colorBlendMode,
        bool invertedMask
    );

    /// <summary>
    ///     モデル描画直前のレンダラのステートを保持する
    /// </summary>
    protected abstract void SaveProfile();

    /// <summary>
    ///     モデル描画直前のレンダラのステートを復帰させる
    /// </summary>
    protected abstract void RestoreProfile();
}
