using Cbm.Graph;

namespace Cbm.Pipeline;

public static class CbmGitBranchNaming
{
    public static string SlugFromBranch(string? branch, bool isDetached)
    {
        if (isDetached)
        {
            return "detached";
        }

        var fallback = "working-tree";
        var source = string.IsNullOrWhiteSpace(branch) || string.Equals(branch, "DETACHED", StringComparison.Ordinal)
            ? fallback
            : branch;

        Span<char> buffer = stackalloc char[source.Length + 8];
        var length = 0;
        var inDash = false;

        foreach (var ch in source)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                if (length == 0 && ch == '-')
                {
                    inDash = true;
                    continue;
                }

                buffer[length++] = ch;
                inDash = false;
            }
            else if (length > 0 && !inDash)
            {
                buffer[length++] = '-';
                inDash = true;
            }
        }

        while (length > 0 && buffer[length - 1] == '-')
        {
            length--;
        }

        return length == 0 ? fallback : new string(buffer[..length]);
    }

    public static string DeriveBranchQualifiedName(string projectName, CbmGitContext context)
    {
        var project = string.IsNullOrWhiteSpace(projectName) ? "project" : projectName;
        var slug = context.IsDetached
            ? "detached"
            : context.IsGit && !string.IsNullOrWhiteSpace(context.BranchSlug)
                ? context.BranchSlug
                : "working-tree";
        return $"{project}.__branch__.{slug}";
    }
}
