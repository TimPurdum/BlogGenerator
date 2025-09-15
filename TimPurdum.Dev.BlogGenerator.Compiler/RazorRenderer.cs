using System.Buffers;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Syntax;

namespace TimPurdum.Dev.BlogGenerator.Compiler;

// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
// Modified by Tim Purdum to support Razor Markup rendering

public static class RazorRendererExtensions
{
    /// <summary>
    /// Converts a Markdown document to HTML.
    /// </summary>
    /// <param name="document">A Markdown document.</param>
    /// <param name="pipeline">The pipeline used for the conversion.</param>
    /// <returns>The HTML string.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="document"/> is null.</exception>
    public static string ToRazor(this MarkdownDocument document)
    {
        RazorRenderer renderer = new(new FastStringWriter());

        renderer.Render(document);
        renderer.Writer.Flush();

        return renderer.Writer.ToString() ?? string.Empty;
    }
}

/// <summary>
///     Razor renderer for a Markdown <see cref="MarkdownDocument" /> object.
/// </summary>
public class RazorRenderer(TextWriter writer) : HtmlRenderer(writer)
{
    protected override SearchValues<char> SEscapedChars { get; set; } = SearchValues.Create("<>&\"@");
    protected override SearchValues<char> SAsciiNonEscapeChars { get; set; } =
        SearchValues.Create("!#$%()*+,-./0123456789:;=?ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz");
    
    /// <summary>
    /// Writes the content escaped for HTML.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="softEscape">Only escape &lt; and &amp;</param>
    public override void WriteEscape(ReadOnlySpan<char> content, bool softEscape = false)
    {
        if (!content.IsEmpty)
        {
            WriteIndent();

            while (true)
            {
                int indexOfCharToEscape = softEscape
                    ? content.IndexOfAny('<', '&')
                    : content.IndexOfAny(SEscapedChars);

                if ((uint)indexOfCharToEscape >= (uint)content.Length)
                {
                    WriteRaw(content);
                    return;
                }

                WriteRaw(content.Slice(0, indexOfCharToEscape));

                if (EnableHtmlEscape)
                {
                    WriteRaw(content[indexOfCharToEscape] switch
                    {
                        '<' => "&lt;",
                        '>' => "&gt;",
                        '&' => "&amp;",
                        '@' => "&commat;",
                        _ => "&quot;"
                    });
                }

                content = content.Slice(indexOfCharToEscape + 1);
            }
        }
    }
}