namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Implements configuration section registry
/// </summary>
public class ConfigSectionRegistry : IConfigSectionRegistry
{
    private readonly Dictionary<string, IConfigSectionEditor> _sections = new();

    public void RegisterSection(IConfigSectionEditor section)
    {
        if (section == null)
        {
            throw new ArgumentNullException(nameof(section));
        }

        _sections[section.SectionKey] = section;
    }

    public void UnregisterSection(string sectionKey)
    {
        _sections.Remove(sectionKey);
    }

    public IConfigSectionEditor GetSection(string sectionKey)
    {
        return _sections.TryGetValue(sectionKey, out var section) ? section : null;
    }

    public IReadOnlyList<IConfigSectionEditor> GetSections()
    {
        return _sections.Values.ToList().AsReadOnly();
    }
}
