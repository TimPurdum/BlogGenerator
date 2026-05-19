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
            long userFacingLine = ex.Start.Line + 1;
            long userFacingColumn = ex.Start.Column + 1;
            throw new MarkdownFrontMatterParseException(
                $"Front-matter YAML is invalid near line {userFacingLine}, column {userFacingColumn}. Fix the YAML and try again.",
                yaml,
                ex);
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

/// <summary>
/// Thrown when a markdown file contains a front-matter block that cannot be deserialized as YAML.
/// </summary>
public sealed class MarkdownFrontMatterParseException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MarkdownFrontMatterParseException"/> class.
    /// </summary>
    public MarkdownFrontMatterParseException(string message, string rawYaml, Exception innerException)
        : base(message, innerException)
    {
        RawYaml = rawYaml;
    }

    /// <summary>
    /// Gets the original raw YAML block that failed to parse.
    /// </summary>
    public string RawYaml { get; }
}
