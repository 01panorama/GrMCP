namespace Gb.Mcp;

internal enum GbVerbosity
{
    Full,
    Compact,
}

internal static class GbVerbosityParser
{
    public static GbVerbosity Parse(string? value)
    {
        if (string.Equals(value?.Trim(), "compact", StringComparison.OrdinalIgnoreCase))
        {
            return GbVerbosity.Compact;
        }

        return GbVerbosity.Full;
    }
}
