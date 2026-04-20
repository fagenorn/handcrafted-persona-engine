namespace PersonaEngine.Lib.Bootstrapper.Planner;

public sealed class AssetPlan
{
    public IReadOnlyList<AssetPlanItem> Items { get; }

    /// <summary>True when every item is <see cref="AssetAction.Skip" /> — i.e. nothing to do.</summary>
    public bool IsEmpty { get; }

    /// <summary>Total bytes the planner expects to fetch for Download/Redownload items.</summary>
    public long RequiredBytes { get; }

    public AssetPlan(IReadOnlyList<AssetPlanItem> items)
    {
        Items = items;

        // Both flags walk Items once at construction. Every hot caller (progress UI,
        // runner gating) reads these each turn; computing them lazily would re-walk.
        var isEmpty = true;
        long requiredBytes = 0;
        foreach (var item in items)
        {
            if (item.Action != AssetAction.Skip)
                isEmpty = false;
            if (item.Action is AssetAction.Download or AssetAction.Redownload)
                requiredBytes += item.Entry.SizeBytes;
        }
        IsEmpty = isEmpty;
        RequiredBytes = requiredBytes;
    }
}
