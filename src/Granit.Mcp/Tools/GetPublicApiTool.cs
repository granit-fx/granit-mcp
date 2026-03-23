using System.ComponentModel;
using Granit.Mcp.Models;
using Granit.Mcp.Services;
using ModelContextProtocol.Server;

namespace Granit.Mcp.Tools;

[McpServerToolType]
public static class GetPublicApiTool
{
    [McpServerTool(Name = "get_public_api")]
    [Description(
        "Retrieves the full public API surface of a Granit type — all public methods, " +
        "properties, and events with their signatures. Works for both .NET types and " +
        "TypeScript exports.")]
    public static async Task<string> ExecuteAsync(
        CodeIndexClient client,
        [Description("Type name, e.g. \"IBlobStorage\", \"GranitModule\". Case-insensitive.")]
        string type,
        [Description("Restrict to a specific repo. Omit to search both.")]
        string? repo = null,
        [Description("Git branch for the code index. Defaults to detected branch or develop.")]
        string? branch = null,
        CancellationToken ct = default)
    {
        string query = type.ToLowerInvariant()
            .Replace("granit.", "", StringComparison.Ordinal);

        if (repo is not "front")
        {
            CodeIndex? codeIndex = await client.GetCodeIndexAsync(branch, ct);
            if (codeIndex is not null)
            {
                CodeSymbol? match = FindDotnetType(codeIndex.Symbols, query);
                if (match is not null)
                {
                    return FormatDotnetApi(match);
                }
            }
        }

        if (repo is not "dotnet")
        {
            FrontIndex? frontIndex = await client.GetFrontIndexAsync(branch, ct);
            if (frontIndex is not null)
            {
                (string Pkg, FrontExport Export)? match = FindFrontExport(frontIndex.Packages, query);
                if (match is not null)
                {
                    return FormatFrontApi(match.Value.Pkg, match.Value.Export);
                }
            }
        }

        return $"Type \"{type}\" not found in the code index.\n\n" +
               "Tip: use `search_code` to find the correct type name.";
    }

    private static CodeSymbol? FindDotnetType(
        List<CodeSymbol> symbols, string query)
    {
        string alpha = new string(query.Where(char.IsLetterOrDigit).ToArray());

        // 1. Exact name match
        CodeSymbol? exact = symbols.Find(s =>
            s.Name.Equals(query, StringComparison.OrdinalIgnoreCase)
            || s.Name.Equals(alpha, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // 2. FQN ends-with
        CodeSymbol? byFqn = symbols.Find(s =>
            s.Fqn.EndsWith($".{query}", StringComparison.OrdinalIgnoreCase)
            || s.Fqn.EndsWith($".{alpha}", StringComparison.OrdinalIgnoreCase));
        if (byFqn is not null)
        {
            return byFqn;
        }

        // 3. Partial — shortest name wins
        return symbols
            .Where(s => s.Name.Contains(alpha, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name.Length)
            .FirstOrDefault();
    }

    private static (string Pkg, FrontExport Export)? FindFrontExport(
        List<FrontPackage> packages, string query)
    {
        string alpha = new string(query.Where(char.IsLetterOrDigit).ToArray());

        foreach (FrontPackage pkg in packages)
        {
            FrontExport? match = pkg.Exports.Find(e =>
                e.Name.Equals(query, StringComparison.OrdinalIgnoreCase)
                || e.Name.Equals(alpha, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return (pkg.Name, match);
            }
        }

        foreach (FrontPackage pkg in packages)
        {
            FrontExport? match = pkg.Exports.Find(e =>
                e.Name.Contains(alpha, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return (pkg.Name, match);
            }
        }

        return null;
    }

    private static string FormatDotnetApi(CodeSymbol sym)
    {
        string ns = sym.Fqn.Replace($".{sym.Name}", "");
        var lines = new List<string>
        {
            $"## {sym.Name}",
            $"**Kind:** {sym.Kind} · **Namespace:** {ns}",
            $"**Project:** {sym.Project} · **File:** {sym.File}",
            "",
        };

        if (sym.Members.Count == 0)
        {
            lines.Add("*No public members.*");
        }
        else
        {
            IOrderedEnumerable<IGrouping<string, CodeMember>> grouped = sym.Members
                .GroupBy(m => m.Kind)
                .OrderBy(g => g.Key);

            foreach (IGrouping<string, CodeMember>? group in grouped)
            {
                string kind = char.ToUpperInvariant(group.Key[0])
                    + group.Key[1..];
                lines.Add($"### {kind}s ({group.Count()})");
                lines.Add("");
                foreach (CodeMember? m in group)
                {
                    string ret = m.ReturnType is not null
                        ? $" → {m.ReturnType}" : "";
                    lines.Add($"- `{m.Signature}`{ret}");
                }
                lines.Add("");
            }
        }

        return string.Join('\n', lines);
    }

    private static string FormatFrontApi(string packageName, FrontExport exp)
    {
        var lines = new List<string>
        {
            $"## {exp.Name}",
            $"**Kind:** {exp.Kind} · **Package:** {packageName}",
            $"**Signature:** `{exp.Signature}`",
            "",
        };

        if (exp.Members is { Count: > 0 })
        {
            lines.Add("### Members");
            lines.Add("");
            foreach (CodeMember m in exp.Members)
            {
                lines.Add($"- `{m.Signature}`");
            }
        }

        return string.Join('\n', lines);
    }
}
