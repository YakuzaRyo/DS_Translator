using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Configer.Utilities
{
    public static class ProjectRootLocator
    {
        private static readonly string[] DefaultSentinelFiles =
        {
            "pyproject.toml",
            "uv.toml",
            "Configer.csproj",
            "Configer.slnx"
        };

        public static string Resolve(params string[] sentinelFiles)
        {
            var sentinels = (sentinelFiles != null && sentinelFiles.Length > 0)
                ? sentinelFiles
                : DefaultSentinelFiles;

            foreach (var start in GetProbeDirectories())
            {
                var candidate = TryFindContainingDirectory(start, sentinels);
                if (!string.IsNullOrEmpty(candidate))
                {
                    return candidate!;
                }
            }

            // Fall back to current directory if nothing is found
            return Directory.GetCurrentDirectory();
        }

        private static IEnumerable<string?> GetProbeDirectories()
        {
            yield return Directory.GetCurrentDirectory();

            var extraProviders = new Func<string?>[]
            {
                () => SafeGet(() => AppContext.BaseDirectory),
                () => SafeGet(() => AppDomain.CurrentDomain.BaseDirectory)
            };

            foreach (var provider in extraProviders)
            {
                var path = provider();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return path;
                }
            }
        }

        private static string? SafeGet(Func<string?> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return null;
            }
        }

        private static string? TryFindContainingDirectory(string? start, IReadOnlyCollection<string> sentinels)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                return null;
            }

            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                if (sentinels.Any(s => File.Exists(Path.Combine(directory.FullName, s))))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }
    }
}
