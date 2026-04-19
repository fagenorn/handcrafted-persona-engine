namespace PersonaEngine.Lib.Bootstrapper.Planner;

public sealed class AssetPlan
{
    public IReadOnlyList<AssetPlanItem> Items { get; }

    public bool IsEmpty => Items.All(i => i.Action == AssetAction.Skip);

    public long RequiredBytes =>
        Items
            .Where(i => i.Action is AssetAction.Download or AssetAction.Redownload)
            .Sum(i => i.Entry.SizeBytes);

    public AssetPlan(IReadOnlyList<AssetPlanItem> items) => Items = items;
}
