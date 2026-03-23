using System.ComponentModel;
using Granit.Mcp.Models;
using Granit.Mcp.Services;
using ModelContextProtocol.Server;

namespace Granit.Mcp.Tools;

[McpServerToolType]
public static class SearchCodeTool
{
    [McpServerTool(Name = "search_code")]
    [Description(
        "Search across Granit source code symbols (types, methods, interfaces, enums). " +
        "Returns ranked matches with name, kind, project, file path, and signature. " +
        "Searches pre-built code indexes for both .NET (granit-dotnet) and TypeScript (granit-front).")]
    public static async Task<string> ExecuteAsync(
        CodeIndexClient client,
        [Description("Search query — type name, method name, or keywords")]
        string query,
        [Description("Restrict search to a specific repo. Omit to search both.")]
        string? repo = null,
        [Description("Filter by symbol kind: class, interface, method, enum, record, function, type")]
        string? kind = null,
        [Description("Maximum results (default 10, max 20)")]
        int limit = 10,
        [Description("Git branch for the code index. Defaults to detected branch or develop.")]
        string? branch = null,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 20);
        string[] terms = Tokenize(query);
        if (terms.Length == 0)
        {
            return "Query too short.";
        }

        var results = new List<ScoredResult>();

        if (repo is not "front")
        {
            CodeIndex? codeIndex = await client.GetCodeIndexAsync(branch, ct);
            if (codeIndex is not null)
            {
                results.AddRange(SearchDotnet(codeIndex, terms, kind));
            }
        }

        if (repo is not "dotnet")
        {
            FrontIndex? frontIndex = await client.GetFrontIndexAsync(branch, ct);
            if (frontIndex is not null)
            {
                results.AddRange(SearchFront(frontIndex, terms, kind));
            }
        }

        if (results.Count == 0)
        {
            string hint = repo is not null ? $" in repo \"{repo}\"" : "";
            return $"No code results found for \"{query}\"{hint}.";
        }

        var top = results
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();

        IEnumerable<string> formatted = top.Select((r, i) =>
        {
            var lines = new List<string>
            {
                $"### {i + 1}. {r.Name}",
                $"**Kind:** {r.Kind} · **Repo:** {r.Repo} · **Project:** {r.Project}",
            };
            if (r.Fqn is not null)
            {
                lines.Add($"**FQN:** {r.Fqn}");
            }

            if (r.File is not null)
            {
                lines.Add($"**File:** {r.File}");
            }

            if (r.Signature is not null)
            {
                lines.Add($"**Signature:** `{r.Signature}`");
            }

            return string.Join('\n', lines);
        });

        return $"## Code search for \"{query}\" ({top.Count} found)\n\n" +
               string.Join("\n\n---\n\n", formatted);
    }

    private static List<ScoredResult> SearchDotnet(
        CodeIndex index, string[] terms, string? kindFilter)
    {
        var results = new List<ScoredResult>();

        foreach (CodeSymbol sym in index.Symbols)
        {
            if (kindFilter is not null && sym.Kind != kindFilter)
            {
                continue;
            }

            int score = ScoreSymbol(
                sym.Name, sym.Fqn,
                sym.Members.Select(m => m.Name).ToArray(), terms);
            if (score > 0)
            {
                results.Add(new ScoredResult(
                    sym.Name, sym.Fqn, sym.Kind, sym.Project,
                    sym.File, null, "dotnet", score));
            }

            foreach (CodeMember member in sym.Members)
            {
                if (kindFilter is not null && member.Kind != kindFilter)
                {
                    continue;
                }

                int memberScore = ScoreMember(
                    member.Name, sym.Name, member.Signature, terms);
                if (memberScore > 0)
                {
                    results.Add(new ScoredResult(
                        $"{sym.Name}.{member.Name}",
                        $"{sym.Fqn}.{member.Name}",
                        member.Kind, sym.Project,
                        sym.File, member.Signature,
                        "dotnet", memberScore));
                }
            }
        }

        return results;
    }

    private static List<ScoredResult> SearchFront(
        FrontIndex index, string[] terms, string? kindFilter)
    {
        var results = new List<ScoredResult>();

        foreach (FrontPackage pkg in index.Packages)
        {
            foreach (FrontExport exp in pkg.Exports)
            {
                if (kindFilter is not null && exp.Kind != kindFilter)
                {
                    continue;
                }

                int score = ScoreExport(
                    exp.Name, pkg.Name, exp.Signature, terms);
                if (score > 0)
                {
                    results.Add(new ScoredResult(
                        exp.Name, $"{pkg.Name}/{exp.Name}",
                        exp.Kind, pkg.Name,
                        null, exp.Signature,
                        "front", score));
                }
            }
        }

        return results;
    }

    private static string[] Tokenize(string query) =>
        query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .ToArray();

    private static int CountHits(string text, string[] terms)
    {
        string lower = text.ToLowerInvariant();
        int count = 0;
        foreach (string term in terms)
        {
            int idx = 0;
            while ((idx = lower.IndexOf(term, idx, StringComparison.Ordinal)) != -1)
            {
                count++;
                idx += term.Length;
            }
        }
        return count;
    }

    private static int ScoreSymbol(
        string name, string fqn, string[] memberNames, string[] terms) =>
        CountHits(name, terms) * 5
        + CountHits(fqn, terms) * 3
        + CountHits(string.Join(' ', memberNames), terms);

    private static int ScoreMember(
        string name, string parentName, string signature, string[] terms) =>
        CountHits(name, terms) * 5
        + CountHits(parentName, terms) * 2
        + CountHits(signature, terms);

    private static int ScoreExport(
        string name, string packageName, string signature, string[] terms) =>
        CountHits(name, terms) * 5
        + CountHits(packageName, terms) * 2
        + CountHits(signature, terms);

    private sealed record ScoredResult(
        string Name, string? Fqn, string Kind, string Project,
        string? File, string? Signature, string Repo, int Score);
}
