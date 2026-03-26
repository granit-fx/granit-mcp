# Configuration

All settings are read from environment variables prefixed with
`GRANIT_MCP_`. Every variable has a sensible default — zero configuration
is required for basic usage.

## Environment variables

### `GRANIT_MCP_LOG_LEVEL`

- **Type:** LogLevel — **Default:** `Information`
- Minimum log level. Valid values: `Trace`, `Debug`, `Information`,
  `Warning`, `Error`, `Critical`, `None`. Case-insensitive.

### `GRANIT_MCP_REFRESH_HOURS`

- **Type:** int — **Default:** `4`
- Hours between documentation re-index cycles.

### `GRANIT_MCP_DATA_DIR`

- **Type:** path — **Default:** `~/.granit-mcp`
- Directory for the SQLite database and logs.

### `GRANIT_MCP_DOCS_URL`

- **Type:** URL — **Default:** `https://granit-fx.dev/llms-full.txt`
- Documentation source URL.

### `GRANIT_MCP_CODE_INDEX_URL`

- **Type:** URL
- **Default:** GitHub raw URL with `{branch}` placeholder
- Template URL for the .NET code index. Must contain `{branch}`.

### `GRANIT_MCP_FRONT_INDEX_URL`

- **Type:** URL
- **Default:** GitHub raw URL with `{branch}` placeholder
- Template URL for the TypeScript code index. Must contain `{branch}`.

### `GRANIT_MCP_GITHUB_TOKEN`

- **Type:** string — **Default:** *(none)*
- GitHub token for GitHub Packages NuGet feed. Optional.

## Passing environment variables

Set variables in the MCP server configuration of your AI assistant.

### Claude Code (`~/.claude.json`)

```json
{
  "mcpServers": {
    "granit-tools": {
      "type": "stdio",
      "command": "granit-tools-mcp",
      "args": [],
      "env": {
        "GRANIT_MCP_LOG_LEVEL": "Warning",
        "GRANIT_MCP_REFRESH_HOURS": "8",
        "GRANIT_MCP_GITHUB_TOKEN": "ghp_xxxxxxxxxxxx"
      }
    }
  }
}
```

### Shell (testing)

```bash
GRANIT_MCP_LOG_LEVEL=Debug granit-tools-mcp
```

## Data directory

The data directory (`~/.granit-mcp` by default) stores:

| File | Purpose | Size |
| ---- | ------- | ---- |
| `docs.db` | SQLite FTS5 index of all documentation articles | ~2-5 MB |

The directory is created automatically on first startup if it does not
exist. It can be safely deleted — the server rebuilds the index on next
launch.

## GitHub Packages integration

By default, `nuget_list` and `nuget_get` only query the public
nuget.org feed. To also include pre-release packages published to
GitHub Packages:

1. Create a GitHub PAT with `read:packages` scope
2. Set `GRANIT_MCP_GITHUB_TOKEN` in your MCP server config

When the token is set:

- `nuget_list` merges results from both feeds
- `nuget_get` consolidates version lists from both feeds
- Source indicators (`[github]`, `[nuget.org+github]`) appear in output

When the token is **not** set, GitHub Packages is silently skipped —
all tools work normally with nuget.org only.

## Cache lifetimes

| Data source | Storage | TTL | Fallback |
| ----------- | ------- | --- | -------- |
| Documentation | SQLite | 4h (config.) | Stale DB |
| .NET code index | Memory | 12h | Stale |
| TS code index | Memory | 12h | Stale |
| NuGet list | Memory | 12h | Stale |
| NuGet detail | Memory | 6h | *(none)* |
| Branch list | Transient | — | Empty |

All caches use graceful degradation — if a network request fails, the
server returns stale data when available rather than failing.

## Logging

The MCP stdio protocol uses `stdout` for JSON-RPC messages. **All logs
are emitted to `stderr`** to avoid interference with the protocol.

To see debug logs when troubleshooting, set `GRANIT_MCP_LOG_LEVEL=Debug`
and observe stderr output.
