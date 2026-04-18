namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

/// <summary>
///     Owns the "scan a directory for names, present them as a combo, keep the
///     saved name selected even when it's vanished from disk" pattern. Used by
///     the Avatar panel (Live2D models) and the Subtitles panel (fonts).
///     <para>
///         When the saved name is not returned by the scan, a synthetic
///         <c>"name  (not found)"</c> entry is prepended so the combo can still
///         display it. <see cref="StripSuffix" /> strips the suffix back off any
///         selected entry before writing to config.
///     </para>
/// </summary>
internal sealed class ScannedNamePicker
{
    public const string MissingSuffix = "  (not found)";

    private readonly Func<IEnumerable<string>> _scan;

    private string[] _choices = Array.Empty<string>();

    private bool _isMissing;

    public ScannedNamePicker(Func<IEnumerable<string>> scan)
    {
        _scan = scan ?? throw new ArgumentNullException(nameof(scan));
    }

    /// <summary>The full combo list, including any synthetic "(not found)" entry.</summary>
    public IReadOnlyList<string> Choices => _choices;

    /// <summary>Whether the saved name is absent from the underlying scan.</summary>
    public bool IsMissing => _isMissing;

    /// <summary>
    ///     Whether the scan returned nothing real (either truly empty, or only
    ///     the synthetic "(not found)" placeholder). Callers can use this to
    ///     decide whether to render an empty-state message.
    /// </summary>
    public bool IsEmpty =>
        _choices.Length == 0
        || (_choices.Length == 1 && _choices[0].EndsWith(MissingSuffix, StringComparison.Ordinal));

    /// <summary>
    ///     Full rescan. Call from the consuming section's ctor, from the Refresh
    ///     click handler, or when the source folder (or other scan input) changed.
    /// </summary>
    public void Refresh(string? savedName)
    {
        var discovered = SafeScan();

        if (
            !string.IsNullOrEmpty(savedName)
            && !discovered.Any(n => string.Equals(n, savedName, StringComparison.Ordinal))
        )
        {
            // Saved name not on disk — prepend a synthetic "(not found)" entry so
            // the combo can still display the saved name.
            var prefixed = new List<string>(discovered.Count + 1) { savedName + MissingSuffix };
            prefixed.AddRange(discovered);
            _choices = prefixed.ToArray();
            _isMissing = true;
        }
        else
        {
            _choices = discovered.ToArray();
            _isMissing = false;
        }
    }

    /// <summary>
    ///     Cheap resync when the saved name changed but the underlying source did not.
    ///     Avoids a full rescan; triggers one if the missing/present flag flipped and
    ///     the choices array no longer matches reality.
    /// </summary>
    public void RecomputeMissing(string? savedName)
    {
        var present =
            !string.IsNullOrEmpty(savedName)
            && _choices.Any(entry =>
                !entry.EndsWith(MissingSuffix, StringComparison.Ordinal)
                && string.Equals(entry, savedName, StringComparison.Ordinal)
            );

        _isMissing = !present && !string.IsNullOrEmpty(savedName);

        var hasMissingEntry = _choices.Any(e =>
            e.EndsWith(MissingSuffix, StringComparison.Ordinal)
        );

        // Flag no longer matches the choices array — rescan so the combo reflects it.
        if (_isMissing != hasMissingEntry)
        {
            Refresh(savedName);
        }
    }

    /// <summary>
    ///     Index into <see cref="Choices" /> whose stripped value matches
    ///     <paramref name="savedName" />, or 0 if no entry matches.
    /// </summary>
    public int IndexOf(string? savedName)
    {
        if (_choices.Length == 0 || string.IsNullOrEmpty(savedName))
        {
            return 0;
        }

        for (var i = 0; i < _choices.Length; i++)
        {
            if (string.Equals(StripSuffix(_choices[i]), savedName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    ///     Strip the <see cref="MissingSuffix" /> from a chosen entry, if present.
    ///     Safe to call on any entry — returns the input unchanged if no suffix.
    /// </summary>
    public static string StripSuffix(string entry) =>
        entry.EndsWith(MissingSuffix, StringComparison.Ordinal)
            ? entry[..^MissingSuffix.Length]
            : entry;

    private List<string> SafeScan()
    {
        try
        {
            return _scan().Where(n => !string.IsNullOrEmpty(n)).ToList();
        }
        catch (Exception)
        {
            // A transient IO error (permission, handle churn, directory missing)
            // should never crash the render thread. Fall back to empty and let
            // the user retry via the Refresh button.
            return [];
        }
    }
}
