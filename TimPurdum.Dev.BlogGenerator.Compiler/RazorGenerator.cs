using System.Reflection;
using System.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.JSInterop;
using TimPurdum.Dev.BlogGenerator.Shared.DefaultImplementationTemplates;

namespace TimPurdum.Dev.BlogGenerator.Compiler;

public static class RazorGenerator
{
    public static string? GenerateCSharpFromRazorFile(string fileName, string razorContent)
    {
        CreateRazorProjectEngine();
        InMemoryRazorProjectItem projectItem = new InMemoryRazorProjectItem(fileName, razorContent);
        RazorCodeDocument? codeDocument = _projectEngine!.Process(projectItem);
        RazorCSharpDocument? cSharpDocument = codeDocument.GetCSharpDocument();
        if (cSharpDocument.Diagnostics
                .Where(d => d.Severity == RazorDiagnosticSeverity.Error 
                            && d.Id != "RZ9992").ToArray() 
            is { Length: > 0 } diagnostics)
        {
            // Ignore "RZ9992: it doesn't like the script tags in the files
            List<string> errors = diagnostics.Select(d => d.GetMessage()).ToList();
            
            throw new InvalidOperationException($"Razor compilation errors in {fileName}: {string.Join(", ", errors)}");
        }

        // Generate C# code
        string? csharpCode = cSharpDocument.GeneratedCode;
        Console.WriteLine($"Generated C# code: {csharpCode}");
        return csharpCode;
    }

    public static Type? GenerateRazorTypeFromFile(string fileName, string razorContent)
    {
        string? csharpCode = GenerateCSharpFromRazorFile(fileName, razorContent);
        return GenerateRazorType(csharpCode!);
    }

    public static Type? GenerateRazorType(string csharpCode, params IEnumerable<string> referenceCodes)
    {
        // Compile with Roslyn
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(csharpCode, CSharpParseOptions.Default);

        CSharpCompilation compilation = CSharpCompilation.Create(
            Generator.BlogSettings!.AssemblyNamespace,
            [syntaxTree, 
                ..referenceCodes.Select(c => CSharpSyntaxTree.ParseText(c, CSharpParseOptions.Default))],
            GetMetadataReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: true
            )
        );
        
        // Emit assembly
        using MemoryStream ms = new MemoryStream();
        EmitResult result = compilation.Emit(ms);

        if (!result.Success)
        {
            List<string> errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage())
                .ToList();

            List<string> warnings = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Warning)
                .Select(d => d.GetMessage())
                .ToList();

            throw new InvalidOperationException($"Razor compilation failed: {string.Join(", ", errors)}{Environment.NewLine}{string.Join(Environment.NewLine, warnings)}");
        }

        ms.Seek(0, SeekOrigin.Begin);
        Assembly assembly = Assembly.Load(ms.ToArray());
        Type? razorType = assembly.GetTypes()
            .FirstOrDefault(t => t.IsSubclassOf(typeof(ComponentBase)));
        Console.WriteLine($"Created Razor component type: {razorType?.FullName}");
        return razorType;
    }
    
    private static void CreateRazorProjectEngine()
    {
        if (_projectEngine is null)
        {
            RazorProjectFileSystem? fileSystem = RazorProjectFileSystem.Create(Directory.GetCurrentDirectory());
            _projectEngine = RazorProjectEngine.Create(RazorConfiguration.Default, fileSystem, builder =>
            {
                builder.SetNamespace(Generator.BlogSettings!.AssemblyNamespace);
                builder.SetRootNamespace(Generator.BlogSettings.AssemblyNamespace);
                // Add common usings
                builder.AddDefaultImports("@using Microsoft.AspNetCore.Components");
                builder.AddDefaultImports("@using Microsoft.AspNetCore.Components.Forms");
                builder.AddDefaultImports("@using Microsoft.AspNetCore.Components.Routing");
                builder.AddDefaultImports("@using Microsoft.AspNetCore.Components.Web");
                builder.AddDefaultImports("@using Microsoft.AspNetCore.Components.Web.Virtualization");
                builder.AddDefaultImports("@using Microsoft.JSInterop");
                builder.AddDefaultImports("@using static Microsoft.AspNetCore.Components.Web.RenderMode");
                builder.AddDefaultImports("@using System.Collections.Generic");
                builder.AddDefaultImports("@using System.Linq");
                builder.AddDefaultImports("@using System.Text");
                builder.AddDefaultImports("@using System.Threading.Tasks");
                builder.AddDefaultImports("@using TimPurdum.Dev.BlogGenerator.Compiler");
                builder.AddDefaultImports("@using TimPurdum.Dev.BlogGenerator.Shared");
                builder.AddDefaultImports("@using TimPurdum.Dev.BlogGenerator.Shared.AbstractTemplates");
                builder.AddDefaultImports("@using TimPurdum.Dev.BlogGenerator.Shared.DefaultImplementationTemplates");
            });
        }
    }
    
    private static List<MetadataReference> GetMetadataReferences()
    {
        List<MetadataReference> references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(AssemblyTargetedPatchBandAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(CSharpArgumentInfo).Assembly.Location),
            // Add Blazor references
            MetadataReference.CreateFromFile(typeof(ComponentBase).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ErrorBoundary).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IJSRuntime).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MarkupParser).Assembly.Location),
            // Add reference to Generator types and template base types
            MetadataReference.CreateFromFile(typeof(RootTemplate).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(RazorGenerator).Assembly.Location)
        ];

        // Add assembly references
        string[] systemAssemblies =
        [
            "System.Runtime",
            "System.Collections",
            "System.Linq",
            "System.Linq.Expressions",
            "System.ComponentModel",
            "System.ComponentModel.Annotations",
            "Microsoft.AspNetCore.Components",
            "Microsoft.AspNetCore.Components.Web"
        ];

        foreach (var assemblyName in systemAssemblies)
            try
            {
                var assembly = Assembly.Load(assemblyName);
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
            catch
            {
                // Ignore missing assemblies
            }

        return references;
    }

    private static RazorProjectEngine? _projectEngine;
}