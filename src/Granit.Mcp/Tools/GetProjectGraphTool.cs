using System.ComponentModel;
using Granit.Mcp.Models;
using Granit.Mcp.Services;
using ModelContextProtocol.Server;

namespace Granit.Mcp.Tools;

[McpServerToolType]
public static class GetProjectGraphTool
{
    [McpServerTool(Name = "get_project_graph")]
    [Description(
        "Shows the project/package dependency graph for the Granit framework. " +
        "Lists all .NET projects and/or TypeScript packages.")]
    public static async Task<string> ExecuteAsync(
        CodeIndexClient client,
        [Description("Restrict to a specific repo. Omit to show both.")]
        string? repo = null,
        [Description("Git branch for the code index. Defaults to detected branch or develop.")]
        string? branch = null,
        CancellationToken ct = default)
    {
        var sections = new List<string>();

        if (repo is not "front")
        {
            CodeIndex? codeIndex = await client.GetCodeIndexAsync(branch, ct);
            if (codeIndex is { ProjectGraph.Count: > 0 })
            {
                var sorted = codeIndex.ProjectGraph
                    .OrderBy(p => p.Name)
                    .ToList();
                IEnumerable<string> lines = sorted.Select(p =>
                {
                    string deps = p.Deps.Count > 0
                        ? $"→ {string.Join(", ", p.Deps)}"
                        : "*(no dependencies)*";
                    return $"- **{p.Name}** ({p.Framework}) {deps}";
                });

                sections.Add(
                    $"### .NET — {sorted.Count} projects\n\n" +
                    string.Join('\n', lines));
            }
        }

        if (repo is not "dotnet")
        {
            FrontIndex? frontIndex = await client.GetFrontIndexAsync(branch, ct);
            if (frontIndex is { Packages.Count: > 0 })
            {
                var sorted = frontIndex.Packages
                    .OrderBy(p => p.Name)
                    .ToList();
                IEnumerable<string> lines = sorted.Select(p =>
                {
                    string desc = !string.IsNullOrEmpty(p.Description)
                        ? $" — {p.Description}" : "";
                    return $"- **{p.Name}**{desc}";
                });

                sections.Add(
                    $"### TypeScript — {sorted.Count} packages\n\n" +
                    string.Join('\n', lines));
            }
        }

        return sections.Count > 0
            ? $"## Granit project graph\n\n{string.Join("\n\n", sections)}"
            : "No project graph data available. " +
              "Code indexes may not be published yet.";
    }
}
