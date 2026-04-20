using System.Numerics;
using PersonaEngine.Lib.Live2D.Framework.Core;
using PersonaEngine.Lib.Live2D.Framework.Rendering;

namespace PersonaEngine.Lib.Live2D.Framework.Model;

public class CubismModel : IDisposable
{
    /// <summary>
    ///     存在していないパラメータIDのリスト
    /// </summary>
    private readonly Dictionary<string, int> _notExistParameterId = [];

    /// <summary>
    ///     存在していないパラメータの値のリスト
    /// </summary>
    private readonly Dictionary<int, float> _notExistParameterValues = [];

    /// <summary>
    ///     存在していないパーツIDのリスト
    /// </summary>
    private readonly Dictionary<string, int> _notExistPartId = [];

    /// <summary>
    ///     存在していないパーツの不透明度のリスト
    /// </summary>
    private readonly Dictionary<int, float> _notExistPartOpacities = [];

    /// <summary>
    ///     パラメータの最大値のリスト
    /// </summary>
    private readonly unsafe float* _parameterMaximumValues;

    /// <summary>
    ///     パラメータの最小値のリスト
    /// </summary>
    private readonly unsafe float* _parameterMinimumValues;

    /// <summary>
    ///     パラメータの値のリスト
    /// </summary>
    private readonly unsafe float* _parameterValues;

    /// <summary>
    ///     Partの子DrawableIndexの配列
    /// </summary>
    private readonly List<int>[] _partChildDrawables;

    /// <summary>
    ///     パーツの不透明度のリスト
    /// </summary>
    private readonly unsafe float* _partOpacities;

    /// <summary>
    ///     保存されたパラメータ
    /// </summary>
    private readonly List<float> _savedParameters = [];

    /// <summary>
    ///     カリング設定の配列
    /// </summary>
    private readonly List<DrawableCullingData> _userCullings = [];

    /// <summary>
    ///     Drawable スクリーン色の配列
    /// </summary>
    private readonly List<DrawableColorData> _userMultiplyColors = [];

    /// <summary>
    ///     Part スクリーン色の配列
    /// </summary>
    private readonly List<PartColorData> _userPartMultiplyColors = [];

    /// <summary>
    ///     Part 乗算色の配列
    /// </summary>
    private readonly List<PartColorData> _userPartScreenColors = [];

    /// <summary>
    ///     Drawable 乗算色の配列
    /// </summary>
    private readonly List<DrawableColorData> _userScreenColors = [];

    public readonly List<string> DrawableIds = [];

    public readonly List<string> ParameterIds = [];

    public readonly List<string> PartIds = [];

    /// <summary>
    ///     モデルのカリング設定をすべて上書きするか？
    /// </summary>
    private bool _isOverwrittenCullings;

    /// <summary>
    ///     乗算色を全て上書きするか？
    /// </summary>
    private bool _isOverwrittenModelMultiplyColors;

    /// <summary>
    ///     スクリーン色を全て上書きするか？
    /// </summary>
    private bool _isOverwrittenModelScreenColors;

    /// <summary>
    ///     モデルの不透明度
    /// </summary>
    private float _modelOpacity;

    public unsafe CubismModel(IntPtr model)
    {
        Model = model;
        _modelOpacity = 1.0f;

        _parameterValues = CubismCore.GetParameterValues(Model);
        _partOpacities = CubismCore.GetPartOpacities(Model);
        _parameterMaximumValues = CubismCore.GetParameterMaximumValues(Model);
        _parameterMinimumValues = CubismCore.GetParameterMinimumValues(Model);

        {
            var parameterIds = CubismCore.GetParameterIds(Model);
            var parameterCount = CubismCore.GetParameterCount(Model);

            for (var i = 0; i < parameterCount; ++i)
            {
                var str = new string(parameterIds[i]);
                ParameterIds.Add(CubismFramework.CubismIdManager.GetId(str));
            }
        }

        var partCount = CubismCore.GetPartCount(Model);
        var partIds = CubismCore.GetPartIds(Model);

        _partChildDrawables = new List<int>[partCount];
        for (var i = 0; i < partCount; ++i)
        {
            var str = new string(partIds[i]);
            PartIds.Add(CubismFramework.CubismIdManager.GetId(str));
            _partChildDrawables[i] = [];
        }

        var drawableIds = CubismCore.GetDrawableIds(Model);
        var drawableCount = CubismCore.GetDrawableCount(Model);

        // カリング設定
        var userCulling = new DrawableCullingData { IsOverwritten = false, IsCulling = false };

        // 乗算色
        var multiplyColor = new CubismTextureColor();

        // スクリーン色
        var screenColor = new CubismTextureColor(0, 0, 0, 1.0f);

        // Parts
        for (var i = 0; i < partCount; ++i)
        {
            _userPartMultiplyColors.Add(
                new PartColorData
                {
                    IsOverwritten = false,
                    Color = multiplyColor, // 乗算色
                }
            );

            _userPartScreenColors.Add(
                new PartColorData
                {
                    IsOverwritten = false,
                    Color = screenColor, // スクリーン色
                }
            );
        }

        // Drawables
        for (var i = 0; i < drawableCount; ++i)
        {
            var str = new string(drawableIds[i]);
            DrawableIds.Add(CubismFramework.CubismIdManager.GetId(str));
            _userMultiplyColors.Add(
                new DrawableColorData
                {
                    IsOverwritten = false,
                    Color = multiplyColor, // 乗算色
                }
            );

            _userScreenColors.Add(
                new DrawableColorData
                {
                    IsOverwritten = false,
                    Color = screenColor, // スクリーン色
                }
            );

            _userCullings.Add(userCulling);

            var parentIndex = CubismCore.GetDrawableParentPartIndices(Model)[i];
            if (parentIndex >= 0)
            {
                _partChildDrawables[parentIndex].Add(i);
            }
        }
    }

    /// <summary>
    ///     モデル
    /// </summary>
    public IntPtr Model { get; }

    public void Dispose()
    {
        CubismFramework.DeallocateAligned(Model);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     partのOverwriteColor Set関数
    /// </summary>
    public void SetPartColor(
        int partIndex,
        float r,
        float g,
        float b,
        float a,
        List<PartColorData> partColors,
        List<DrawableColorData> drawableColors
    )
    {
        partColors[partIndex].Color.R = r;
        partColors[partIndex].Color.G = g;
        partColors[partIndex].Color.B = b;
        partColors[partIndex].Color.A = a;

        if (partColors[partIndex].IsOverwritten)
        {
            for (var i = 0; i < _partChildDrawables[partIndex].Count; i++)
            {
                var drawableIndex = _partChildDrawables[partIndex][i];
                drawableColors[drawableIndex].Color.R = r;
                drawableColors[drawableIndex].Color.G = g;
                drawableColors[drawableIndex].Color.B = b;
                drawableColors[drawableIndex].Color.A = a;
            }
        }
    }

    /// <summary>
    ///     partのOverwriteFlag Set関数
    /// </summary>
    public void SetOverwriteColorForPartColors(
        int partIndex,
        bool value,
        List<PartColorData> partColors,
        List<DrawableColorData> drawableColors
    )
    {
        partColors[partIndex].IsOverwritten = value;

        for (var i = 0; i < _partChildDrawables[partIndex].Count; i++)
        {
            var drawableIndex = _partChildDrawables[partIndex][i];
            drawableColors[drawableIndex].IsOverwritten = value;
            if (value)
            {
                drawableColors[drawableIndex].Color.R = partColors[partIndex].Color.R;
                drawableColors[drawableIndex].Color.G = partColors[partIndex].Color.G;
                drawableColors[drawableIndex].Color.B = partColors[partIndex].Color.B;
                drawableColors[drawableIndex].Color.A = partColors[partIndex].Color.A;
            }
        }
    }

    /// <summary>
    ///     モデルのパラメータを更新する。
    /// </summary>
    public void Update()
    {
        // Update model.
        CubismCore.UpdateModel(Model);
        // Reset dynamic drawable flags.
        CubismCore.ResetDrawableDynamicFlags(Model);
    }

    /// <summary>
    ///     Pixel単位でキャンバスの幅の取得
    /// </summary>
    /// <returns>キャンバスの幅(pixel)</returns>
    public float GetCanvasWidthPixel()
    {
        if (Model == IntPtr.Zero)
        {
            return 0.0f;
        }

        CubismCore.ReadCanvasInfo(Model, out var tmpSizeInPixels, out _, out _);

        return tmpSizeInPixels.X;
    }

    /// <summary>
    ///     Pixel単位でキャンバスの高さの取得
    /// </summary>
    /// <returns>キャンバスの高さ(pixel)</returns>
    public float GetCanvasHeightPixel()
    {
        if (new IntPtr(Model) == IntPtr.Zero)
        {
            return 0.0f;
        }

        CubismCore.ReadCanvasInfo(Model, out var tmpSizeInPixels, out _, out _);

        return tmpSizeInPixels.Y;
    }

    /// <summary>
    ///     PixelsPerUnitを取得する。
    /// </summary>
    /// <returns>PixelsPerUnit</returns>
    public float GetPixelsPerUnit()
    {
        if (new IntPtr(Model) == IntPtr.Zero)
        {
            return 0.0f;
        }

        CubismCore.ReadCanvasInfo(Model, out _, out _, out var tmpPixelsPerUnit);

        return tmpPixelsPerUnit;
    }

    /// <summary>
    ///     Unit単位でキャンバスの幅の取得
    /// </summary>
    /// <returns>キャンバスの幅(Unit)</returns>
    public float GetCanvasWidth()
    {
        CubismCore.ReadCanvasInfo(Model, out var tmpSizeInPixels, out _, out var tmpPixelsPerUnit);

        return tmpSizeInPixels.X / tmpPixelsPerUnit;
    }

    /// <summary>
    ///     Unit単位でキャンバスの高さの取得
    /// </summary>
    /// <returns>キャンバスの高さ(Unit)</returns>
    public float GetCanvasHeight()
    {
        CubismCore.ReadCanvasInfo(Model, out var tmpSizeInPixels, out _, out var tmpPixelsPerUnit);

        return tmpSizeInPixels.Y / tmpPixelsPerUnit;
    }

    /// <summary>
    ///     パーツのインデックスを取得する。
    /// </summary>
    /// <param name="partId">パーツのID</param>
    /// <returns>パーツのインデックス</returns>
    public int GetPartIndex(string partId)
    {
        var partIndex = PartIds.IndexOf(partId);
        if (partIndex != -1)
        {
            return partIndex;
        }

        var partCount = CubismCore.GetPartCount(Model);

        // モデルに存在していない場合、非存在パーツIDリスト内にあるかを検索し、そのインデックスを返す
        if (_notExistPartId.TryGetValue(partId, out var item))
        {
            return item;
        }

        // 非存在パーツIDリストにない場合、新しく要素を追加する
        partIndex = partCount + _notExistPartId.Count;

        _notExistPartId.TryAdd(partId, partIndex);
        _notExistPartOpacities.Add(partIndex, 0);

        return partIndex;
    }

    /// <summary>
    ///     パーツのIDを取得する。
    /// </summary>
    /// <param name="partIndex">パーツのIndex</param>
    /// <returns>パーツのID</returns>
    public string GetPartId(int partIndex)
    {
        if (0 <= partIndex && partIndex < PartIds.Count)
        {
            throw new IndexOutOfRangeException("Out of PartIds size");
        }

        return PartIds[partIndex];
    }

    /// <summary>
    ///     パーツの個数を取得する。
    /// </summary>
    /// <returns>パーツの個数</returns>
    public int GetPartCount()
    {
        return CubismCore.GetPartCount(Model);
    }

    /// <summary>
    ///     パーツの不透明度を設定する。
    /// </summary>
    /// <param name="partId">パーツのID</param>
    /// <param name="opacity">不透明度</param>
    public void SetPartOpacity(string partId, float opacity)
    {
        // 高速化のためにPartIndexを取得できる機構になっているが、外部からの設定の時は呼び出し頻度が低いため不要
        var index = GetPartIndex(partId);

        if (index < 0)
        {
            return; // パーツが無いのでスキップ
        }

        SetPartOpacity(index, opacity);
    }

    /// <summary>
    ///     パーツの不透明度を設定する。
    /// </summary>
    /// <param name="partIndex">パーツのインデックス</param>
    /// <param name="opacity">パーツの不透明度</param>
    public unsafe void SetPartOpacity(int partIndex, float opacity)
    {
        if (_notExistPartOpacities.ContainsKey(partIndex))
        {
            _notExistPartOpacities[partIndex] = opacity;

            return;
        }

        //インデックスの範囲内検知
        if (0 > partIndex || partIndex >= GetPartCount())
        {
            throw new ArgumentException("partIndex out of range");
        }

        _partOpacities[partIndex] = opacity;
    }

    /// <summary>
    ///     パーツの不透明度を取得する。
    /// </summary>
    /// <param name="partId">パーツのID</param>
    /// <returns>パーツの不透明度</returns>
    public float GetPartOpacity(string partId)
    {
        // 高速化のためにPartIndexを取得できる機構になっているが、外部からの設定の時は呼び出し頻度が低いため不要
        var index = GetPartIndex(partId);

        if (index < 0)
        {
            return 0; //パーツが無いのでスキップ
        }

        return GetPartOpacity(index);
    }

    /// <summary>
    ///     パーツの不透明度を取得する。
    /// </summary>
    /// <param name="partIndex">パーツのインデックス</param>
    /// <returns>パーツの不透明度</returns>
    public unsafe float GetPartOpacity(int partIndex)
    {
        if (_notExistPartOpacities.TryGetValue(partIndex, out var value))
        {
            // モデルに存在しないパーツIDの場合、非存在パーツリストから不透明度を返す
            return value;
        }

        //インデックスの範囲内検知
        if (0 > partIndex || partIndex >= GetPartCount())
        {
            throw new ArgumentException("partIndex out of range");
        }

        return _partOpacities[partIndex];
    }

    /// <summary>
    ///     パラメータのインデックスを取得する。
    /// </summary>
    /// <param name="parameterId">パラメータID</param>
    /// <returns>パラメータのインデックス</returns>
    public int GetParameterIndex(string parameterId)
    {
        var parameterIndex = ParameterIds.IndexOf(parameterId);
        if (parameterIndex != -1)
        {
            return parameterIndex;
        }

        // モデルに存在していない場合、非存在パラメータIDリスト内を検索し、そのインデックスを返す
        if (_notExistParameterId.TryGetValue(parameterId, out var data))
        {
            return data;
        }

        // 非存在パラメータIDリストにない場合、新しく要素を追加する
        parameterIndex = CubismCore.GetParameterCount(Model) + _notExistParameterId.Count;

        _notExistParameterId.TryAdd(parameterId, parameterIndex);
        _notExistParameterValues.Add(parameterIndex, 0);

        return parameterIndex;
    }

    /// <summary>
    ///     パラメータの個数を取得する。
    /// </summary>
    /// <returns>パラメータの個数</returns>
    public int GetParameterCount()
    {
        return CubismCore.GetParameterCount(Model);
    }

    /// <summary>
    ///     パラメータの種類を取得する。
    /// </summary>
    /// <param name="parameterIndex">パラメータのインデックス</param>
    /// <returns>
    ///     csmParameterType_Normal -> 通常のパラメータ
    ///     csmParameterType_BlendShape -> ブレンドシェイプパラメータ
    /// </returns>
    public unsafe int GetParameterType(int parameterIndex)
    {
        return CubismCore.GetParameterTypes(Model)[parameterIndex];
    }

    /// <summary>
    ///     パラメータの最大値を取得する。
    /// </summary>
    /// <param name="parameterIndex">パラメータのインデックス</param>
    /// <returns>パラメータの最大値</returns>
    public unsafe float GetParameterMaximumValue(int parameterIndex)
    {
        return CubismCore.GetParameterMaximumValues(Model)[parameterIndex];
    }

    /// <summary>
    ///     パラメータの最小値を取得する。
    /// </summary>
    /// <param name="parameterIndex">パラメータのインデックス</param>
    /// <returns>パラメータの最小値</returns>
    public unsafe float GetParameterMinimumValue(int parameterIndex)
    {
        return CubismCore.GetParameterMinimumValues(Model)[parameterIndex];
    }

    /// <summary>
    ///     パラメータのデフォルト値を取得する。
    /// </summary>
    /// <param name="parameterIndex">パラメータのインデックス</param>
    /// <returns> パラメータのデフォルト値</returns>
    public unsafe float GetParameterDefaultValue(int parameterIndex)
    {
        return CubismCore.GetParameterDefaultValues(Model)[parameterIndex];
    }

    /// <summary>
    ///     パラメータの値を取得する。
    /// </summary>
    /// <param name="parameterId">パラメータID</param>
    /// <returns>パラメータの値</returns>
    public float GetParameterValue(string parameterId)
    {
        // 高速化のためにParameterIndexを取得できる機構になっているが、外部からの設定の時は呼び出し頻度が低いため不要
        var parameterIndex = GetParameterIndex(parameterId);

        return GetParameterValue(parameterIndex);
    }

    /// <summary>
    ///     パラメータの値を取得する。
    /// </summary>
    /// <param name="parameterIndex">パラメータのインデックス</param>
    /// <returns>パラメータの値</returns>
    public unsafe float GetParameterValue(int parameterIndex)
    {
        if (_notExistParameterValues.TryGetValue(parameterIndex, out var item))
        {
            return item;
        }

        //インデックスの範囲内検知
        if (0 > parameterIndex || parameterIndex >= GetParameterCount())
        {
            throw new ArgumentException("parameterIndex out of range");
        }

        return _parameterValues[parameterIndex];
    }

    /// <summary>
    ///     パラメータの値を設定する。
    /// </summary>
    /// <param name="parameterId">パラメータID</param>
    /// <param name="value">パラメータの値</param>
    /// <param name="weight">重み</param>
    public void SetParameterValue(string parameterId, float value, float weight = 1.0f)
    {
        var index = GetParameterIndex(parameterId);
        SetParameterValue(index, value, weight);
    }

    /// <summary>
    ///     パラメータの値を設定する。
    /// </summary>
    /// <param name="parameterIndex">パラメータのインデックス</param>
    /// <param name="value">パラメータの値</param>
    /// <param name="weight">重み</param>
    public unsafe void SetParameterValue(int parameterIndex, float value, float weight = 1.0f)
    {
        if (_notExistParameterValues.TryGetValue(parameterIndex, out var value1))
        {
            _notExistParameterValues[parameterIndex] =
                weight == 1 ? value : value1 * (1 - weight) + value * weight;

            return;
        }

        //インデックスの範囲内検知
        if (0 > parameterIndex || parameterIndex >= GetParameterCount())
        {
            throw new ArgumentException("parameterIndex out of range");
        }

        if (CubismCore.GetParameterMaximumValues(Model)[parameterIndex] < value)
        {
            value = CubismCore.GetParameterMaximumValues(Model)[parameterIndex];
        }

        if (CubismCore.GetParameterMinimumValues(Model)[parameterIndex] > value)
        {
            value = CubismCore.GetParameterMinimumValues(Model)[parameterIndex];
        }

        _parameterValues[parameterIndex] =
            weight == 1
                ? value
                : _parameterValues[parameterIndex] =
                    _parameterValues[parameterIndex] * (1 - weight) + value * weight;
    }

    /// <summary>
    ///     パラメータの値を加算する。
    /// </summary>
    /// <param name="parameterId">パラメータID</param>
    /// <param name="value">加算する値</param>
    /// <param name="weight">重み</param>
    public void AddParameterValue(string parameterId, float value, float weight = 1.0f)
    {
        var index = GetParameterIndex(parameterId);
        AddParameterValue(index, value, weight);
    }

    /// <summary>
    ///     パラメータの値を加算する。
    /// </summary>
    /// <param name="parameterIndex">パラメータのインデックス</param>
    /// <param name="value">加算する値</param>
    /// <param name="weight">重み</param>
    public void AddParameterValue(int parameterIndex, float value, float weight = 1.0f)
    {
        if (parameterIndex == -1)
        {
            return;
        }

        SetParameterValue(parameterIndex, GetParameterValue(parameterIndex) + value * weight);
    }

    /// <summary>
    ///     パラメータの値を乗算する。
    /// </summary>
    /// <param name="parameterId">パラメータID</param>
    /// <param name="value">乗算する値</param>
    /// <param name="weight">重み</param>
    public void MultiplyParameterValue(string parameterId, float value, float weight = 1.0f)
    {
        var index = GetParameterIndex(parameterId);
        MultiplyParameterValue(index, value, weight);
    }

    /// <summary>
    ///     パラメータの値を乗算する。
    /// </summary>
    /// <param name="parameterIndex">パラメータのインデックス</param>
    /// <param name="value">乗算する値</param>
    /// <param name="weight">重み</param>
    public void MultiplyParameterValue(int parameterIndex, float value, float weight = 1.0f)
    {
        if (parameterIndex == -1)
        {
            return;
        }

        SetParameterValue(
            parameterIndex,
            GetParameterValue(parameterIndex) * (1.0f + (value - 1.0f) * weight)
        );
    }

    /// <summary>
    ///     Drawableのインデックスを取得する。
    /// </summary>
    /// <param name="drawableId">DrawableのID</param>
    /// <returns>Drawableのインデックス</returns>
    public int GetDrawableIndex(string drawableId)
    {
        return DrawableIds.IndexOf(drawableId);
    }

    /// <summary>
    ///     Drawableの個数を取得する。
    /// </summary>
    /// <returns>Drawableの個数</returns>
    public int GetDrawableCount()
    {
        return CubismCore.GetDrawableCount(Model);
    }

    /// <summary>
    ///     DrawableのIDを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>DrawableのID</returns>
    public string GetDrawableId(int drawableIndex)
    {
        if (0 <= drawableIndex && drawableIndex < DrawableIds.Count)
        {
            throw new IndexOutOfRangeException("Out of DrawableIds size");
        }

        return DrawableIds[drawableIndex];
    }

    /// <summary>
    ///     Drawableの描画順リストを取得する。
    /// </summary>
    /// <returns>Drawableの描画順リスト</returns>
    public unsafe int* GetDrawableRenderOrders()
    {
        return CubismCore.GetDrawableRenderOrders(Model);
    }

    /// <summary>
    ///     Drawableのテクスチャインデックスリストの取得
    ///     関数名が誤っていたため、代替となる getDrawableTextureIndex を追加し、この関数は非推奨となりました。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableのテクスチャインデックスリスト</returns>
    public int GetDrawableTextureIndices(int drawableIndex)
    {
        return GetDrawableTextureIndex(drawableIndex);
    }

    /// <summary>
    ///     Drawableのテクスチャインデックスを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableのテクスチャインデックス</returns>
    public unsafe int GetDrawableTextureIndex(int drawableIndex)
    {
        var textureIndices = CubismCore.GetDrawableTextureIndices(Model);

        return textureIndices[drawableIndex];
    }

    /// <summary>
    ///     Drawableの頂点インデックスの個数を取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableの頂点インデックスの個数</returns>
    public unsafe int GetDrawableVertexIndexCount(int drawableIndex)
    {
        return CubismCore.GetDrawableIndexCounts(Model)[drawableIndex];
    }

    /// <summary>
    ///     Drawableの頂点の個数を取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableの頂点の個数</returns>
    public unsafe int GetDrawableVertexCount(int drawableIndex)
    {
        return CubismCore.GetDrawableVertexCounts(Model)[drawableIndex];
    }

    /// <summary>
    ///     Drawableの頂点リストを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableの頂点リスト</returns>
    public unsafe float* GetDrawableVertices(int drawableIndex)
    {
        return (float*)GetDrawableVertexPositions(drawableIndex);
    }

    /// <summary>
    ///     Drawableの頂点インデックスリストを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableの頂点インデックスリスト</returns>
    public unsafe ushort* GetDrawableVertexIndices(int drawableIndex)
    {
        return CubismCore.GetDrawableIndices(Model)[drawableIndex];
    }

    /// <summary>
    ///     Drawableの頂点リストを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableの頂点リスト</returns>
    public unsafe Vector2* GetDrawableVertexPositions(int drawableIndex)
    {
        return CubismCore.GetDrawableVertexPositions(Model)[drawableIndex];
    }

    /// <summary>
    ///     Drawableの頂点のUVリストを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableの頂点のUVリスト</returns>
    public unsafe Vector2* GetDrawableVertexUvs(int drawableIndex)
    {
        return CubismCore.GetDrawableVertexUvs(Model)[drawableIndex];
    }

    /// <summary>
    ///     Drawableの不透明度を取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableの不透明度</returns>
    public unsafe float GetDrawableOpacity(int drawableIndex)
    {
        return CubismCore.GetDrawableOpacities(Model)[drawableIndex];
    }

    /// <summary>
    ///     Drawableの乗算色を取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableの乗算色</returns>
    public unsafe Vector4 GetDrawableMultiplyColor(int drawableIndex)
    {
        return CubismCore.GetDrawableMultiplyColors(Model)[drawableIndex];
    }

    /// <summary>
    ///     Drawableのスクリーン色を取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableのスクリーン色</returns>
    public unsafe Vector4 GetDrawableScreenColor(int drawableIndex)
    {
        return CubismCore.GetDrawableScreenColors(Model)[drawableIndex];
    }

    /// <summary>
    ///     Drawableの親パーツのインデックスを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>drawableの親パーツのインデックス</returns>
    public unsafe int GetDrawableParentPartIndex(int drawableIndex)
    {
        return CubismCore.GetDrawableParentPartIndices(Model)[drawableIndex];
    }

    /// <summary>
    ///     Drawableのブレンドモードを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableのブレンドモード</returns>
    public unsafe CubismBlendMode GetDrawableBlendMode(int drawableIndex)
    {
        var constantFlags = CubismCore.GetDrawableConstantFlags(Model)[drawableIndex];

        return IsBitSet(constantFlags, CsmEnum.CsmBlendAdditive) ? CubismBlendMode.Additive
            : IsBitSet(constantFlags, CsmEnum.CsmBlendMultiplicative)
                ? CubismBlendMode.Multiplicative
            : CubismBlendMode.Normal;
    }

    /// <summary>
    ///     Drawableのマスク使用時の反転設定を取得する。
    ///     マスクを使用しない場合は無視される
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableのマスクの反転設定</returns>
    public unsafe bool GetDrawableInvertedMask(int drawableIndex)
    {
        var constantFlags = CubismCore.GetDrawableConstantFlags(Model)[drawableIndex];

        return IsBitSet(constantFlags, CsmEnum.CsmIsInvertedMask);
    }

    /// <summary>
    ///     Drawableの表示情報を取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>
    ///     true    Drawableが表示
    ///     false   Drawableが非表示
    /// </returns>
    public unsafe bool GetDrawableDynamicFlagIsVisible(int drawableIndex)
    {
        var dynamicFlags = CubismCore.GetDrawableDynamicFlags(Model)[drawableIndex];

        return IsBitSet(dynamicFlags, CsmEnum.CsmIsVisible);
    }

    /// <summary>
    ///     直近のCubismModel::Update関数でDrawableの表示状態が変化したかを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>
    ///     true    Drawableの表示状態が直近のCubismModel::Update関数で変化した
    ///     false   Drawableの表示状態が直近のCubismModel::Update関数で変化していない
    /// </returns>
    public unsafe bool GetDrawableDynamicFlagVisibilityDidChange(int drawableIndex)
    {
        var dynamicFlags = CubismCore.GetDrawableDynamicFlags(Model)[drawableIndex];

        return IsBitSet(dynamicFlags, CsmEnum.CsmVisibilityDidChange);
    }

    /// <summary>
    ///     直近のCubismModel::Update関数でDrawableの不透明度が変化したかを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>
    ///     true    Drawableの不透明度が直近のCubismModel::Update関数で変化した
    ///     false   Drawableの不透明度が直近のCubismModel::Update関数で変化していない
    /// </returns>
    public unsafe bool GetDrawableDynamicFlagOpacityDidChange(int drawableIndex)
    {
        var dynamicFlags = CubismCore.GetDrawableDynamicFlags(Model)[drawableIndex];

        return IsBitSet(dynamicFlags, CsmEnum.CsmOpacityDidChange);
    }

    /// <summary>
    ///     直近のCubismModel::Update関数でDrawableのDrawOrderが変化したかを取得する。
    ///     DrawOrderはArtMesh上で指定する0から1000の情報
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>
    ///     true    Drawableの不透明度が直近のCubismModel::Update関数で変化した
    ///     false   Drawableの不透明度が直近のCubismModel::Update関数で変化していない
    /// </returns>
    public unsafe bool GetDrawableDynamicFlagDrawOrderDidChange(int drawableIndex)
    {
        var dynamicFlags = CubismCore.GetDrawableDynamicFlags(Model)[drawableIndex];

        return IsBitSet(dynamicFlags, CsmEnum.CsmDrawOrderDidChange);
    }

    /// <summary>
    ///     直近のCubismModel::Update関数でDrawableの描画の順序が変化したかを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>
    ///     true    Drawableの描画の順序が直近のCubismModel::Update関数で変化した
    ///     false   Drawableの描画の順序が直近のCubismModel::Update関数で変化していない
    /// </returns>
    public unsafe bool GetDrawableDynamicFlagRenderOrderDidChange(int drawableIndex)
    {
        var dynamicFlags = CubismCore.GetDrawableDynamicFlags(Model)[drawableIndex];

        return IsBitSet(dynamicFlags, CsmEnum.CsmRenderOrderDidChange);
    }

    /// <summary>
    ///     直近のCubismModel::Update関数でDrawableの頂点情報が変化したかを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>
    ///     true    Drawableの頂点情報が直近のCubismModel::Update関数で変化した
    ///     false   Drawableの頂点情報が直近のCubismModel::Update関数で変化していない
    /// </returns>
    public unsafe bool GetDrawableDynamicFlagVertexPositionsDidChange(int drawableIndex)
    {
        var dynamicFlags = CubismCore.GetDrawableDynamicFlags(Model)[drawableIndex];

        return IsBitSet(dynamicFlags, CsmEnum.CsmVertexPositionsDidChange);
    }

    /// <summary>
    ///     直近のCubismModel::Update関数でDrawableの乗算色・スクリーン色が変化したかを取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>
    ///     true    Drawableの乗算色・スクリーン色が直近のCubismModel::Update関数で変化した
    ///     false   Drawableの乗算色・スクリーン色が直近のCubismModel::Update関数で変化していない
    /// </returns>
    public unsafe bool GetDrawableDynamicFlagBlendColorDidChange(int drawableIndex)
    {
        var dynamicFlags = CubismCore.GetDrawableDynamicFlags(Model)[drawableIndex];

        return IsBitSet(dynamicFlags, CsmEnum.CsmBlendColorDidChange);
    }

    /// <summary>
    ///     Drawableのクリッピングマスクリストを取得する。
    /// </summary>
    /// <returns>Drawableのクリッピングマスクリスト</returns>
    public unsafe int** GetDrawableMasks()
    {
        return CubismCore.GetDrawableMasks(Model);
    }

    /// <summary>
    ///     Drawableのクリッピングマスクの個数リストを取得する。
    /// </summary>
    /// <returns>Drawableのクリッピングマスクの個数リスト</returns>
    public unsafe int* GetDrawableMaskCounts()
    {
        return CubismCore.GetDrawableMaskCounts(Model);
    }

    /// <summary>
    ///     クリッピングマスクを使用しているかどうか？
    /// </summary>
    /// <returns>
    ///     true    クリッピングマスクを使用している
    ///     false   クリッピングマスクを使用していない
    /// </returns>
    public unsafe bool IsUsingMasking()
    {
        for (var d = 0; d < CubismCore.GetDrawableCount(Model); ++d)
        {
            if (CubismCore.GetDrawableMaskCounts(Model)[d] <= 0)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    ///     保存されたパラメータを読み込む
    /// </summary>
    public unsafe void LoadParameters()
    {
        var parameterCount = CubismCore.GetParameterCount(Model);
        var savedParameterCount = _savedParameters.Count;

        if (parameterCount > savedParameterCount)
        {
            parameterCount = savedParameterCount;
        }

        for (var i = 0; i < parameterCount; ++i)
        {
            _parameterValues[i] = _savedParameters[i];
        }
    }

    /// <summary>
    ///     パラメータを保存する。
    /// </summary>
    public unsafe void SaveParameters()
    {
        var parameterCount = CubismCore.GetParameterCount(Model);
        var savedParameterCount = _savedParameters.Count;

        if (savedParameterCount != parameterCount)
        {
            _savedParameters.Clear();
            for (var i = 0; i < parameterCount; ++i)
            {
                _savedParameters.Add(_parameterValues[i]);
            }
        }
        else
        {
            for (var i = 0; i < parameterCount; ++i)
            {
                _savedParameters[i] = _parameterValues[i];
            }
        }
    }

    /// <summary>
    ///     drawableの乗算色を取得する
    /// </summary>
    public CubismTextureColor GetMultiplyColor(int drawableIndex)
    {
        if (
            GetOverwriteFlagForModelMultiplyColors()
            || GetOverwriteFlagForDrawableMultiplyColors(drawableIndex)
        )
        {
            return _userMultiplyColors[drawableIndex].Color;
        }

        var color = GetDrawableMultiplyColor(drawableIndex);

        return new CubismTextureColor(color.X, color.Y, color.Z, color.W);
    }

    /// <summary>
    ///     drawableのスクリーン色を取得する
    /// </summary>
    public CubismTextureColor GetScreenColor(int drawableIndex)
    {
        if (
            GetOverwriteFlagForModelScreenColors()
            || GetOverwriteFlagForDrawableScreenColors(drawableIndex)
        )
        {
            return _userScreenColors[drawableIndex].Color;
        }

        var color = GetDrawableScreenColor(drawableIndex);

        return new CubismTextureColor(color.X, color.Y, color.Z, color.W);
    }

    /// <summary>
    ///     drawableの乗算色を設定する
    /// </summary>
    public void SetMultiplyColor(int drawableIndex, CubismTextureColor color)
    {
        SetMultiplyColor(drawableIndex, color.R, color.G, color.B, color.A);
    }

    /// <summary>
    ///     drawableの乗算色を設定する
    /// </summary>
    public void SetMultiplyColor(int drawableIndex, float r, float g, float b, float a = 1.0f)
    {
        _userMultiplyColors[drawableIndex].Color.R = r;
        _userMultiplyColors[drawableIndex].Color.G = g;
        _userMultiplyColors[drawableIndex].Color.B = b;
        _userMultiplyColors[drawableIndex].Color.A = a;
    }

    /// <summary>
    ///     drawableのスクリーン色を設定する
    /// </summary>
    /// <param name="drawableIndex"></param>
    /// <param name="color"></param>
    public void SetScreenColor(int drawableIndex, CubismTextureColor color)
    {
        SetScreenColor(drawableIndex, color.R, color.G, color.B, color.A);
    }

    /// <summary>
    ///     drawableのスクリーン色を設定する
    /// </summary>
    public void SetScreenColor(int drawableIndex, float r, float g, float b, float a = 1.0f)
    {
        _userScreenColors[drawableIndex].Color.R = r;
        _userScreenColors[drawableIndex].Color.G = g;
        _userScreenColors[drawableIndex].Color.B = b;
        _userScreenColors[drawableIndex].Color.A = a;
    }

    /// <summary>
    ///     partの乗算色を取得する
    /// </summary>
    public CubismTextureColor GetPartMultiplyColor(int partIndex)
    {
        return _userPartMultiplyColors[partIndex].Color;
    }

    /// <summary>
    ///     partの乗算色を取得する
    /// </summary>
    public CubismTextureColor GetPartScreenColor(int partIndex)
    {
        return _userPartScreenColors[partIndex].Color;
    }

    /// <summary>
    ///     partのスクリーン色を設定する
    /// </summary>
    public void SetPartMultiplyColor(int partIndex, CubismTextureColor color)
    {
        SetPartMultiplyColor(partIndex, color.R, color.G, color.B, color.A);
    }

    /// <summary>
    ///     partの乗算色を設定する
    /// </summary>
    public void SetPartMultiplyColor(int partIndex, float r, float g, float b, float a = 1.0f)
    {
        SetPartColor(partIndex, r, g, b, a, _userPartMultiplyColors, _userMultiplyColors);
    }

    /// <summary>
    ///     partのスクリーン色を設定する
    /// </summary>
    public void SetPartScreenColor(int partIndex, CubismTextureColor color)
    {
        SetPartScreenColor(partIndex, color.R, color.G, color.B, color.A);
    }

    /// <summary>
    ///     partのスクリーン色を設定する
    /// </summary>
    /// <param name="a"></param>
    public void SetPartScreenColor(int partIndex, float r, float g, float b, float a = 1.0f)
    {
        SetPartColor(partIndex, r, g, b, a, _userPartScreenColors, _userScreenColors);
    }

    /// <summary>
    ///     SDKからモデル全体の乗算色を上書きするか。
    /// </summary>
    /// <returns>
    ///     true    ->  SDK上の色情報を使用
    ///     false   ->  モデルの色情報を使用
    /// </returns>
    public bool GetOverwriteFlagForModelMultiplyColors()
    {
        return _isOverwrittenModelMultiplyColors;
    }

    /// <summary>
    ///     SDKからモデル全体のスクリーン色を上書きするか。
    /// </summary>
    /// <returns>
    ///     true    ->  SDK上の色情報を使用
    ///     false   ->  モデルの色情報を使用
    /// </returns>
    public bool GetOverwriteFlagForModelScreenColors()
    {
        return _isOverwrittenModelScreenColors;
    }

    /// <summary>
    ///     SDKからモデル全体の乗算色を上書きするかをセットする
    ///     SDK上の色情報を使うならtrue、モデルの色情報を使うならfalse
    /// </summary>
    public void SetOverwriteFlagForModelMultiplyColors(bool value)
    {
        _isOverwrittenModelMultiplyColors = value;
    }

    /// <summary>
    ///     SDKからモデル全体のスクリーン色を上書きするかをセットする
    ///     SDK上の色情報を使うならtrue、モデルの色情報を使うならfalse
    /// </summary>
    public void SetOverwriteFlagForModelScreenColors(bool value)
    {
        _isOverwrittenModelScreenColors = value;
    }

    /// <summary>
    ///     SDKからdrawableの乗算色を上書きするか。
    /// </summary>
    /// <returns>
    ///     true    ->  SDK上の色情報を使用
    ///     false   ->  モデルの色情報を使用
    /// </returns>
    public bool GetOverwriteFlagForDrawableMultiplyColors(int drawableIndex)
    {
        return _userMultiplyColors[drawableIndex].IsOverwritten;
    }

    /// <summary>
    ///     SDKからdrawableのスクリーン色を上書きするか。
    /// </summary>
    /// <returns>
    ///     true    ->  SDK上の色情報を使用
    ///     false   ->  モデルの色情報を使用
    /// </returns>
    public bool GetOverwriteFlagForDrawableScreenColors(int drawableIndex)
    {
        return _userScreenColors[drawableIndex].IsOverwritten;
    }

    /// <summary>
    ///     SDKからdrawableの乗算色を上書きするかをセットする
    ///     SDK上の色情報を使うならtrue、モデルの色情報を使うならfalse
    /// </summary>
    public void SetOverwriteFlagForDrawableMultiplyColors(int drawableIndex, bool value)
    {
        _userMultiplyColors[drawableIndex].IsOverwritten = value;
    }

    /// <summary>
    ///     SDKからdrawableのスクリーン色を上書きするかをセットする
    ///     SDK上の色情報を使うならtrue、モデルの色情報を使うならfalse
    /// </summary>
    public void SetOverwriteFlagForDrawableScreenColors(int drawableIndex, bool value)
    {
        _userScreenColors[drawableIndex].IsOverwritten = value;
    }

    /// <summary>
    ///     SDKからpartの乗算色を上書きするか。
    /// </summary>
    /// <returns>
    ///     true    ->  SDK上の色情報を使用
    ///     false   ->  モデルの色情報を使用
    /// </returns>
    public bool GetOverwriteColorForPartMultiplyColors(int partIndex)
    {
        return _userPartMultiplyColors[partIndex].IsOverwritten;
    }

    /// <summary>
    ///     SDKからpartのスクリーン色を上書きするかをセットする
    ///     SDK上の色情報を使うならtrue、モデルの色情報を使うならfalse
    /// </summary>
    public bool GetOverwriteColorForPartScreenColors(int partIndex)
    {
        return _userPartScreenColors[partIndex].IsOverwritten;
    }

    /// <summary>
    ///     Drawableのカリング情報を取得する。
    /// </summary>
    /// <param name="drawableIndex">Drawableのインデックス</param>
    /// <returns>Drawableのカリング情報</returns>
    public unsafe bool GetDrawableCulling(int drawableIndex)
    {
        if (
            GetOverwriteFlagForModelCullings() || GetOverwriteFlagForDrawableCullings(drawableIndex)
        )
        {
            return _userCullings[drawableIndex].IsCulling;
        }

        var constantFlags = CubismCore.GetDrawableConstantFlags(Model);

        return !IsBitSet(constantFlags[drawableIndex], CsmEnum.CsmIsDoubleSided);
    }

    /// <summary>
    ///     Drawableのカリング情報を設定する
    /// </summary>
    public void SetDrawableCulling(int drawableIndex, bool isCulling)
    {
        _userCullings[drawableIndex].IsCulling = isCulling;
    }

    /// <summary>
    ///     SDKからモデル全体のカリング設定を上書きするか。
    /// </summary>
    /// <returns>
    ///     true    ->  SDK上のカリング設定を使用
    ///     false   ->  モデルのカリング設定を使用
    /// </returns>
    public bool GetOverwriteFlagForModelCullings()
    {
        return _isOverwrittenCullings;
    }

    /// <summary>
    ///     SDKからモデル全体のカリング設定を上書きするかをセットする
    ///     SDK上のカリング設定を使うならtrue、モデルのカリング設定を使うならfalse
    /// </summary>
    public void SetOverwriteFlagForModelCullings(bool value)
    {
        _isOverwrittenCullings = value;
    }

    /// <summary>
    ///     SDKからdrawableのカリング設定を上書きするか。
    /// </summary>
    /// <returns>
    ///     true    ->  SDK上のカリング設定を使用
    ///     false   ->  モデルのカリング設定を使用
    /// </returns>
    public bool GetOverwriteFlagForDrawableCullings(int drawableIndex)
    {
        return _userCullings[drawableIndex].IsOverwritten;
    }

    /// <summary>
    ///     SDKからdrawableのカリング設定を上書きするかをセットする
    ///     SDK上のカリング設定を使うならtrue、モデルのカリング設定を使うならfalse
    /// </summary>
    public void SetOverwriteFlagForDrawableCullings(int drawableIndex, bool value)
    {
        _userCullings[drawableIndex].IsOverwritten = value;
    }

    /// <summary>
    ///     モデルの不透明度を取得する
    /// </summary>
    /// <returns>不透明度の値</returns>
    public float GetModelOpacity()
    {
        return _modelOpacity;
    }

    /// <summary>
    ///     モデルの不透明度を設定する
    /// </summary>
    /// <param name="value">不透明度の値</param>
    public void SetModelOpacity(float value)
    {
        _modelOpacity = value;
    }

    private static bool IsBitSet(byte data, byte mask)
    {
        return (data & mask) == mask;
    }

    public void SetOverwriteColorForPartMultiplyColors(int partIndex, bool value)
    {
        _userPartMultiplyColors[partIndex].IsOverwritten = value;
        SetOverwriteColorForPartColors(
            partIndex,
            value,
            _userPartMultiplyColors,
            _userMultiplyColors
        );
    }

    public void SetOverwriteColorForPartScreenColors(int partIndex, bool value)
    {
        _userPartScreenColors[partIndex].IsOverwritten = value;
        SetOverwriteColorForPartColors(partIndex, value, _userPartScreenColors, _userScreenColors);
    }

    /// <summary>
    ///     パラメータのIDを取得する。
    /// </summary>
    /// <param name="parameterIndex">パラメータのIndex</param>
    /// <returns>パラメータのID</returns>
    public string GetParameterId(int parameterIndex)
    {
        if (0 <= parameterIndex && parameterIndex < ParameterIds.Count)
        {
            throw new IndexOutOfRangeException("Out of ParameterIds size");
        }

        return ParameterIds[parameterIndex];
    }
}
