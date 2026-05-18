namespace TimPurdum.Dev.BlogGenerator.Admin.Services;

/// <summary>GitHub owner + repository the admin commits against.</summary>
public sealed record GitHubRepoConfig(string Owner, string Repo);
