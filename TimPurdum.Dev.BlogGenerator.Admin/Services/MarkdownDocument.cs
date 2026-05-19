using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace TimPurdum.Dev.BlogGenerator.Admin.Services;

/// <summary>
/// A markdown file with a YAML frontmatter block, structured as <c>---\n{yaml}\n---\n{body}</c>.
/// </summary>
public static class MarkdownDocument
{
    private static readonly Regex FrontMatterRegex =
        new(@"^---\s*(.*?)\s*---\s*(.*)$", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    /// <summary>Parse the YAML+body content. Returns a tuple of <c>(frontmatter, body)</c>.</summary>
    public static (TFront FrontMatter, string Body) Parse<TFront>(string fileContent) where TFront : new()
    {
        Match match = FrontMatterRegex.Match(fileContent);
        if (!match.Success)
        {
            // No frontmatter — return defaults and the whole content as body.
            return (new TFront(), fileContent);
        }
        string yaml = match.Groups[1].Value;
        string body = match.Groups[2].Value;
        TFront front;
        try
        {
            front = Deserializer.Deserialize<TFront>(yaml) ?? new TFront();
        }
        catch (YamlException ex)
        {
            throw new MarkdownFrontMatterParseException($"Front-matter YAML is invalid: {ex.Message}", yaml, ex);
        }
        return (front, body);
    }

    /// <summary>Serialize the frontmatter + body back into the on-disk markdown form.</summary>
    public static string Build<TFront>(TFront frontmatter, string body)
    {
        StringBuilder sb = new();
        sb.AppendLine("---");
        sb.Append(Serializer.Serialize(frontmatter));
        sb.AppendLine("---");
        if (!string.IsNullOrEmpty(body))
        {
            // Preserve a single blank line between frontmatter and body so the file feels hand-written.
            if (!body.StartsWith('\n')) sb.Append('\n');
            sb.Append(body);
            if (!body.EndsWith('\n')) sb.Append('\n');
        }
        return sb.ToString();
    }
}

public sealed class MarkdownFrontMatterParseException : Exception
{
    public MarkdownFrontMatterParseException(string message, string rawYaml, Exception innerException)
        : base(message, innerException)
    {
        RawYaml = rawYaml;
    }

    public string RawYaml { get; }
}
