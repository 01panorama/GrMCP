namespace Gb.Mcp;

internal static class GbIndexChildArgs
{
    internal const string ParentPidEnvironmentVariable = "GB_INDEX_PARENT_PID";

    internal static bool TryParse(string[] args, out string repoPath, out string resultFilePath)
    {
        repoPath = string.Empty;
        resultFilePath = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--index", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return false;
                }

                repoPath = args[index + 1];
                index++;
                continue;
            }

            if (string.Equals(args[index], "--result-file", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return false;
                }

                resultFilePath = args[index + 1];
                index++;
            }
        }

        return !string.IsNullOrWhiteSpace(repoPath) && !string.IsNullOrWhiteSpace(resultFilePath);
    }
}
