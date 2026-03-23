using System.ComponentModel;
using Granit.Mcp.Services;
using ModelContextProtocol.Server;

namespace Granit.Mcp.Tools;

[McpServerToolType]
public static class GetDocTool
{
    [McpServerTool(Name = "get_doc")]
    [Description(
        "Retrieves the full content of a documentation article by ID. " +
        "Use search_docs first to find the article ID.")]
    public static string Execute(
        DocsStore store,
        [Description("Article ID returned by search_docs (e.g. \"doc-3\")")]
        string id)
    {
        string? status = store.EnsureReadyOrStatus();
        if (status is not null)
        {
            return status;
        }

        DocArticle? article = store.GetById(id);
        if (article is null)
        {
            return $"Article \"{id}\" not found. " +
                   "Use search_docs to find valid IDs.";
        }

        return $"# {article.Title}\n\n" +
               $"**Category:** {article.Category}\n\n" +
               article.Content;
    }
}
