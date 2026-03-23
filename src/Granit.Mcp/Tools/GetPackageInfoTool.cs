using System.ComponentModel;
using Granit.Mcp.Services;
using ModelContextProtocol.Server;

namespace Granit.Mcp.Tools;

[McpServerToolType]
public static class GetPackageInfoTool
{
    [McpServerTool(Name = "get_package_info")]
    [Description(
        "Retrieves detailed information about a specific Granit NuGet package: " +
        "all published versions, dependency groups per target framework, license, and tags.")]
    public static async Task<string> ExecuteAsync(
        NuGetClient nuget,
        [Description("NuGet package ID, e.g. \"Granit.Core\", \"Granit.BlobStorage\"")]
        string package,
        [Description("Specific version to inspect. If omitted, shows the latest version.")]
        string? version = null,
        CancellationToken ct = default)
    {
        PackageDetail? info = await nuget.GetPackageInfoAsync(package, ct);

        if (info is null)
        {
            return $"Package \"{package}\" not found on NuGet.\n\n" +
                   "Tip: use `list_packages` to see all available Granit packages.";
        }

        if (version is not null)
        {
            PackageVersionInfo? match = info.Versions.Find(v => v.Version == version);
            if (match is null)
            {
                string available = string.Join(", ",
                    info.Versions.Where(v => v.Listed)
                        .TakeLast(10)
                        .Select(v => v.Version));
                return $"Version \"{version}\" not found for {info.Id}.\n\n" +
                       $"**Recent versions:** {available}";
            }
        }

        string displayVersion = version ?? info.LatestVersion;

        var lines = new List<string>
        {
            $"## {info.Id} v{displayVersion}",
            "",
        };

        if (!string.IsNullOrEmpty(info.Description))
        {
            lines.Add($"> {info.Description}");
            lines.Add("");
        }

        // Metadata
        lines.Add($"**Authors:** {info.Authors}");
        if (info.License is not null)
        {
            lines.Add($"**License:** {info.License}");
        }

        if (info.ProjectUrl is not null)
        {
            lines.Add($"**Project:** {info.ProjectUrl}");
        }

        if (info.Tags.Count > 0)
        {
            lines.Add($"**Tags:** {string.Join(", ", info.Tags)}");
        }

        lines.Add("");

        // Dependencies
        if (info.DependencyGroups.Count > 0)
        {
            lines.Add("### Dependencies");
            lines.Add("");
            foreach (PackageDepGroup group in info.DependencyGroups)
            {
                lines.Add($"**{group.Framework}**");
                if (group.Dependencies.Count == 0)
                {
                    lines.Add("- *(none)*");
                }
                else
                {
                    foreach (PackageDep dep in group.Dependencies)
                    {
                        lines.Add($"- {dep.Id} {dep.Range}");
                    }
                }
                lines.Add("");
            }
        }

        // Version history
        var listed = info.Versions.Where(v => v.Listed).ToList();
        var recent = listed.TakeLast(10).Reverse().ToList();
        if (recent.Count > 0)
        {
            lines.Add("### Recent versions");
            lines.Add("");
            foreach (PackageVersionInfo? v in recent)
            {
                string date = v.Published is not null
                    ? $" ({v.Published.Split('T')[0]})" : "";
                lines.Add($"- v{v.Version}{date}");
            }
            if (listed.Count > 10)
            {
                lines.Add($"- *… and {listed.Count - 10} earlier versions*");
            }
        }

        return string.Join('\n', lines);
    }
}
