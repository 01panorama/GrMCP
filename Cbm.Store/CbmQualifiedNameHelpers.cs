namespace Cbm.Store;

internal static class CbmQualifiedNameHelpers
{
    public static string QnToPackage(string? qualifiedName)
    {
        if (string.IsNullOrEmpty(qualifiedName))
        {
            return string.Empty;
        }

        var dots = new List<int>();
        for (var i = 0; i < qualifiedName.Length; i++)
        {
            if (qualifiedName[i] == '.')
            {
                dots.Add(i);
            }
        }

        if (dots.Count >= 3)
        {
            var start = dots[1] + 1;
            var len = dots[2] - start;
            if (len > 0 && len < 256)
            {
                return qualifiedName.Substring(start, len);
            }
        }

        if (dots.Count >= 1)
        {
            var start = dots[0] + 1;
            var end = dots.Count >= 2 ? dots[1] : qualifiedName.Length;
            var len = end - start;
            if (len > 0 && len < 256)
            {
                return qualifiedName.Substring(start, len);
            }
        }

        return string.Empty;
    }

    public static string QnToTopPackage(string? qualifiedName)
    {
        if (string.IsNullOrEmpty(qualifiedName))
        {
            return string.Empty;
        }

        var firstDot = qualifiedName.IndexOf('.');
        if (firstDot < 0)
        {
            return string.Empty;
        }

        var start = firstDot + 1;
        var secondDot = qualifiedName.IndexOf('.', start);
        var end = secondDot >= 0 ? secondDot : qualifiedName.Length;
        var len = end - start;
        if (len > 0 && len < 256)
        {
            return qualifiedName.Substring(start, len);
        }

        return string.Empty;
    }

    public static bool IsTestFilePath(string? filePath)
    {
        return !string.IsNullOrEmpty(filePath)
            && filePath.Contains("test", StringComparison.Ordinal);
    }
}
