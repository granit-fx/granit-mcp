namespace Granit.Mcp.Services;

/// <summary>
/// Detects the current Git branch from .git/HEAD in the working directory.
/// Falls back to "develop" if not in a git repo.
/// </summary>
public sealed class GitBranchDetector
{
    private const string DefaultBranch = "develop";
    private const string RefPrefix = "ref: refs/heads/";

    public static string DetectBranch()
    {
        try
        {
            string? gitDir = FindGitDir(Directory.GetCurrentDirectory());
            if (gitDir is null)
            {
                return DefaultBranch;
            }

            string headPath = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headPath))
            {
                return DefaultBranch;
            }

            string content = File.ReadAllText(headPath).Trim();
            return content.StartsWith(RefPrefix, StringComparison.Ordinal)
                ? content[RefPrefix.Length..]
                : DefaultBranch; // detached HEAD
        }
        catch
        {
            return DefaultBranch;
        }
    }

    private static string? FindGitDir(string startDir)
    {
        string? dir = startDir;
        while (dir is not null)
        {
            string gitDir = Path.Combine(dir, ".git");
            if (Directory.Exists(gitDir))
            {
                return gitDir;
            }

            if (File.Exists(gitDir)) // worktree: .git is a file
            {
                string content = File.ReadAllText(gitDir).Trim();
                if (content.StartsWith("gitdir:", StringComparison.Ordinal))
                {
                    return content["gitdir:".Length..].Trim();
                }
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
