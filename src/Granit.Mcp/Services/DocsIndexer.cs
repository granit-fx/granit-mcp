using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Granit.Mcp.Services;

/// <summary>
/// Background service that fetches llms-full.txt and indexes it into
/// the DocsStore on startup, then refreshes periodically.
/// </summary>
public sealed class DocsIndexer(
    DocsStore store,
    IHttpClientFactory httpFactory,
    GranitMcpConfig config,
    ILogger<DocsIndexer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (store.HasFreshIndex(config.RefreshHours))
        {
            logger.LogInformation(
                "Using cached FTS5 index (still fresh)");
            store.MarkReady();
        }
        else
        {
            await IndexAsync(stoppingToken);
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromHours(config.RefreshHours));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await IndexAsync(stoppingToken);
        }
    }

    private async Task IndexAsync(CancellationToken ct)
    {
        try
        {
            logger.LogInformation(
                "Fetching llms-full.txt from {Url}", config.DocsUrl);
            string content = await FetchDocsAsync(ct);
            store.Index(content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Failed to index docs — serving stale cache if available");

            // If we have a persisted DB from a previous run, mark ready
            store.MarkReadyIfHasData();
        }
    }

    private async Task<string> FetchDocsAsync(CancellationToken ct)
    {
        using HttpClient http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        HttpResponseMessage response = await http.GetAsync(
            config.DocsUrl, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(ct);
    }
}
