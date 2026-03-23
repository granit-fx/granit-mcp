using System.ComponentModel;
using Granit.Mcp.Services;
using ModelContextProtocol.Server;

namespace Granit.Mcp.Tools;

[McpServerToolType]
public static class ListPatternsTool
{
    [McpServerTool(Name = "list_patterns")]
    [Description(
        "Lists all architecture patterns documented in the Granit " +
        "framework. Use search_docs or get_doc to read pattern details.")]
    public static string Execute(DocsStore store)
    {
        string? status = store.EnsureReadyOrStatus();
        if (status is not null)
        {
            return status;
        }

        List<DocSearchResult> patterns = store.ListByCategory("pattern");

        if (patterns.Count == 0)
        {
            return "No patterns found in the documentation index.";
        }

        IEnumerable<string> lines = patterns.Select(p =>
            $"- **{p.Title}** (`{p.Id}`)");

        return $"## Granit architecture patterns " +
               $"({patterns.Count})\n\n" +
               string.Join('\n', lines) +
               "\n\nUse `get_doc` with the ID to read full pattern content.";
    }
}
