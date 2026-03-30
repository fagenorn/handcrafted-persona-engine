namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Manages registration and access to configuration sections
/// </summary>
public interface IConfigSectionRegistry
{
    void RegisterSection(IConfigSectionEditor section);

    void UnregisterSection(string sectionKey);

    IConfigSectionEditor GetSection(string sectionKey);

    IReadOnlyList<IConfigSectionEditor> GetSections();
}
