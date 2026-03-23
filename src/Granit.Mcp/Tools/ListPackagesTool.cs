using System.ComponentModel;
using Granit.Mcp.Services;
using ModelContextProtocol.Server;

namespace Granit.Mcp.Tools;

[McpServerToolType]
public static class ListPackagesTool
{
    [McpServerTool(Name = "list_packages")]
    [Description(
        "Lists all published Granit NuGet packages with their latest version, " +
        "description, and download count.")]
    public static async Task<string> ExecuteAsync(
        NuGetClient nuget,
        CancellationToken ct = default)
    {
        List<PackageSummary> packages = await nuget.ListPackagesAsync(ct);

        if (packages.Count == 0)
        {
            return "No Granit packages found on NuGet.";
        }

        var sorted = packages.OrderBy(p => p.Id).ToList();
        IEnumerable<string> rows = sorted.Select(p =>
        {
            string dl = p.Downloads >= 1000
                ? $"{p.Downloads / 1000.0:F1}k"
                : p.Downloads.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string desc = !string.IsNullOrEmpty(p.Description)
                ? p.Description : "No description";
            return $"- **{p.Id}** v{p.Version} — {desc} ({dl} downloads)";
        });

        return $"## Granit NuGet packages ({sorted.Count})\n\n" +
               string.Join('\n', rows);
    }
}
