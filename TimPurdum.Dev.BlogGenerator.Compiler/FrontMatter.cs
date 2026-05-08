using YamlDotNet.Serialization;

namespace TimPurdum.Dev.BlogGenerator.Compiler;

/// <summary>
/// Parsed YAML frontmatter. Wraps a YamlDotNet-deserialized dictionary so the rest of the compiler
/// can treat scalars and indented sequences uniformly. Mutations round-trip back to YAML via <see cref="Serialize"/>.
/// </summary>
public class FrontMatter
{
    private static readonly IDeserializer Deserializer =
        new DeserializerBuilder().Build();

    private static readonly ISerializer Serializer =
        new SerializerBuilder().Build();

    private readonly Dictionary<string, object?> _data;

    private FrontMatter(Dictionary<string, object?> data) => _data = data;

    public static FrontMatter Parse(string yaml)
    {
        Dictionary<string, object?>? raw =
            Deserializer.Deserialize<Dictionary<string, object?>>(yaml);
        return new FrontMatter(raw ?? new Dictionary<string, object?>());
    }

    public string GetString(string key, string @default = "")
        => _data.TryGetValue(key, out object? v) && v is not null
            ? v.ToString() ?? @default
            : @default;

    public DateTime? GetDateTime(string key)
        => _data.TryGetValue(key, out object? v) switch
        {
            true when v is DateTime dt => dt,
            true when v is string s && DateTime.TryParse(s, out DateTime d) => d,
            _ => null
        };

    /// <summary>
    /// Returns indented-sequence-of-mapping values (e.g. <c>images:\n  - src: ...\n    caption: ...</c>)
    /// as a list of string→string dictionaries. Shallow flatten only — nested mappings stringify;
    /// promote them to typed access if needed.
    /// </summary>
    public List<Dictionary<string, string>> GetMappingList(string key)
    {
        if (!_data.TryGetValue(key, out object? v) || v is not IEnumerable<object?> list)
        {
            return [];
        }

        List<Dictionary<string, string>> result = [];
        foreach (object? item in list)
        {
            if (item is IDictionary<object, object?> map)
            {
                Dictionary<string, string> entry = new();
                foreach (KeyValuePair<object, object?> kv in map)
                {
                    entry[kv.Key.ToString() ?? string.Empty] = kv.Value?.ToString() ?? string.Empty;
                }
                result.Add(entry);
            }
        }
        return result;
    }

    public void Set(string key, object? value) => _data[key] = value;

    public IEnumerable<string> Keys => _data.Keys;

    /// <summary>Serializes back to YAML (without the surrounding <c>---</c> fences).</summary>
    public string Serialize() => Serializer.Serialize(_data);
}
