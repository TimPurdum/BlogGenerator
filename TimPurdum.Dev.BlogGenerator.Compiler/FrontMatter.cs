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
    /// Reads a YAML boolean. Accepts the YAML 1.1 truthy/falsy scalar set (<c>true/yes/y/on/1</c> and
    /// <c>false/no/n/off/0</c>), case-insensitively, because front matter is hand-written as often as
    /// it is generated. An unrecognized value returns <paramref name="default"/> rather than throwing.
    /// </summary>
    public bool GetBool(string key, bool @default = false)
    {
        if (!_data.TryGetValue(key, out object? v) || v is null) return @default;
        if (v is bool b) return b;

        return v.ToString()?.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "y" or "on" or "1" => true,
            "false" or "no" or "n" or "off" or "0" => false,
            _ => WarnUnrecognized(key, v, @default)
        };
    }

    /// <summary>
    /// Logs an unrecognized boolean scalar (e.g. a typo like <c>draft: ture</c>) so it is visible in
    /// build output. The semantic here is deliberately unchanged: this still returns <paramref name="default"/>
    /// rather than throwing, because throwing would abort the build over a front-matter typo.
    /// </summary>
    private static bool WarnUnrecognized(string key, object value, bool @default)
    {
        Console.WriteLine(
            $"Warning: front matter key '{key}' has an unrecognized boolean value '{value}'; " +
            $"treating it as {(@default ? "true" : "false")}.");
        return @default;
    }

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
