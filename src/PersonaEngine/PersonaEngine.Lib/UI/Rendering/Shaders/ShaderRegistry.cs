using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonaEngine.Lib.UI.Rendering.Shaders;

/// <summary>
///     Packaged-resource loader for all shader sources in the project.
///     Resolves <paramref name="relativePath" /> against
///     <c>AppContext.BaseDirectory/Resources/Shaders/</c>, reads the file once,
///     and caches the result for the process lifetime. Shader sources are
///     validated (ASCII-only) and preprocessed (<c>#include</c> expansion) on
///     first load; subsequent calls return the cached, fully-processed string
///     by reference.
/// </summary>
public static class ShaderRegistry
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    private static readonly Regex IncludeRegex = new(
        "^\\s*#include\\s+\"(?<path>[^\"\\r\\n]+)\"\\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    /// <summary>
    ///     Loads a shader source file from <c>Resources/Shaders/</c>, caching
    ///     the fully-processed result by normalized relative path.
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

        return Cache.GetOrAdd(key, k => LoadAndProcess(k, new HashSet<string>()));
    }

    private static string Normalize(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    private static string LoadAndProcess(string key, HashSet<string> inProgress)
    {
        if (!inProgress.Add(key))
        {
            var chain = string.Join(" -> ", inProgress.Append(key));
            throw new InvalidOperationException($"Circular #include: {chain}");
        }

        try
        {
            var text = ReadFile(key);
            EnsureAscii(key, text);

            return ExpandIncludes(text, inProgress);
        }
        finally
        {
            inProgress.Remove(key);
        }
    }

    private static string ReadFile(string key)
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

    private static string ExpandIncludes(string source, HashSet<string> inProgress)
    {
        return IncludeRegex.Replace(
            source,
            match =>
            {
                var includedKey = Normalize(match.Groups["path"].Value);
                var included = LoadAndProcess(includedKey, inProgress);
                if (!included.EndsWith('\n'))
                {
                    return included + "\n";
                }

                return included;
            }
        );
    }

    /// <summary>
    ///     Rejects any shader source that contains non-ASCII characters. The
    ///     native D3DCompile path marshals the string through the user's ANSI
    ///     code page, so characters like em-dash or degree sign can become
    ///     code-page-dependent bytes on a user's machine and trip "unexpected
    ///     end of file" parse errors that do not reproduce on the author's box.
    /// </summary>
    private static void EnsureAscii(string key, string source)
    {
        var line = 1;
        var column = 1;
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c > 0x7F)
            {
                throw new InvalidOperationException(
                    $"Shader source '{key}' contains non-ASCII character "
                        + $"U+{(int)c:X4} at line {line}, column {column}. "
                        + "Shader sources must be pure ASCII to compile "
                        + "portably across user locales."
                );
            }

            if (c == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
    }
}
