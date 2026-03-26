# Contributing

## Prerequisites

- .NET 10 SDK
- Git

## Build

```bash
git clone https://github.com/granit-fx/granit-tools-mcp.git
cd granit-tools-mcp
dotnet build
```

## Test

```bash
dotnet test
```

The test suite covers:

- **DocsStore** — FTS5 indexing, search, category inference, article parsing
- **GranitMcpConfig** — environment variable parsing, defaults, fallbacks
- **GitBranchDetector** — branch detection from `.git/HEAD`, worktree support

## Run locally

```bash
dotnet run --project src/Granit.Tools.Mcp
```

The server starts and listens on stdin/stdout. Use an MCP client or pipe
JSON-RPC messages directly for testing.

## Pack and install locally

```bash
dotnet pack -c Release -o nupkgs
dotnet tool install --global --add-source nupkgs Granit.Tools.Mcp
```

## Project structure

```text
src/Granit.Tools.Mcp/
├── Program.cs                  Host setup, MCP transport
├── GranitMcpConfig.cs          Environment variable configuration
├── Models/
│   ├── CodeIndex.cs            .NET code index models
│   ├── FrontIndex.cs           TypeScript code index models
│   └── NuGetModels.cs          NuGet API response models
├── Services/
│   ├── DocsStore.cs            SQLite FTS5 index + search
│   ├── DocsIndexer.cs          Background llms-full.txt fetcher
│   ├── CodeIndexClient.cs      Branch-aware code index cache
│   ├── NuGetClient.cs          NuGet + GitHub Packages client
│   └── GitBranchDetector.cs    .git/HEAD branch detection
└── Tools/
    ├── SearchDocsTool.cs       docs_search
    ├── GetDocTool.cs           docs_get
    ├── ListPatternsTool.cs     docs_list_patterns
    ├── SearchCodeTool.cs       code_search
    ├── GetPublicApiTool.cs     code_get_api
    ├── GetProjectGraphTool.cs  code_get_graph
    ├── ListBranchesTool.cs     code_list_branches
    ├── ListPackagesTool.cs     nuget_list
    └── GetPackageInfoTool.cs   nuget_get

tests/Granit.Tools.Mcp.Tests/
├── DocsStoreTests.cs
├── GranitMcpConfigTests.cs
└── GitBranchDetectorTests.cs

docs/
├── getting-started.md
├── tools-reference.md
├── configuration.md
├── architecture.md
├── usage-patterns.md
├── contributing.md
└── adr/
    ├── 001-json-index-cloudflare-workers.md
    ├── 002-granit-mcp-code-and-packages.md
    └── 003-local-dotnet-tool-with-fts5.md
```

## Adding a new tool

1. Create a new file in `src/Granit.Tools.Mcp/Tools/`
2. Decorate the class with `[McpServerToolType]`
3. Add a static async method with `[McpServerTool(Name = "tool_name")]`
4. Add a `[Description]` attribute on the method and each parameter
5. Inject services via method parameters (resolved from DI)
6. Return a `string` (Markdown)

The MCP SDK discovers tools automatically via `WithToolsFromAssembly()`.

**Example skeleton:**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Granit.Tools.Mcp.Tools;

[McpServerToolType]
public static class MyNewTool
{
    [McpServerTool(Name = "my_tool")]
    [Description("Short description of what this tool does.")]
    public static async Task<string> ExecuteAsync(
        MyService service,
        [Description("Parameter description")]
        string param,
        CancellationToken ct = default)
    {
        // Implementation
        return "Markdown result";
    }
}
```

## Conventions

- **Tool names** use `snake_case` with a domain prefix (`docs_`, `code_`, `nuget_`)
- **Return format** is always Markdown
- **Error messages** are user-friendly and suggest the correct tool to use
- **Graceful degradation** — tools return status JSON during indexing, not errors
- **No secrets in code** — tokens come from environment variables only
- **Commits** follow [Conventional Commits](https://www.conventionalcommits.org/)
