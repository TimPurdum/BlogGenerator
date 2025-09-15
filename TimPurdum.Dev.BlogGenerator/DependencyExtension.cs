using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TimPurdum.Dev.BlogGenerator.Shared;

namespace TimPurdum.Dev.BlogGenerator;

public static class DependencyExtension
{
    public static async Task<WebAssemblyHostBuilder> AddGeneratedBlogContent(this WebAssemblyHostBuilder builder)
    {
        ServiceProvider sp = builder.Services.BuildServiceProvider();
        IServiceScope scope = sp.CreateScope();
        NavigationManager navigationManager = scope.ServiceProvider.GetRequiredService<NavigationManager>();
        string url = navigationManager.Uri;
        HttpClient client = scope.ServiceProvider.GetRequiredService<HttpClient>();
        string htmlContent = await client.GetStringAsync(url);

        List<Type> razorComponentTypes = Assembly.GetEntryAssembly()!.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ComponentBase)) && !t.IsAbstract)
            .ToList();

        foreach (Type type in razorComponentTypes)
        {
            if (htmlContent.Contains($"id=\"{type.Name.PascalToKebabCase()}\""))
            {
                Console.WriteLine($"Adding component {type.Name} with selector #{type.Name.PascalToKebabCase()}");
                try
                {
                    builder.RootComponents.Add(type, $"#{type.Name.PascalToKebabCase()}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error adding component {type.Name}: {ex.Message}");
                }
            }
        }

        return builder;
    }
}