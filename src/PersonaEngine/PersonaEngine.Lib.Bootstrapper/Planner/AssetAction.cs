namespace PersonaEngine.Lib.Bootstrapper.Planner;

public enum AssetAction
{
    Skip, // already installed and current
    Download, // not installed
    Reverify, // installed; re-hash to confirm
    Redownload, // installed but version mismatch or hash mismatch
    Remove, // installed but not in selected profile anymore
}
