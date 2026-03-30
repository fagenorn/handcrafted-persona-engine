using System.Text.Json;

namespace PersonaEngine.Lib.Live2D.Framework.Model;

/// <summary>
///     ユーザデータをロード、管理、検索インターフェイス、解放までを行う。
/// </summary>
public class CubismModelUserData
{
    public const string ArtMesh = "ArtMesh";

    public const string Meta = "Meta";

    public const string UserDataCount = "UserDataCount";

    public const string TotalUserDataSize = "TotalUserDataSize";

    public const string UserData = "UserData";

    public const string Target = "Target";

    public const string Id = "Id";

    public const string Value = "Value";

    /// <summary>
    ///     ユーザデータ構造体配列
    /// </summary>
    private readonly List<CubismModelUserDataNode> _userDataNodes = [];

    /// <summary>
    ///     閲覧リスト保持
    /// </summary>
    public readonly List<CubismModelUserDataNode> ArtMeshUserDataNodes = [];

    /// <summary>
    ///     userdata3.jsonをパースする。
    /// </summary>
    /// <param name="data">userdata3.jsonが読み込まれいるバッファ</param>
    public CubismModelUserData(string data)
    {
        using var stream = File.Open(data, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var obj =
            JsonSerializer.Deserialize(
                stream,
                CubismModelUserDataObjContext.Default.CubismModelUserDataObj
            ) ?? throw new Exception("Load UserData error");

        var typeOfArtMesh = CubismFramework.CubismIdManager.GetId(ArtMesh);

        var nodeCount = obj.Meta.UserDataCount;

        for (var i = 0; i < nodeCount; i++)
        {
            var node = obj.UserData[i];
            CubismModelUserDataNode addNode = new()
            {
                TargetId = CubismFramework.CubismIdManager.GetId(node.Id),
                TargetType = CubismFramework.CubismIdManager.GetId(node.Target),
                Value = CubismFramework.CubismIdManager.GetId(node.Value),
            };
            _userDataNodes.Add(addNode);

            if (addNode.TargetType == typeOfArtMesh)
            {
                ArtMeshUserDataNodes.Add(addNode);
            }
        }
    }
}
