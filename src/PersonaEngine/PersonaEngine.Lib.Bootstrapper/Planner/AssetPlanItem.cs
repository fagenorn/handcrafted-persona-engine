using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Bootstrapper.Planner;

public sealed record AssetPlanItem(AssetEntry Entry, AssetAction Action);
