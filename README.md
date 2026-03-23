# granit-mcp

[![GitHub](https://img.shields.io/badge/github-repo-blue?logo=github)](https://github.com/granit-fx/granit-mcp)
[![NuGet](https://img.shields.io/nuget/v/Granit.Mcp?logo=nuget)](https://www.nuget.org/packages/Granit.Mcp)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=granit-fx_granit-mcp&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=granit-fx_granit-mcp)
[![License: Apache-2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

Local MCP server for the [Granit framework](https://granit-fx.dev) — gives AI
assistants (Claude Code, Cursor, Windsurf) structured access to documentation,
code navigation, and NuGet package metadata.

Built as a **.NET 10 dotnet tool** with **SQLite FTS5** for full-text search
and the [Model Context Protocol SDK](https://modelcontextprotocol.io/).

## Tools (10)

### Documentation

| Tool | Description |
| ---- | ----------- |
| `search_docs` | FTS5 search — returns ID, title, snippet |
| `get_doc` | Full article content by ID |
| `list_patterns` | Architecture patterns list |

### Code navigation

| Tool | Description |
| ---- | ----------- |
| `search_code` | Search symbols across .NET and TS |
| `get_public_api` | Public API of a type with signatures |
| `get_project_graph` | Project/package dependency graph |
| `list_branches` | Branches with committed code indexes |

### NuGet packages

| Tool | Description |
| ---- | ----------- |
| `list_packages` | Granit.\* packages with version/downloads |
| `get_package_info` | Versions, deps, frameworks, license |

## Install

```bash
dotnet tool install --global Granit.Mcp
```

## Use with Claude Code

```json
{
  "mcpServers": {
    "granit": {
      "command": "granit-mcp"
    }
  }
}
```

## Use with Cursor / Windsurf

Add the MCP server in **Settings > MCP Servers**:

- **Name:** `granit`
- **Command:** `granit-mcp`

## Architecture

```text
Claude Code ──stdio──> Granit.Mcp (local .NET 10 tool)
                         |-- Docs ---------> SQLite FTS5
                         |                     ^ llms-full.txt (auto-generated)
                         |-- Code ---------> .mcp-*-index.json (GitHub raw)
                         |-- NuGet --------> api.nuget.org
                         +-- Branches -----> api.github.com
```

### Data sources

| Source | Origin | Cache |
| ------ | ------ | ----- |
| Documentation | `granit-fx.dev/llms-full.txt` | SQLite (4h refresh) |
| .NET code index | GitHub raw (branch-aware) | In-memory (12h) |
| Front code index | GitHub raw (branch-aware) | In-memory (12h) |
| NuGet packages | NuGet Search API | In-memory (12h) |
| NuGet package info | NuGet Registration API | In-memory (6h) |

### Branch detection

The tool reads `.git/HEAD` from the current working directory and uses
the detected branch for code index tools by default. Explicit `branch`
parameter overrides this.

## Development

```bash
dotnet build
dotnet run --project src/Granit.Mcp
```

## ADRs

- [ADR-001](docs/adr/001-json-index-cloudflare-workers.md) —
  JSON index + Cloudflare Workers (superseded)
- [ADR-002](docs/adr/002-granit-mcp-code-and-packages.md) —
  Code navigation & NuGet packages (superseded)
- [ADR-003](docs/adr/003-local-dotnet-tool-with-fts5.md) —
  Local .NET tool with SQLite FTS5

## License

Apache-2.0
