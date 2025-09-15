# Blazor Site Generator

This project is a Blazor-based static site generator designed to create and manage a personal blog. It leverages the power of Blazor for building interactive web applications and provides a simple way to generate static HTML files for deployment.

## Features

- **Static Site Generation**: Converts Markdown content with YAML front matter into static HTML files
- **Blazor Component Integration**: Embed interactive Blazor components directly in Markdown content using `blazor-component` code blocks
- **RSS Feed Generation**: Automatically generates RSS feeds for blog posts
- **Flexible Template System**: Customizable Razor templates for different layouts (posts, pages, root template)
- **File-based Content Management**: Organize content using simple file structure with date-based post naming
- **Interactive Navigation**: Automatic generation of navigation links from content metadata
- **Multi-layout Support**: Support for different page layouts (post layout, page layout, custom layouts)

## Architecture

The solution consists of three main projects:

### TimPurdum.Dev.BlogGenerator
- Main Blazor WebAssembly project that serves as the target for generated content
- Contains the build target that triggers the compilation process
- Includes default template implementations

### TimPurdum.Dev.BlogGenerator.Compiler
- Console application that performs the actual site generation
- **Key Components:**
  - `Generator.cs`: Main orchestrator for site generation
  - `MarkupParser.cs`: Parses Markdown files with YAML front matter
  - `RazorGenerator.cs`: Compiles Razor templates at build time
  - `RssFeedGenerator.cs`: Creates RSS feeds from post metadata
  - `HtmlFileGenerator.cs`: Generates final HTML output

### TimPurdum.Dev.BlogGenerator.Shared
- Shared components and abstractions
- **Contains:**
  - `BlogSettings.cs`: Configuration class for site settings
  - `AbstractTemplates/`: Base classes for customizable templates
  - `MarkupComponent.razor`: Component for rendering embedded Blazor components

## Content Structure

```
Source/
├── Content/
│   ├── Posts/           # Blog posts (YYYY-MM-DD-title.md format)
│   └── Pages/           # Static pages
└── Templates/           # Custom Razor templates
```

### Post Format
Posts use YAML front matter with Markdown content:

```markdown
---
layout: post
title: "Your Post Title"
subtitle: "Optional subtitle"
author: "Author Name"
description: "SEO description"
---

# Your Post Content

Regular Markdown content here.

## Embedding Blazor Components

Use blazor-component code blocks to embed interactive components:

```blazor-component component-name
<YourBlazorComponent Parameter="value" />
```
```

### Post Naming Convention
Posts must follow the naming pattern: `YYYY-MM-DD-title.md`
- Example: `2025-08-06-my-blog-post.md`

## Configuration

Configure your blog through `wwwroot/appsettings.json`:

```json
{
  "BlogSettings": {
    "SiteName": "My Blazor Blog",
    "SiteTitle": "A Blazor Blog",
    "SiteUrl": "https://www.example.com",
    "SiteDescription": "A blog about software development and technology.",
    "HeaderLinks": [
      "<link rel=\"stylesheet\" href=\"/css/custom.css\" />"
    ],
    "PostsContentPath": "Source/Content/Posts",
    "PagesContentPath": "Source/Content/Pages",
    "OutputWebRootPath": "wwwroot",
    "OutputComponentsPath": "Components",
    "SourceTemplatesPath": "Source/Templates"
  }
}
```

## Template System

The generator uses a hierarchical template system:

1. **Root Template**: Inherits from `BaseRootTemplate` - defines the overall page structure
2. **Layout Templates**: Inherits from `BasePageLayout` or `BasePostLayout` - defines content area layout
3. **Component Templates**: Header, Footer, Navigation components

### Creating Custom Templates

1. Create Razor files in `Source/Templates/`
2. Inherit from appropriate base classes:
   - `BaseRootTemplate` for root templates
   - `BasePageLayout` for page layouts
   - `BasePostLayout` for post layouts
   - `BaseHeader`, `BaseFooter`, `BaseNavMenu` for components

## Build Process

The generator integrates into the MSBuild process:

1. **Pre-Build**: Compiler scans for Blazor WebAssembly project
2. **Content Parsing**: Reads Markdown files and extracts metadata
3. **Template Compilation**: Compiles custom Razor templates
4. **Component Generation**: Creates Blazor components from embedded code blocks
5. **HTML Generation**: Renders final static HTML files
6. **RSS Generation**: Creates RSS feed from post metadata

## Dependencies

- **.NET 9.0**: Target framework
- **Markdig**: Markdown processing
- **Microsoft.AspNetCore.Components.WebAssembly**: Blazor WebAssembly support
- **Microsoft.AspNetCore.Razor.Language**: Razor template compilation
- **System.ServiceModel.Syndication**: RSS feed generation

## Getting Started

1. **Clone/Reference**: Add the BlogGenerator project to your solution
2. **Configure**: Set up your `appsettings.json` with blog settings
3. **Create Templates**: Add custom Razor templates in `Source/Templates/`
4. **Add Content**: Create Markdown files in `Source/Content/Posts/` and `Source/Content/Pages/`
5. **Build**: Run `dotnet build` - the generator runs automatically as part of the build process

## Output

The generator produces:
- Static HTML files in the specified output directory
- Generated Blazor components for embedded interactive elements
- RSS feed (`feed.xml`)
- Maintains original file structure for easy deployment

## Advanced Features

- **Interactive Components**: Embed full Blazor components in Markdown content
- **Script Injection**: Automatic script tag generation for components requiring JavaScript
- **SEO Optimization**: Proper meta tags and structured data generation
- **Development Integration**: Automatic regeneration during development builds
