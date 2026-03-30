namespace PersonaEngine.Lib.Live2D.Framework.Id;

/// <summary>
///     ID名を管理する。
/// </summary>
public class CubismIdManager
{
    /// <summary>
    ///     登録されているIDのリスト
    /// </summary>
    private readonly List<string> _ids = [];

    /// <summary>
    ///     ID名をリストから登録する。
    /// </summary>
    /// <param name="list">ID名リスト</param>
    public void RegisterIds(List<string> list)
    {
        list.ForEach(item =>
        {
            GetId(item);
        });
    }

    /// <summary>
    ///     ID名からIDを取得する。
    ///     未登録のID名の場合、登録も行う。
    /// </summary>
    /// <param name="item">ID名</param>
    public string GetId(string item)
    {
        if (_ids.Contains(item))
        {
            return item;
        }

        _ids.Add(item);

        return item;
    }
}
