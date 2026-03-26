# Getting Started

Install `granit-tools-mcp` as a global .NET tool and connect it to your
AI assistant in under two minutes.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- An MCP-compatible AI assistant (Claude Code, Cursor, Windsurf)

## Installation

```bash
dotnet tool install --global Granit.Tools.Mcp
```

Verify:

```bash
granit-tools-mcp --version
```

## Connect to your AI assistant

### Claude Code

Add the server to `~/.claude.json` (global) or `.mcp.json` (per-project):

```json
{
  "mcpServers": {
    "granit-tools": {
      "type": "stdio",
      "command": "granit-tools-mcp",
      "args": [],
      "env": {}
    }
  }
}
```

Restart Claude Code. The tools appear automatically.

### Cursor / Windsurf

Open **Settings > MCP Servers**, click **Add**, then enter:

| Field | Value |
| ----- | ----- |
| Name | `granit-tools` |
| Transport | stdio |
| Command | `granit-tools-mcp` |

## First use

On first startup, the server fetches `llms-full.txt` from the Granit
documentation site and builds a local SQLite FTS5 index. This takes a
few seconds. Tools return a status message while indexing is in progress:

```json
{ "state": "Indexing", "message": "Building FTS5 index from llms-full.txt..." }
```

Once the index is ready, all nine tools are available. The index refreshes
automatically every 4 hours (configurable).

## Quick tour

Try these in your AI assistant:

```text
# Search documentation
→ docs_search("blob storage")

# Read a specific article
→ docs_get("doc-3")

# Find a type across the codebase
→ code_search("IBlobStorage")

# Inspect a type's public API
→ code_get_api("GranitModule")

# List all published NuGet packages
→ nuget_list()
```

## What's next

- [Tools Reference](tools-reference.md) — complete reference for all nine tools
- [Configuration](configuration.md) — environment variables, data directory, caching
- [Architecture](architecture.md) — how the server works under the hood
