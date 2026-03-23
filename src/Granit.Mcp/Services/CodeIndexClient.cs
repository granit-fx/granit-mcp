using System.Text.Json;
using Granit.Mcp.Models;
using Microsoft.Extensions.Logging;

namespace Granit.Mcp.Services;

/// <summary>
/// Fetches and caches .mcp-code-index.json and .mcp-front-index.json
/// from GitHub raw, with branch-aware URL resolution.
/// </summary>
public sealed class CodeIndexClient(
    IHttpClientFactory httpFactory,
    GranitMcpConfig config,
    ILogger<CodeIndexClient> logger)
{
    private const string DefaultBranch = "develop";

    private readonly Dictionary<string, CachedIndex<CodeIndex>> _codeCache = new();
    private readonly Dictionary<string, CachedIndex<FrontIndex>> _frontCache = new();
    private readonly Lock _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string ResolveBranch(string? branch) =>
        branch ?? GitBranchDetector.DetectBranch();

    public async Task<CodeIndex?> GetCodeIndexAsync(
        string? branch, CancellationToken ct = default)
    {
        string resolved = ResolveBranch(branch);
        return await GetCachedAsync(
            _codeCache, config.CodeIndexUrl, resolved, ct);
    }

    public async Task<FrontIndex?> GetFrontIndexAsync(
        string? branch, CancellationToken ct = default)
    {
        string resolved = ResolveBranch(branch);
        return await GetCachedAsync(
            _frontCache, config.FrontIndexUrl, resolved, ct);
    }

    public async Task<List<BranchInfo>> ListBranchesAsync(
        string? repo, CancellationToken ct = default)
    {
        var results = new List<BranchInfo>();
        using HttpClient http = httpFactory.CreateClient();
        http.DefaultRequestHeaders.Add("User-Agent", "granit-mcp");

        if (repo is null or "dotnet")
        {
            List<BranchInfo> branches = await CheckRepoBranchesAsync(
                http, "granit-fx", "granit-dotnet",
                ".mcp-code-index.json", ct);
            results.AddRange(branches);
        }

        if (repo is null or "front")
        {
            List<BranchInfo> branches = await CheckRepoBranchesAsync(
                http, "granit-fx", "granit-front",
                ".mcp-front-index.json", ct);
            results.AddRange(branches);
        }

        return results;
    }

    private async Task<T?> GetCachedAsync<T>(
        Dictionary<string, CachedIndex<T>> cache,
        string urlTemplate,
        string branch,
        CancellationToken ct) where T : class
    {
        lock (_lock)
        {
            if (cache.TryGetValue(branch, out CachedIndex<T>? cached)
                && !cached.IsExpired)
            {
                return cached.Data;
            }
        }

        try
        {
            string url = urlTemplate.Replace("{branch}", branch);
            using HttpClient http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            string json = await http.GetStringAsync(url, ct);
            T? data = JsonSerializer.Deserialize<T>(json, JsonOptions);

            if (data is not null)
            {
                lock (_lock)
                {
                    cache[branch] = new CachedIndex<T>(
                        data, DateTime.UtcNow.AddHours(12));
                }
            }

            return data;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Failed to fetch index for branch {Branch}", branch);

            // Return stale cache if available
            lock (_lock)
            {
                return cache.TryGetValue(branch, out CachedIndex<T>? stale)
                    ? stale.Data
                    : null;
            }
        }
    }

    private static async Task<List<BranchInfo>> CheckRepoBranchesAsync(
        HttpClient http, string owner, string repo,
        string indexFile, CancellationToken ct)
    {
        string url = $"https://api.github.com/repos/{owner}/{repo}/branches?per_page=100";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/vnd.github+json");

        HttpResponseMessage response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        List<GitHubBranch>? branches = await JsonSerializer.DeserializeAsync<List<GitHubBranch>>(
            await response.Content.ReadAsStreamAsync(ct),
            JsonOptions, ct);

        if (branches is null)
        {
            return [];
        }

        var results = new List<BranchInfo>();
        IEnumerable<Task<BranchInfo>> checks = branches.Select(async b =>
        {
            string fileUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{indexFile}?ref={b.Name}";
            var req = new HttpRequestMessage(HttpMethod.Head, fileUrl);
            req.Headers.Add("Accept", "application/vnd.github+json");
            req.Headers.Add("User-Agent", "granit-mcp");
            HttpResponseMessage res = await http.SendAsync(req, ct);
            return new BranchInfo(repo, b.Name, res.IsSuccessStatusCode);
        });

        return (await Task.WhenAll(checks))
            .Where(b => b.HasIndex)
            .ToList();
    }

    private sealed record CachedIndex<T>(T Data, DateTime ExpiresAt)
    {
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }

    private sealed record GitHubBranch(
        [property: System.Text.Json.Serialization.JsonPropertyName("name")]
        string Name);
}

public sealed record BranchInfo(string Repo, string Branch, bool HasIndex);
