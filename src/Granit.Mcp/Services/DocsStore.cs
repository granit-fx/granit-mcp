using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Granit.Mcp.Services;

/// <summary>
/// SQLite FTS5-backed documentation store.
/// Parses llms-full.txt into articles by H1 headings and indexes them for search.
/// </summary>
public sealed class DocsStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly ILogger<DocsStore> _logger;
    private volatile bool _ready;

    public bool IsReady => _ready;

    public DocsStore(GranitMcpConfig config, ILogger<DocsStore> logger)
    {
        _logger = logger;

        Directory.CreateDirectory(config.DataDir);
        string dbPath = Path.Combine(config.DataDir, "docs.db");
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();

        InitSchema();
    }

    private void InitSchema()
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS state (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS docs USING fts5(
                id,
                title,
                category,
                content,
                tokenize='unicode61'
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Replaces all indexed docs with parsed articles from llms-full.txt content.
    /// </summary>
    public void Index(string markdownContent)
    {
        List<DocArticle> articles = ParseArticles(markdownContent);

        using SqliteTransaction tx = _db.BeginTransaction();

        using (SqliteCommand del = _db.CreateCommand())
        {
            del.CommandText = "DELETE FROM docs;";
            del.ExecuteNonQuery();
        }

        using (SqliteCommand insert = _db.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO docs (id, title, category, content) " +
                "VALUES ($id, $title, $category, $content);";
            SqliteParameter pId = insert.Parameters.Add("$id", SqliteType.Text);
            SqliteParameter pTitle = insert.Parameters.Add("$title", SqliteType.Text);
            SqliteParameter pCategory = insert.Parameters.Add("$category", SqliteType.Text);
            SqliteParameter pContent = insert.Parameters.Add("$content", SqliteType.Text);

            foreach (DocArticle article in articles)
            {
                pId.Value = article.Id;
                pTitle.Value = article.Title;
                pCategory.Value = article.Category;
                pContent.Value = article.Content;
                insert.ExecuteNonQuery();
            }
        }

        using (SqliteCommand state = _db.CreateCommand())
        {
            state.CommandText =
                "INSERT OR REPLACE INTO state (key, value) " +
                "VALUES ('last_indexed', $ts);";
            state.Parameters.AddWithValue("$ts",
                DateTime.UtcNow.ToString("O"));
            state.ExecuteNonQuery();
        }

        tx.Commit();
        _ready = true;

        _logger.LogInformation(
            "Indexed {Count} articles into FTS5", articles.Count);
    }

    /// <summary>
    /// FTS5 search. Returns lightweight results (id + title + snippet).
    /// </summary>
    public List<DocSearchResult> Search(string query, int limit = 6)
    {
        string escaped = EscapeQuery(query);
        if (string.IsNullOrWhiteSpace(escaped))
        {
            return [];
        }

        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT id, title, category,
                   snippet(docs, 3, '»', '«', '…', 40) AS snippet,
                   rank
            FROM docs
            WHERE docs MATCH $query
            ORDER BY rank
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$query", escaped);
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<DocSearchResult>();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new DocSearchResult(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }
        return results;
    }

    /// <summary>
    /// Fetch full article content by id.
    /// </summary>
    public DocArticle? GetById(string id)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            "SELECT id, title, category, content " +
            "FROM docs WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new DocArticle(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
    }

    /// <summary>
    /// List all articles in a given category.
    /// </summary>
    public List<DocSearchResult> ListByCategory(string category)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            "SELECT id, title, category, '' " +
            "FROM docs WHERE category = $cat ORDER BY title;";
        cmd.Parameters.AddWithValue("$cat", category);

        var results = new List<DocSearchResult>();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new DocSearchResult(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }
        return results;
    }

    /// <summary>
    /// Check if we have a cached index that's still fresh enough.
    /// </summary>
    public bool HasFreshIndex(int maxAgeHours)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            "SELECT value FROM state WHERE key = 'last_indexed';";
        string? result = cmd.ExecuteScalar() as string;
        if (result is null)
        {
            return false;
        }

        return DateTime.TryParse(result, out DateTime ts)
            && (DateTime.UtcNow - ts).TotalHours < maxAgeHours;
    }

    public string? EnsureReadyOrStatus()
    {
        return _ready
            ? null
            : """{"state":"Indexing","message":"Building FTS5 index from llms-full.txt..."}""";
    }

    // ─── Parsing ─────────────────────────────────────────────────────────

    private static List<DocArticle> ParseArticles(string markdown)
    {
        var articles = new List<DocArticle>();
        string[] lines = markdown.Split('\n');

        string? currentTitle = null;
        var contentLines = new List<string>();
        int id = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            // Skip the <SYSTEM> tag at the top
            if (line.StartsWith("<SYSTEM>", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal)
                && !line.StartsWith("## ", StringComparison.Ordinal))
            {
                // Save previous article
                if (currentTitle is not null && contentLines.Count > 0)
                {
                    articles.Add(BuildArticle(
                        id++, currentTitle, contentLines));
                }

                currentTitle = line[2..].Trim();
                contentLines.Clear();
            }
            else if (currentTitle is not null)
            {
                contentLines.Add(line);
            }
        }

        // Last article
        if (currentTitle is not null && contentLines.Count > 0)
        {
            articles.Add(BuildArticle(id, currentTitle, contentLines));
        }

        return articles;
    }

    private static DocArticle BuildArticle(
        int id, string title, List<string> contentLines)
    {
        string content = string.Join('\n', contentLines).Trim();
        string category = InferCategory(title, content);
        return new DocArticle($"doc-{id}", title, category, content);
    }

    private static string InferCategory(string title, string content)
    {
        string lower = title.ToLowerInvariant();
        if (lower.Contains("pattern") || lower.Contains("architecture"))
        {
            return "pattern";
        }

        if (lower.Contains("module") || lower.Contains("granit."))
        {
            return "module";
        }

        if (lower.Contains("getting started") || lower.Contains("quick start")
            || lower.Contains("crud"))
        {
            return "guide";
        }

        if (lower.Contains("gdpr") || lower.Contains("compliance")
            || lower.Contains("iso 27001") || lower.Contains("crypto"))
        {
            return "compliance";
        }

        if (content.Contains("```csharp") || content.Contains("```cs"))
        {
            return "reference";
        }

        return "general";
    }

    /// <summary>
    /// Escapes user query for FTS5 MATCH syntax.
    /// Wraps each term in double quotes and joins with OR.
    /// </summary>
    private static string EscapeQuery(string query)
    {
        IEnumerable<string> terms = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2)
            .Select(t => $"\"{t.Replace("\"", "")}\"");
        return string.Join(" OR ", terms);
    }

    public void Dispose() => _db.Dispose();
}

public sealed record DocSearchResult(
    string Id, string Title, string Category, string Snippet);

public sealed record DocArticle(
    string Id, string Title, string Category, string Content);
