namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Data structure for active operations with progress tracking
/// </summary>
public class ActiveOperation
{
    public ActiveOperation(string id, string name)
    {
        Id = id;
        Name = name;
        Progress = 0f;
        CancellationSource = new CancellationTokenSource();
    }

    public string Id { get; }

    public string Name { get; }

    public float Progress { get; set; }

    public CancellationTokenSource CancellationSource { get; }
}
