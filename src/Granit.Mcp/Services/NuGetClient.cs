using System.Text.Json;
using Granit.Mcp.Models;
using Microsoft.Extensions.Logging;

namespace Granit.Mcp.Services;

/// <summary>
/// NuGet API client with in-memory caching.
/// Uses the public NuGet v3 API — no authentication required.
/// </summary>
public sealed class NuGetClient(
    IHttpClientFactory httpFactory,
    ILogger<NuGetClient> logger)
{
    private const string SearchUrl =
        "https://azuresearch-usnc.nuget.org/query";
    private const string RegistrationUrl =
        "https://api.nuget.org/v3/registration5-gz-semver2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private CachedData<List<PackageSummary>>? _packageListCache;
    private readonly Dictionary<string, CachedData<PackageDetail>> _packageInfoCache = new();
    private readonly Lock _lock = new();

    public async Task<List<PackageSummary>> ListPackagesAsync(
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_packageListCache is not null && !_packageListCache.IsExpired)
            {
                return _packageListCache.Data;
            }
        }

        using HttpClient http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(15);

        string url = $"{SearchUrl}?q=owner:granit-fx&take=50&prerelease=false";
        string json = await http.GetStringAsync(url, ct);
        NuGetSearchResponse? response = JsonSerializer.Deserialize<NuGetSearchResponse>(
            json, JsonOptions);

        List<PackageSummary> packages = response?.Data
            .Select(p => new PackageSummary(
                p.Id, p.Version, p.Description ?? "",
                p.TotalDownloads, p.Authors, p.Tags))
            .ToList() ?? [];

        lock (_lock)
        {
            _packageListCache = new CachedData<List<PackageSummary>>(
                packages, DateTime.UtcNow.AddHours(12));
        }

        return packages;
    }

    public async Task<PackageDetail?> GetPackageInfoAsync(
        string packageId, CancellationToken ct = default)
    {
        string key = packageId.ToLowerInvariant();

        lock (_lock)
        {
            if (_packageInfoCache.TryGetValue(key, out CachedData<PackageDetail>? cached)
                && !cached.IsExpired)
            {
                return cached.Data;
            }
        }

        try
        {
            using HttpClient http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);

            string url = $"{RegistrationUrl}/{key}/index.json";
            HttpResponseMessage response = await http.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            RegistrationIndex? index = await JsonSerializer.DeserializeAsync<RegistrationIndex>(
                await response.Content.ReadAsStreamAsync(ct),
                JsonOptions, ct);
            if (index is null)
            {
                return null;
            }

            var entries = new List<CatalogEntry>();
            foreach (RegistrationPage page in index.Items)
            {
                if (page.Items is not null)
                {
                    entries.AddRange(
                        page.Items.Select(l => l.CatalogEntry));
                }
                else
                {
                    HttpResponseMessage pageResponse = await http.GetAsync(page.Url, ct);
                    if (!pageResponse.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    RegistrationPage? pageData = await JsonSerializer
                        .DeserializeAsync<RegistrationPage>(
                            await pageResponse.Content
                                .ReadAsStreamAsync(ct),
                            JsonOptions, ct);
                    if (pageData?.Items is not null)
                    {
                        entries.AddRange(
                            pageData.Items.Select(l => l.CatalogEntry));
                    }
                }
            }

            if (entries.Count == 0)
            {
                return null;
            }

            CatalogEntry latest = entries[^1];
            var detail = new PackageDetail(
                latest.Id,
                latest.Version,
                latest.Description ?? "",
                latest.Authors ?? "",
                latest.LicenseExpression ?? latest.LicenseUrl,
                latest.ProjectUrl,
                latest.Tags ?? [],
                entries.Select(e => new PackageVersionInfo(
                    e.Version,
                    e.Published,
                    e.Listed != false)).ToList(),
                (latest.DependencyGroups ?? []).Select(g =>
                    new PackageDepGroup(
                        g.TargetFramework,
                        (g.Dependencies ?? []).Select(d =>
                            new PackageDep(d.Id, d.Range)).ToList()))
                    .ToList());

            lock (_lock)
            {
                _packageInfoCache[key] = new CachedData<PackageDetail>(
                    detail, DateTime.UtcNow.AddHours(6));
            }

            return detail;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Failed to fetch NuGet info for {Package}", packageId);
            return null;
        }
    }

    private sealed record CachedData<T>(T Data, DateTime ExpiresAt)
    {
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
}

public sealed record PackageSummary(
    string Id, string Version, string Description,
    long Downloads, List<string> Authors, List<string> Tags);

public sealed record PackageDetail(
    string Id, string LatestVersion, string Description,
    string Authors, string? License, string? ProjectUrl,
    List<string> Tags,
    List<PackageVersionInfo> Versions,
    List<PackageDepGroup> DependencyGroups);

public sealed record PackageVersionInfo(
    string Version, string? Published, bool Listed);

public sealed record PackageDepGroup(
    string Framework, List<PackageDep> Dependencies);

public sealed record PackageDep(string Id, string Range);
