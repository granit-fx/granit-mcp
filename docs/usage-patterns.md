# Usage Patterns

Practical workflows for getting the most out of Granit Tools MCP in your
AI assistant.

## Documentation: search then fetch

The documentation tools are designed as a two-step workflow that reduces
token consumption by 60-80% compared to returning full articles inline.

```text
Step 1 — docs_search("blob storage")
         → returns IDs + snippets (lightweight)

Step 2 — docs_get("doc-7")
         → returns full article content (only when needed)
```

**Why two steps?** The full documentation corpus is large. Returning
snippets first lets the AI assistant decide which article is relevant
before spending tokens on the full content.

### Discover architecture patterns

```text
docs_list_patterns()
→ shows all patterns with IDs

docs_get("doc-15")
→ read a specific pattern
```

## Code navigation: find then inspect

### Find a type

```text
code_search("IBlobStorage")
→ ranked results with FQN, project, file
```

### Inspect its API

```text
code_get_api("IBlobStorage")
→ full public API: methods, properties, events with signatures
```

### Filter by kind or repo

```text
code_search("Handler", kind="class", repo="dotnet")
→ only .NET classes matching "Handler"
```

### Understand project dependencies

```text
code_get_graph(repo="dotnet")
→ all projects with their → dependency chains
```

### Work on a feature branch

```text
code_list_branches(repo="dotnet")
→ see which branches have a code index

code_search("MyNewType", branch="feature/new-module")
→ search against a specific branch
```

## NuGet: discovery and inspection

### List all packages

```text
nuget_list()
→ all Granit.* packages with versions and download counts
```

### Check a specific package

```text
nuget_get("Granit.BlobStorage")
→ versions, dependencies, license, tags

nuget_get("Granit.Core", version="2.0.0")
→ inspect a specific version
```

### Include dev packages (GitHub Packages)

Set `GRANIT_MCP_GITHUB_TOKEN` in your config to also see pre-release
packages:

```text
nuget_list()
→ stable packages + dev previews with [github] indicator
```

## Combining tools effectively

### "How do I use blob storage?"

```text
1. docs_search("blob storage")     → find the guide
2. docs_get("doc-7")               → read the full guide
3. code_get_api("IBlobStorage")    → see the API surface
4. nuget_get("Granit.BlobStorage") → check latest version and deps
```

### "What changed in this module?"

```text
1. code_search("MyModule", repo="dotnet")   → find the type
2. code_get_api("MyModule")                  → current API surface
3. code_search("MyModule", branch="develop") → compare with develop
```

### "Which pattern should I use?"

```text
1. docs_list_patterns()    → browse all patterns
2. docs_get("doc-15")      → read the recommended pattern
3. code_search("Pattern")  → find existing implementations
```

## Tips

- **Use `docs_search` before asking the AI to generate code.** The
  documentation contains Granit-specific conventions that generic
  training data does not cover.
- **Use `code_get_api` instead of reading full source files.** It
  returns only the public API surface — far fewer tokens than a
  complete file read.
- **Set the `branch` parameter** when working on a feature branch that
  has its own code index. Run `code_list_branches` to check availability.
- **Use `repo` to narrow results** when you know whether you need
  .NET or TypeScript code.
