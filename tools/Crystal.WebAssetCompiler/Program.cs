using Crystal.WebAssets;

namespace Crystal.WebAssetCompiler;

internal static class Program
{
    public static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Options.PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            Options.PrintUsage();
            return 0;
        }

        List<AssetLibrarySource> libraries = [];
        foreach (string item in options.Libraries)
        {
            string path = Path.IsPathRooted(item) ? item : Path.Combine(options.ClientRoot, "Data", item);
            if (!path.EndsWith(".lib", StringComparison.OrdinalIgnoreCase)) path += ".lib";
            if (!File.Exists(path)) throw new FileNotFoundException($"Library not found: {path}");
            libraries.Add(new AssetLibrarySource(Path.GetFileNameWithoutExtension(path), path));
        }

        List<AssetMapSource> maps = [];
        foreach (string item in options.Maps)
        {
            string path = Path.IsPathRooted(item) ? item : Path.Combine(options.ClientRoot, "Map", item);
            if (!path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)) path += ".map";
            if (!File.Exists(path)) throw new FileNotFoundException($"Map not found: {path}");
            maps.Add(new AssetMapSource(Path.GetFileNameWithoutExtension(path), path));
        }

        if (options.AllMaps)
        {
            string mapRoot = Path.Combine(options.ClientRoot, "Map");
            foreach (AssetMapSource source in MapSourceDiscovery.Discover(mapRoot))
            {
                if (maps.All(value => !string.Equals(value.Id, source.Id, StringComparison.OrdinalIgnoreCase)))
                    maps.Add(source);
            }
        }

        if (libraries.Count == 0 && maps.Count == 0)
        {
            Console.Error.WriteLine("No libraries or maps selected. Use --lib and/or --map, or --all-maps.");
            return 2;
        }

        WebAssetCompileResult result = WebAssetCompilerService.Compile(new WebAssetCompileRequest
        {
            ClientRoot = Path.GetFullPath(options.ClientRoot),
            OutRoot = Path.GetFullPath(options.OutRoot),
            AssetVersion = options.AssetVersion,
            ClientBuildHash = options.ClientBuildHash,
            Libraries = libraries,
            Maps = maps,
            IncludeMapLibraries = options.IncludeMapLibraries,
        });

        Console.WriteLine($"Compiled {result.Manifest.Libraries.Count} libraries and {result.Manifest.Maps.Count} maps.");
        Console.WriteLine($"Asset version: {result.Manifest.AssetVersion}");
        Console.WriteLine($"Client build hash: {result.Manifest.ClientBuildHash}");
        Console.WriteLine($"Output: {Path.GetFullPath(options.OutRoot)}");
        foreach (string warning in result.Warnings) Console.Error.WriteLine($"WARN {warning}");
        return 0;
    }

    private sealed class Options
    {
        public string ClientRoot { get; private init; } = string.Empty;
        public string OutRoot { get; private init; } = string.Empty;
        public string AssetVersion { get; private init; } = "dev";
        public string ClientBuildHash { get; private init; } = string.Empty;
        public List<string> Libraries { get; } = [];
        public List<string> Maps { get; } = [];
        public bool AllMaps { get; private init; }
        public bool IncludeMapLibraries { get; private init; }
        public bool ShowHelp { get; private init; }

        public static Options Parse(string[] args)
        {
            string? clientRoot = null;
            string? outRoot = null;
            string assetVersion = "dev";
            string buildHash = string.Empty;
            List<string> libraries = [];
            List<string> maps = [];
            bool allMaps = false;
            bool includeMapLibraries = false;
            bool help = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {arg}.");
                switch (arg)
                {
                    case "--client-root": clientRoot = Next(); break;
                    case "--out": outRoot = Next(); break;
                    case "--version": assetVersion = Next(); break;
                    case "--build-hash": buildHash = Next(); break;
                    case "--lib": libraries.Add(Next()); break;
                    case "--map": maps.Add(Next()); break;
                    case "--all-maps": allMaps = true; break;
                    case "--map-libraries": includeMapLibraries = true; break;
                    case "-h":
                    case "--help": help = true; break;
                    default: throw new ArgumentException($"Unknown argument: {arg}");
                }
            }

            if (!help && string.IsNullOrWhiteSpace(clientRoot)) throw new ArgumentException("--client-root is required.");
            if (!help && string.IsNullOrWhiteSpace(outRoot)) throw new ArgumentException("--out is required.");
            if (!help && string.IsNullOrWhiteSpace(buildHash)) throw new ArgumentException("--build-hash is required from web-client/dist/client-build.json.");

            Options options = new()
            {
                ClientRoot = clientRoot ?? string.Empty,
                OutRoot = outRoot ?? string.Empty,
                AssetVersion = assetVersion,
                ClientBuildHash = buildHash,
                AllMaps = allMaps,
                IncludeMapLibraries = includeMapLibraries,
                ShowHelp = help,
            };
            options.Libraries.AddRange(libraries);
            options.Maps.AddRange(maps);
            return options;
        }

        public static void PrintUsage()
        {
            Console.WriteLine(
                """
                Crystal WebAssetCompiler
                  --client-root <dir>   Client root containing Data/ and Map/
                  --out <dir>           Output web asset root
                  --version <value>     Asset version (default: dev)
                  --lib <path>          Library path relative to Data/ or absolute; repeatable
                  --map <path>          Map path relative to Map/ or absolute; repeatable
                  --all-maps            Compile every .map under Map/
                  --map-libraries       Compile map libraries referenced by selected maps
                  --build-hash <value>  Web client build hash from client-build.json (required)
                """);
        }
    }
}
