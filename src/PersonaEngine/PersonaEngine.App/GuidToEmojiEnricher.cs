using System.Collections.Concurrent;

using Serilog.Core;
using Serilog.Events;

namespace PersonaEngine.App;

public class GuidToEmojiEnricher : ILogEventEnricher
{
    private readonly GuidEmojiMapper _mapper;

    public GuidToEmojiEnricher() { _mapper = new GuidEmojiMapper(); }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var guidProperties = logEvent.Properties
                                     .Where(kvp => kvp.Value is ScalarValue { Value: Guid })
                                     .Select(kvp => new { kvp.Key, Value = (Guid)(((ScalarValue)kvp.Value).Value ?? Guid.Empty) })
                                     .ToList();

        foreach ( var propInfo in guidProperties )
        {
            var emoji = _mapper.GetEmojiForGuid(propInfo.Value);

            logEvent.RemovePropertyIfPresent(propInfo.Key);

            var emojiProperty = propertyFactory.CreateProperty(propInfo.Key, emoji);
            logEvent.AddOrUpdateProperty(emojiProperty);
        }
    }
}

public class GuidEmojiMapper
{
    private static readonly IReadOnlyList<string> Emojis = new List<string> {
                                                                                "🚀",
                                                                                "🌟",
                                                                                "💡",
                                                                                "🔧",
                                                                                "🐞",
                                                                                "🔗",
                                                                                "💾",
                                                                                "🔑",
                                                                                "🎉",
                                                                                "🎯",
                                                                                "📁",
                                                                                "📄",
                                                                                "📦",
                                                                                "🧭",
                                                                                "📡",
                                                                                "🧪",
                                                                                "🧬",
                                                                                "⚙️",
                                                                                "💎",
                                                                                "🧩",
                                                                                "🐙",
                                                                                "🦄",
                                                                                "🐘",
                                                                                "🦋",
                                                                                "🐌",
                                                                                "🐬",
                                                                                "🐿️",
                                                                                "🍄",
                                                                                "🌵",
                                                                                "🍀"
                                                                            }.AsReadOnly();

    private readonly ConcurrentDictionary<Guid, string> _guidEmojiMap = new();

    private int _emojiIndex = -1;

    public string GetEmojiForGuid(Guid guid)
    {
        if ( guid == Guid.Empty )
        {
            return "\u2796";
        }
        
        return _guidEmojiMap.GetOrAdd(guid, _ =>
                                            {
                                                var nextIndex      = Interlocked.Increment(ref _emojiIndex);
                                                var emojiListIndex = (nextIndex & int.MaxValue) % Emojis.Count;

                                                return Emojis[emojiListIndex];
                                            });
    }
}