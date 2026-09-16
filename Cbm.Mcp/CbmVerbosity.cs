namespace Cbm.Mcp;

internal enum CbmVerbosity
{
    Full,
    Compact,
}

internal static class CbmVerbosityParser
{
    public static CbmVerbosity Parse(string? value)
    {
        if (string.Equals(value?.Trim(), "compact", StringComparison.OrdinalIgnoreCase))
        {
            return CbmVerbosity.Compact;
        }

        return CbmVerbosity.Full;
    }
}
