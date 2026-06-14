using PullSight.Api.Contracts.GitHub;

namespace PullSight.Api.Services.GitHub;

public static class GitHubDiffLineMapper
{
    public static bool IsChangedRightSideLine(
        GitHubPullRequestDiffResponse diff,
        string filePath,
        int lineNumber)
    {
        var file = diff.Files.FirstOrDefault(item =>
            string.Equals(item.FileName, filePath, StringComparison.Ordinal));
        if (file?.Patch is null || lineNumber <= 0)
        {
            return false;
        }

        var currentNewLine = 0;
        foreach (var line in file.Patch.Split('\n'))
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                var plusIndex = line.IndexOf('+');
                if (plusIndex < 0)
                {
                    continue;
                }

                var start = line[(plusIndex + 1)..].Split(',', ' ')[0];
                _ = int.TryParse(start, out currentNewLine);
                continue;
            }

            if (currentNewLine <= 0)
            {
                continue;
            }

            if (line.StartsWith("-", StringComparison.Ordinal)
                && !line.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            var isAdded = line.StartsWith("+", StringComparison.Ordinal)
                && !line.StartsWith("+++", StringComparison.Ordinal);
            if (currentNewLine == lineNumber)
            {
                return isAdded;
            }

            currentNewLine++;
        }

        return false;
    }
}

