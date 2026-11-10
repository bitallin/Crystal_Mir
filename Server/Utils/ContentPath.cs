namespace Server
{
    public static class ContentPath
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Dictionary<string, string>> DirectoryCache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            return path.Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
        }

        public static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            string normalized = Normalize(path);

            try
            {
                if (File.Exists(normalized) || Directory.Exists(normalized))
                    return normalized;

                string fullPath = Path.GetFullPath(normalized);
                string root = Path.GetPathRoot(fullPath);

                if (string.IsNullOrEmpty(root)) return normalized;

                string current = root;
                string remainder = fullPath.Substring(root.Length);
                string[] segments = remainder.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

                foreach (string segment in segments)
                {
                    string candidate = Path.Combine(current, segment);

                    if (File.Exists(candidate) || Directory.Exists(candidate))
                    {
                        current = candidate;
                        continue;
                    }

                    string match = FindEntry(current, segment);
                    if (match == null) return normalized;

                    current = Path.Combine(current, match);
                }

                return current;
            }
            catch
            {
                return normalized;
            }
        }

        private static string FindEntry(string directory, string name)
        {
            lock (CacheLock)
            {
                if (!DirectoryCache.TryGetValue(directory, out Dictionary<string, string> entries))
                {
                    entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    try
                    {
                        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                        {
                            string entryName = Path.GetFileName(entry);
                            entries.TryAdd(entryName, entryName);
                        }
                    }
                    catch
                    {
                    }

                    DirectoryCache[directory] = entries;
                }

                return entries.TryGetValue(name, out string match) ? match : null;
            }
        }
    }
}
