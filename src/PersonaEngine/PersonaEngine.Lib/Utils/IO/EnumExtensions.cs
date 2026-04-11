using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;

namespace PersonaEngine.Lib.Utils.IO;

public static class EnumExtensions
{
    // Thread-safe cache for storing descriptions
    private static readonly ConcurrentDictionary<
        (Type Type, string Name),
        string
    > DescriptionCache = new();

    /// <summary>
    ///     Gets the description of an enum value.
    ///     If the enum value has a <see cref="DescriptionAttribute" />, returns its value.
    ///     Otherwise, returns the string representation of the enum value.
    /// </summary>
    /// <param name="value">The enum value.</param>
    /// <returns>The description or the string representation if no description is found.</returns>
    public static string GetDescription(this Enum value)
    {
        var type = value.GetType();
        var name = Enum.GetName(type, value);

        if (name == null)
        {
            return value.ToString();
        }

        var cacheKey = (type, name);

        return DescriptionCache.GetOrAdd(
            cacheKey,
            key =>
            {
                var field = key.Type.GetField(key.Name);

                if (field == null)
                {
                    return key.Name;
                }

                var attribute = field.GetCustomAttribute<DescriptionAttribute>(false);

                return attribute?.Description ?? key.Name;
            }
        );
    }
}
