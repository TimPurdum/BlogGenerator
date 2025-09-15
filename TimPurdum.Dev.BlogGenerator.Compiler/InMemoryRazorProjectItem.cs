using System.Text;
using Microsoft.AspNetCore.Razor.Language;

namespace TimPurdum.Dev.BlogGenerator.Compiler;

public class InMemoryRazorProjectItem(string fileName, string content) : RazorProjectItem
{
    public override string BasePath => "/";
    public override string FilePath => fileName;
    public override string PhysicalPath => fileName;
    public override string RelativePhysicalPath => fileName;
    public override string FileKind => FileKinds.Component;
    public override string? CssScope => null;

    public override bool Exists => true;

    public override Stream Read()
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }
}