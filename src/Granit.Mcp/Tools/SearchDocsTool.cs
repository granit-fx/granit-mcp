using System.ComponentModel;
using Granit.Mcp.Services;
using ModelContextProtocol.Server;

namespace Granit.Mcp.Tools;

[McpServerToolType]
public static class SearchDocsTool
{
    [McpServerTool(Name = "search_docs")]
    [Description(
        "Full-text search across Granit framework documentation. " +
        "Returns lightweight results (ID, title, category, snippet). " +
        "Use get_doc with the returned ID to read full content.")]
    public static string Execute(
        DocsStore store,
        [Description("Search query in plain English or keywords")]
        string query,
        [Description("Maximum number of results (default 6, max 20)")]
        int limit = 6)
    {
        string? status = store.EnsureReadyOrStatus();
        if (status is not null)
        {
            return status;
        }

        List<DocSearchResult> results = store.Search(query, Math.Clamp(limit, 1, 20));

        if (results.Count == 0)
        {
            return $"No results found for \"{query}\".";
        }

        IEnumerable<string> lines = results.Select((r, i) =>
            $"### {i + 1}. {r.Title}\n" +
            $"**ID:** `{r.Id}` · **Category:** {r.Category}\n" +
            $"{r.Snippet}");

        return $"## Search results for \"{query}\" " +
               $"({results.Count} found)\n\n" +
               string.Join("\n\n---\n\n", lines);
    }
}
