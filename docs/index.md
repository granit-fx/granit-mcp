# Documentation

Granit Tools MCP is a local .NET 10 dotnet tool that gives AI assistants
structured access to the Granit framework — documentation search, code
navigation, and NuGet package discovery — via the Model Context Protocol.

## Contents

- [Getting Started](getting-started.md) — install, connect, first use
- [Tools Reference](tools-reference.md) — all 9 tools with parameters
  and examples
- [Configuration](configuration.md) — environment variables, caching,
  GitHub Packages
- [Architecture](architecture.md) — system design, services, error
  handling
- [Usage Patterns](usage-patterns.md) — practical workflows and tips
- [Contributing](contributing.md) — build, test, add a new tool

## Architecture Decision Records

- [ADR-001](adr/001-json-index-cloudflare-workers.md) — JSON index
  on Cloudflare Workers
- [ADR-002](adr/002-granit-mcp-code-and-packages.md) — code and
  package tools
- [ADR-003](adr/003-local-dotnet-tool-with-fts5.md) — migration to
  local .NET tool with FTS5
