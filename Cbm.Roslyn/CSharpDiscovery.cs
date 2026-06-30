using System.Text;
using System.Text.RegularExpressions;

namespace Cbm.Roslyn;

public sealed class CSharpDiscovery
{
    private static readonly HashSet<string> AlwaysSkipDirectories = new(StringComparer.Ordinal)
    {
        ".git",
        ".hg",
        ".svn",
        ".worktrees",
        ".idea",
        ".vs",
        ".vscode",
        ".eclipse",
        ".claude",
        ".cache",
        ".eggs",
        ".env",
        ".mypy_cache",
        ".nox",
        ".pytest_cache",
        ".ruff_cache",
        ".tox",
        ".venv",
        "__pycache__",
        "env",
        "htmlcov",
        "site-packages",
        "venv",
        ".npm",
        ".nyc_output",
        ".pnpm-store",
        ".yarn",
        "bower_components",
        "coverage",
        "node_modules",
        ".next",
        ".nuxt",
        ".svelte-kit",
        ".angular",
        ".turbo",
        ".parcel-cache",
        ".docusaurus",
        ".expo",
        "bin",
        "dist",
        "obj",
        "Pods",
        "target",
        "temp",
        "tmp",
        ".terraform",
        ".serverless",
        "bazel-bin",
        "bazel-out",
        "bazel-testlogs",
        ".cargo",
        ".stack-work",
        ".dart_tool",
        "zig-cache",
        "zig-out",
        ".metals",
        ".bloop",
        ".bsp",
        ".ccls-cache",
        ".clangd",
        "elm-stuff",
        "_opam",
        ".cpcache",
        ".shadow-cljs",
        ".vercel",
        ".netlify",
        "deploy",
        "deployed",
        ".qdrant_code_embeddings",
        ".tmp",
        "vendor",
        "vendored",
    };

    private static readonly HashSet<string> FastSkipDirectories = new(StringComparer.Ordinal)
    {
        "generated",
        "gen",
        "auto-generated",
        "fixtures",
        "testdata",
        "test_data",
        "__tests__",
        "__mocks__",
        "__snapshots__",
        "__fixtures__",
        "__test__",
        "docs",
        "doc",
        "documentation",
        "examples",
        "example",
        "samples",
        "sample",
        "assets",
        "static",
        "public",
        "media",
        "third_party",
        "thirdparty",
        "3rdparty",
        "external",
        "migrations",
        "seeds",
        "e2e",
        "integration",
        "locale",
        "locales",
        "i18n",
        "l10n",
        "scripts",
        "tools",
        "hack",
        "build",
        "out",
    };

    private static readonly string[] AlwaysIgnoredSuffixes =
    [
        ".tmp",
        "~",
        ".pyc",
        ".pyo",
        ".o",
        ".a",
        ".so",
        ".dll",
        ".class",
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".ico",
        ".bmp",
        ".tiff",
        ".webp",
        ".svg",
        ".wasm",
        ".node",
        ".exe",
        ".bin",
        ".dat",
        ".db",
        ".sqlite",
        ".sqlite3",
        ".woff",
        ".woff2",
        ".ttf",
        ".eot",
        ".otf",
    ];

    private static readonly string[] FastIgnoredSuffixes =
    [
        ".zip",
        ".tar",
        ".gz",
        ".bz2",
        ".xz",
        ".rar",
        ".7z",
        ".jar",
        ".war",
        ".ear",
        ".mp3",
        ".mp4",
        ".avi",
        ".mov",
        ".wav",
        ".flac",
        ".ogg",
        ".mkv",
        ".webm",
        ".pdf",
        ".doc",
        ".docx",
        ".xls",
        ".xlsx",
        ".ppt",
        ".pptx",
        ".odt",
        ".ods",
        ".map",
        ".min.js",
        ".min.css",
        ".pem",
        ".crt",
        ".key",
        ".cer",
        ".p12",
        ".pb",
        ".avro",
        ".parquet",
        ".beam",
        ".elc",
        ".rlib",
        ".coverage",
        ".prof",
        ".out",
        ".patch",
        ".diff",
    ];

    private static readonly HashSet<string> FastSkipFilenames = new(StringComparer.Ordinal)
    {
        "LICENSE",
        "LICENSE.txt",
        "LICENSE.md",
        "LICENSE-MIT",
        "LICENSE-APACHE",
        "LICENCE",
        "LICENCE.txt",
        "LICENCE.md",
        "CHANGELOG",
        "CHANGELOG.md",
        "CHANGES.md",
        "HISTORY",
        "HISTORY.md",
        "AUTHORS",
        "AUTHORS.md",
        "CONTRIBUTORS",
        "CONTRIBUTORS.md",
        "CODEOWNERS",
        "go.sum",
        "yarn.lock",
        "pnpm-lock.yaml",
        "Pipfile.lock",
        "poetry.lock",
        "Gemfile.lock",
        "Cargo.lock",
        "mix.lock",
        "flake.lock",
        "pubspec.lock",
        "composer.lock",
        "package-lock.json",
        "configure",
        "Makefile.in",
        "config.guess",
        "config.sub",
    };

    private static readonly string[] FastPatterns =
    [
        ".d.ts",
        ".bundle.",
        ".chunk.",
        ".generated.",
        ".pb.go",
        "_pb2.py",
        ".pb2.py",
        "_grpc.pb.go",
        "_string.go",
        "mock_",
        "_mock.",
        "_test_helpers.",
        ".stories.",
        ".spec.",
        ".test.",
    ];

    public IReadOnlyList<DiscoveredCSharpFile> Discover(
        string rootPath,
        CSharpDiscoveryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        options ??= new CSharpDiscoveryOptions();

        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        var matchers = LoadRootIgnoreMatchers(root, options);
        var files = new List<DiscoveredCSharpFile>();
        Walk(root, relativePrefix: string.Empty, matchers, options, files);

        return files
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static List<IgnoreMatcher> LoadRootIgnoreMatchers(
        string root,
        CSharpDiscoveryOptions options)
    {
        var matchers = new List<IgnoreMatcher>();
        AddIgnoreFile(matchers, Path.Combine(root, ".gitignore"), baseRelativePath: string.Empty);
        AddIgnoreFile(matchers, Path.Combine(root, ".git", "info", "exclude"), baseRelativePath: string.Empty);

        var cbmIgnorePath = string.IsNullOrWhiteSpace(options.IgnoreFilePath)
            ? Path.Combine(root, ".cbmignore")
            : options.IgnoreFilePath;
        AddIgnoreFile(matchers, cbmIgnorePath, baseRelativePath: string.Empty);

        return matchers;
    }

    private static void Walk(
        string directoryPath,
        string relativePrefix,
        IReadOnlyList<IgnoreMatcher> inheritedMatchers,
        CSharpDiscoveryOptions options,
        List<DiscoveredCSharpFile> files)
    {
        var matchers = inheritedMatchers;
        if (!string.IsNullOrEmpty(relativePrefix))
        {
            var nestedMatchers = new List<IgnoreMatcher>(inheritedMatchers);
            AddIgnoreFile(nestedMatchers, Path.Combine(directoryPath, ".gitignore"), relativePrefix);
            matchers = nestedMatchers;
        }

        foreach (var directory in SafeEnumerateDirectories(directoryPath))
        {
            if (HasReparsePoint(directory.FullName))
            {
                continue;
            }

            var relativePath = CombineRelative(relativePrefix, directory.Name);
            if (ShouldSkipDirectoryName(directory.Name, options.Mode) ||
                IsIgnored(matchers, relativePath, isDirectory: true))
            {
                continue;
            }

            Walk(directory.FullName, relativePath, matchers, options, files);
        }

        foreach (var file in SafeEnumerateFiles(directoryPath))
        {
            if (HasReparsePoint(file.FullName))
            {
                continue;
            }

            var relativePath = CombineRelative(relativePrefix, file.Name);
            if (!string.Equals(file.Extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
                ShouldSkipFilename(file.Name, options.Mode) ||
                HasIgnoredSuffix(file.Name, options.Mode) ||
                MatchesFastPattern(file.Name, options.Mode) ||
                IsIgnored(matchers, relativePath, isDirectory: false) ||
                IsTooLarge(file, options.MaxFileSizeBytes))
            {
                continue;
            }

            files.Add(new DiscoveredCSharpFile(
                file.FullName,
                relativePath,
                file.Length,
                File.GetLastWriteTimeUtc(file.FullName).Ticks * 100,
                CSharpFileFingerprint.ComputeSha256(file.FullName)));
        }
    }

    private static bool ShouldSkipDirectoryName(string name, CSharpIndexMode mode)
    {
        return AlwaysSkipDirectories.Contains(name) ||
            (mode != CSharpIndexMode.Full && FastSkipDirectories.Contains(name));
    }

    private static bool ShouldSkipFilename(string name, CSharpIndexMode mode)
    {
        return mode != CSharpIndexMode.Full && FastSkipFilenames.Contains(name);
    }

    private static bool HasIgnoredSuffix(string name, CSharpIndexMode mode)
    {
        return AlwaysIgnoredSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)) ||
            (mode != CSharpIndexMode.Full &&
                FastIgnoredSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)));
    }

    private static bool MatchesFastPattern(string name, CSharpIndexMode mode)
    {
        return mode != CSharpIndexMode.Full &&
            FastPatterns.Any(pattern => name.Contains(pattern, StringComparison.Ordinal));
    }

    private static bool IsIgnored(
        IEnumerable<IgnoreMatcher> matchers,
        string relativePath,
        bool isDirectory)
    {
        bool? ignored = null;
        foreach (var matcher in matchers)
        {
            var result = matcher.IsIgnored(relativePath, isDirectory);
            if (result.HasValue)
            {
                ignored = result.Value;
            }
        }

        return ignored == true;
    }

    private static void AddIgnoreFile(
        List<IgnoreMatcher> matchers,
        string? path,
        string baseRelativePath)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var patterns = File.ReadLines(path)
            .Select(IgnorePattern.Parse)
            .Where(pattern => pattern is not null)
            .Cast<IgnorePattern>()
            .ToArray();

        if (patterns.Length > 0)
        {
            matchers.Add(new IgnoreMatcher(NormalizeRelativePath(baseRelativePath), patterns));
        }
    }

    private static bool IsTooLarge(FileInfo file, long? maxFileSizeBytes)
    {
        return maxFileSizeBytes.HasValue && file.Length > maxFileSizeBytes.Value;
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(string directoryPath)
    {
        try
        {
            return new DirectoryInfo(directoryPath)
                .EnumerateDirectories()
                .OrderBy(directory => directory.Name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<FileInfo> SafeEnumerateFiles(string directoryPath)
    {
        try
        {
            return new DirectoryInfo(directoryPath)
                .EnumerateFiles()
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string CombineRelative(string prefix, string name)
    {
        return string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}";
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .Trim('/');
    }

    private sealed class IgnoreMatcher(
        string baseRelativePath,
        IReadOnlyList<IgnorePattern> patterns)
    {
        public bool? IsIgnored(string relativePath, bool isDirectory)
        {
            var localPath = ToLocalPath(relativePath);
            if (localPath is null)
            {
                return null;
            }

            bool? ignored = null;
            foreach (var pattern in patterns)
            {
                if (pattern.IsMatch(localPath, isDirectory))
                {
                    ignored = !pattern.Negated;
                }
            }

            return ignored;
        }

        private string? ToLocalPath(string relativePath)
        {
            relativePath = NormalizeRelativePath(relativePath);
            if (string.IsNullOrEmpty(baseRelativePath))
            {
                return relativePath;
            }

            if (string.Equals(relativePath, baseRelativePath, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var prefix = baseRelativePath + "/";
            return relativePath.StartsWith(prefix, StringComparison.Ordinal)
                ? relativePath[prefix.Length..]
                : null;
        }
    }

    private sealed partial class IgnorePattern
    {
        private readonly Regex regex;
        private readonly bool hasSlash;

        private IgnorePattern(
            string pattern,
            bool negated,
            bool directoryOnly,
            bool anchored)
        {
            Pattern = pattern;
            Negated = negated;
            DirectoryOnly = directoryOnly;
            Anchored = anchored;
            hasSlash = pattern.Contains('/', StringComparison.Ordinal);
            regex = new Regex(
                "^" + GlobToRegex(pattern) + "$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        public string Pattern { get; }
        public bool Negated { get; }
        public bool DirectoryOnly { get; }
        public bool Anchored { get; }

        public static IgnorePattern? Parse(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                return null;
            }

            var negated = trimmed.StartsWith('!');
            if (negated)
            {
                trimmed = trimmed[1..].TrimStart();
            }

            if (trimmed.Length == 0)
            {
                return null;
            }

            var anchored = trimmed.StartsWith('/');
            trimmed = trimmed.TrimStart('/');

            var directoryOnly = trimmed.EndsWith('/');
            trimmed = trimmed.TrimEnd('/');
            if (trimmed.Length == 0)
            {
                return null;
            }

            return new IgnorePattern(trimmed, negated, directoryOnly, anchored);
        }

        public bool IsMatch(string relativePath, bool isDirectory)
        {
            relativePath = NormalizeRelativePath(relativePath);

            if (DirectoryOnly && !isDirectory && !PathContainsDirectory(relativePath))
            {
                return false;
            }

            if (!hasSlash)
            {
                return MatchBasename(relativePath);
            }

            if (Anchored)
            {
                return regex.IsMatch(relativePath) ||
                    (DirectoryOnly && relativePath.StartsWith(Pattern + "/", StringComparison.Ordinal));
            }

            return regex.IsMatch(relativePath) ||
                regex.IsMatch(Path.GetFileName(relativePath)) ||
                relativePath.EndsWith("/" + Pattern, StringComparison.Ordinal) ||
                (DirectoryOnly && relativePath.Contains("/" + Pattern + "/", StringComparison.Ordinal));
        }

        private bool MatchBasename(string relativePath)
        {
            var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Any(segment => regex.IsMatch(segment));
        }

        private bool PathContainsDirectory(string relativePath)
        {
            return relativePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .SkipLast(1)
                .Any(segment => regex.IsMatch(segment));
        }

        private static string GlobToRegex(string pattern)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < pattern.Length; i++)
            {
                var current = pattern[i];
                if (current == '*')
                {
                    var isDoubleStar = i + 1 < pattern.Length && pattern[i + 1] == '*';
                    builder.Append(isDoubleStar ? ".*" : "[^/]*");
                    if (isDoubleStar)
                    {
                        i++;
                    }
                }
                else if (current == '?')
                {
                    builder.Append("[^/]");
                }
                else
                {
                    builder.Append(Regex.Escape(current.ToString()));
                }
            }

            return builder.ToString();
        }
    }
}

public enum CSharpIndexMode
{
    Full,
    Moderate,
    Fast,
}

public sealed record CSharpDiscoveryOptions
{
    public CSharpIndexMode Mode { get; init; } = CSharpIndexMode.Full;
    public string? IgnoreFilePath { get; init; }
    public long? MaxFileSizeBytes { get; init; }
}

public sealed record DiscoveredCSharpFile(
    string Path,
    string RelativePath,
    long Size,
    long MtimeNs,
    string Sha256);
