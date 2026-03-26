# Tools Reference

Granit Tools MCP exposes **nine tools** over the Model Context Protocol,
organized in three groups: Documentation, Code Navigation, and NuGet.

## Documentation tools

### `docs_search`

Full-text search across the Granit framework documentation. Returns
lightweight results — use `docs_get` with the returned ID to read the
full article.

| Parameter | Type | Required | Default | Description |
| --------- | ---- | -------- | ------- | ----------- |
| `query` | string | yes | — | Search query in plain English or keywords |
| `limit` | int | no | 6 | Maximum results (max 20) |

**Returns:** Markdown list with numbered results showing title, ID,
category, and a text snippet.

**Example:**

```text
→ docs_search("multi-tenancy configuration")

## Documentation results (3 matches)

1. **Multi-Tenancy Module** (doc-12) [module]
   Configure tenant resolution, per-tenant databases, and tenant-aware services...

2. **Getting Started with Multi-Tenancy** (doc-45) [guide]
   Step-by-step guide to enable multi-tenancy in a Granit application...
```

> **Tip:** Search uses SQLite FTS5 with Unicode tokenization. Both
> natural language queries and specific keywords work well. Terms shorter
> than 2 characters are ignored.

---

### `docs_get`

Retrieves the full content of a documentation article by ID.

| Parameter | Type | Required | Default | Description |
| --------- | ---- | -------- | ------- | ----------- |
| `id` | string | yes | — | Article ID from `docs_search` (e.g. `"doc-3"`) |

**Returns:** Full Markdown content with H1 title and category.

**Example:**

```text
→ docs_get("doc-12")

# Multi-Tenancy Module
**Category:** module

Granit's multi-tenancy module provides...
```

---

### `docs_list_patterns`

Lists all architecture patterns documented in the framework.

| Parameter | Type | Required | Default | Description |
| --------- | ---- | -------- | ------- | ----------- |

*No parameters.*

**Returns:** Markdown list of patterns with their IDs, ready to pass to
`docs_get`.

---

## Code navigation tools

All code tools support an optional `branch` parameter. When omitted, the
server detects the current Git branch from `.git/HEAD` in the working
directory. Falls back to `develop` if detection fails.

### `code_search`

Search across Granit source code symbols — types, methods, interfaces,
and enums — in both the .NET and TypeScript codebases.

| Parameter | Type | Required | Default |
| --------- | ---- | -------- | ------- |
| `query` | string | yes | — |
| `repo` | string | no | both |
| `kind` | string | no | all |
| `limit` | int | no | 10 |
| `branch` | string | no | detected |

- **query:** Type name, method name, or keywords (min 2 chars per term)
- **repo:** Restrict to `"dotnet"` or `"front"`
- **kind:** Filter by `class`, `interface`, `method`, `enum`, `record`,
  `function`, or `type`
- **limit:** Maximum results (max 20)
- **branch:** Git branch for the code index

**Scoring algorithm:**

| Match location | Points |
| -------------- | ------ |
| Symbol name | 5 |
| Fully-qualified name | 3 |
| Parent name (members) | 2 |
| Member name | 1 |
| Signature (members) | 1 |

Results are sorted by score (descending).

**Returns:** Ranked Markdown list showing kind, repo, project, FQN, file
path, and signature.

---

### `code_get_api`

Retrieves the full public API surface of a Granit type — all public
methods, properties, and events with their signatures.

| Parameter | Type | Required | Default |
| --------- | ---- | -------- | ------- |
| `type` | string | yes | — |
| `repo` | string | no | both |
| `branch` | string | no | detected |

- **type:** Type name, e.g. `"IBlobStorage"`. Case-insensitive.
- **repo:** Restrict to `"dotnet"` or `"front"`
- **branch:** Git branch for the code index

**Type resolution order:**

1. Exact name match (case-insensitive)
2. Alphanumeric-only match (ignores special characters)
3. FQN ends-with match
4. Partial name match (shortest name wins)

**Returns:** Markdown with kind, namespace, project, file, and members
grouped by kind (Methods, Properties, Events, etc.) with return types
and signatures.

---

### `code_get_graph`

Shows the project/package dependency graph for the Granit framework.

| Parameter | Type | Required | Default | Description |
| --------- | ---- | -------- | ------- | ----------- |
| `repo` | string | no | both | Restrict to `"dotnet"` or `"front"` |
| `branch` | string | no | detected | Git branch for the code index |

**Returns:** Markdown sections for .NET projects (with framework targets
and `→` dependency notation) and TypeScript packages (with descriptions).

---

### `code_list_branches`

Lists Git branches that have a committed code index file.

| Parameter | Type | Required | Default | Description |
| --------- | ---- | -------- | ------- | ----------- |
| `repo` | string | no | both | Restrict to `"dotnet"` or `"front"` |

**Returns:** Markdown grouped by repo, showing available branches. Use
these values for the `branch` parameter of other code tools.

---

## NuGet tools

### `nuget_list`

Lists all published Granit NuGet packages with latest version, description,
and download count.

When `GRANIT_MCP_GITHUB_TOKEN` is set, also includes pre-release packages
from GitHub Packages.

| Parameter | Type | Required | Default | Description |
| --------- | ---- | -------- | ------- | ----------- |

*No parameters.*

**Returns:** Markdown list sorted by package ID. Download counts
formatted as `1.2k` when >= 1000. Source indicators:

| Indicator | Meaning |
| --------- | ------- |
| *(none)* | Published on nuget.org only |
| `[github]` | Published on GitHub Packages only |
| `[nuget.org+github]` | Published on both feeds |

**Example:**

```text
→ nuget_list()

## Granit NuGet packages (42)

- **Granit.BlobStorage** v2.1.0 — Blob storage abstraction layer (3.2k downloads)
- **Granit.Core** v3.0.0 — Core framework module (12.5k downloads) [nuget.org+github]
- **Granit.Core** v3.1.0-preview.1 — Core framework module (0 downloads) [github]
```

---

### `nuget_get`

Retrieves detailed information about a specific package: versions,
dependencies, license, and tags.

| Parameter | Type | Required | Default | Description |
| --------- | ---- | -------- | ------- | ----------- |
| `package` | string | yes | — | NuGet package ID (e.g. `"Granit.Core"`) |
| `version` | string | no | latest | Specific version to inspect |

**Returns:** Markdown with:

- Package name and version heading
- Description (blockquote)
- Metadata: authors, license, project URL, tags
- Dependencies grouped by target framework
- Last 10 versions with publication dates and source feed

**Example:**

```text
→ nuget_get("Granit.BlobStorage")

## Granit.BlobStorage v2.1.0

> Blob storage abstraction layer for the Granit framework

**Authors:** Jean-François Meyers
**License:** Apache-2.0
**Tags:** granit, blob, storage

### Dependencies

**net10.0**
- Granit.Core [3.0.0, )

### Recent versions

- v2.1.0 (2026-03-20)
- v2.0.0 (2026-03-01)
- v1.0.0 (2026-02-15)
```
