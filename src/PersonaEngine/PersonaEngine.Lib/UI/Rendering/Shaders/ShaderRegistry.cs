using System.Collections.Concurrent;

namespace PersonaEngine.Lib.UI.Rendering.Shaders;

/// <summary>
///     Packaged-resource loader for all shader sources in the project.
///     Resolves <paramref name="relativePath" /> against
///     <c>AppContext.BaseDirectory/Resources/Shaders/</c>, reads the file once,
///     and caches the result for the process lifetime. Shader sources are
///     validated and preprocessed (<c>#include</c> expansion) on first load;
///     subsequent calls return the cached, fully-processed string by reference.
/// </summary>
public static class ShaderRegistry
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>
    ///     Loads a shader source file from <c>Resources/Shaders/</c>, caching
    ///     the result by normalized relative path.
    /// </summary>
    /// <param name="relativePath">
    ///     Forward-slash-delimited path relative to <c>Resources/Shaders/</c>.
    ///     Backslashes are normalized. Leading separators are trimmed.
    /// </param>
    /// <exception cref="InvalidOperationException">
    ///     The file does not exist, contains non-ASCII characters, or has a
    ///     circular <c>#include</c>.
    /// </exception>
    public static string GetSource(string relativePath)
    {
        var key = Normalize(relativePath);

        return Cache.GetOrAdd(key, Load);
    }

    private static string Normalize(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    private static string Load(string key)
    {
        var absolute = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "Shaders",
            key.Replace('/', Path.DirectorySeparatorChar)
        );

        if (!File.Exists(absolute))
        {
            throw new InvalidOperationException(
                $"Shader source '{key}' not found at path: {absolute}"
            );
        }

        return File.ReadAllText(absolute);
    }
}
