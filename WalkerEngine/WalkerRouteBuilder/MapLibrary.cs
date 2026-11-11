using System.Text.Json;

namespace WalkerRouteBuilder;

internal sealed class MapRecord
{
    public string ImagePath { get; set; } = "";

    public string? SourcePath { get; set; }
}

internal sealed class ScenarioRecord
{
    public string Name { get; set; } = "Сценарий";

    public string? FilePath { get; set; }

    public Scenario Document { get; set; } = new();

    public bool Dirty { get; set; }
}

internal sealed class RouteRecord
{
    public string Name { get; set; } = "Маршрут";

    public string FilePath { get; set; } = "";

    /// <summary>Имя подпапки в Database/routes (обычно SafeName карты).</summary>
    public string? MapFolder { get; set; }
}

internal sealed class MapLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public List<MapRecord> Maps { get; } = [];

    public List<ScenarioRecord> Scenarios { get; } = [];

    public List<ChainRecord> Chains { get; } = [];

    public static string Folder => ResolveFolder();

    public static string MapsFolder => Ensure(Path.Combine(Folder, "maps"));

    public static string ScenariosFolder => Ensure(Path.Combine(Folder, "scenarios"));

    public static string ChainsFolder => Ensure(Path.Combine(Folder, "chains"));

    public static string RoutesFolder => Ensure(Path.Combine(Folder, "routes"));

    public static string RoutesFolderFor(string? mapPath)
    {
        string folder = Path.Combine(RoutesFolder, MapFolderKey(mapPath));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string MapFolderKey(string? mapPath)
    {
        string name = string.IsNullOrWhiteSpace(mapPath) ? "общие" : Path.GetFileNameWithoutExtension(mapPath);
        return SafeName(name);
    }

    public static List<RouteRecord> DiscoverRoutes()
    {
        var routes = new List<RouteRecord>();
        string root = RoutesFolder;
        if (!Directory.Exists(root))
            return routes;

        foreach (string file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            string full = Path.GetFullPath(file);
            string? parent = Path.GetDirectoryName(full);
            string? folderName = parent is null ? null : Path.GetFileName(parent);
            if (string.Equals(folderName, "routes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
                folderName = null;

            string name = Path.GetFileNameWithoutExtension(full);
            if (name.EndsWith(".route", StringComparison.OrdinalIgnoreCase))
                name = name[..^".route".Length];

            routes.Add(new RouteRecord
            {
                Name = name.Length == 0 ? Path.GetFileName(full) : name,
                FilePath = full,
                MapFolder = string.IsNullOrWhiteSpace(folderName) ? null : folderName,
            });
        }

        routes.Sort((a, b) =>
        {
            int folder = string.Compare(a.MapFolder ?? "", b.MapFolder ?? "", StringComparison.CurrentCultureIgnoreCase);
            return folder != 0
                ? folder
                : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });
        return routes;
    }

    public static string FilePath => Path.Combine(Folder, "library.json");

    public static MapLibrary Load()
    {
        MigrateLegacyLibrary();
        var library = new MapLibrary();
        if (!File.Exists(FilePath))
            return library;

        LibraryDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<LibraryDto>(File.ReadAllText(FilePath), JsonOptions);
        }
        catch
        {
            return library;
        }

        if (dto is null)
            return library;

        bool migrated = false;
        foreach (LibraryMapDto mapDto in dto.Maps ?? [])
        {
            if (string.IsNullOrWhiteSpace(mapDto.Image))
                continue;
            string imagePath = ToFullPath(mapDto.Image);
            if (library.Maps.Any(item => string.Equals(item.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (!string.IsNullOrWhiteSpace(mapDto.Source) && Path.IsPathRooted(mapDto.Source))
                migrated = true;

            library.Maps.Add(new MapRecord
            {
                ImagePath = imagePath,
                SourcePath = string.IsNullOrWhiteSpace(mapDto.Source) ? null : ResolveStoredPath(mapDto.Source),
            });

            // Старый формат: сценарии висели на карте.
            foreach (LibraryScenarioDto scenarioDto in mapDto.Scenarios ?? [])
            {
                if (TryAddScenario(library, scenarioDto))
                    migrated = true;
            }
        }

        foreach (LibraryScenarioDto scenarioDto in dto.Scenarios ?? [])
        {
            if (!string.IsNullOrWhiteSpace(scenarioDto.File) && Path.IsPathRooted(scenarioDto.File))
                migrated = true;
            TryAddScenario(library, scenarioDto);
        }

        foreach (LibraryChainDto chainDto in dto.Chains ?? [])
        {
            if (!string.IsNullOrWhiteSpace(chainDto.File) && Path.IsPathRooted(chainDto.File))
                migrated = true;
            TryAddChain(library, chainDto);
        }

        if (ImportExternalMaps(library))
            migrated = true;

        bool legacyNested = dto.Scenarios is null
            && dto.Maps?.Any(map => map.Scenarios is { Count: > 0 }) == true;
        if (migrated || legacyNested)
            library.Save();

        return library;
    }

    private static bool TryAddScenario(MapLibrary library, LibraryScenarioDto scenarioDto)
    {
        if (string.IsNullOrWhiteSpace(scenarioDto.File))
            return false;
        string scenarioPath = ToFullPath(scenarioDto.File);
        if (!File.Exists(scenarioPath))
            return false;
        if (library.Scenarios.Any(item => string.Equals(item.FilePath, scenarioPath, StringComparison.OrdinalIgnoreCase)))
            return false;

        try
        {
            Scenario document = ScenarioFile.Load(scenarioPath, out _);
            library.Scenarios.Add(new ScenarioRecord
            {
                Name = string.IsNullOrWhiteSpace(scenarioDto.Name)
                    ? Path.GetFileNameWithoutExtension(scenarioPath)
                    : scenarioDto.Name,
                FilePath = scenarioPath,
                Document = document,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryAddChain(MapLibrary library, LibraryChainDto chainDto)
    {
        if (string.IsNullOrWhiteSpace(chainDto.File))
            return false;
        string chainPath = ToFullPath(chainDto.File);
        if (!File.Exists(chainPath))
            return false;
        if (library.Chains.Any(item => string.Equals(item.FilePath, chainPath, StringComparison.OrdinalIgnoreCase)))
            return false;

        try
        {
            ScenarioChain document = ChainFile.Load(chainPath);
            library.Chains.Add(new ChainRecord
            {
                Name = string.IsNullOrWhiteSpace(chainDto.Name)
                    ? Path.GetFileNameWithoutExtension(chainPath)
                    : chainDto.Name,
                FilePath = chainPath,
                Document = document,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ImportMap(string sourcePath)
    {
        string full = Path.GetFullPath(sourcePath);
        if (IsInside(full, MapsFolder))
            return full;

        string target = UniquePath(MapsFolder, Path.GetFileName(full));
        File.Copy(full, target, overwrite: false);
        return target;
    }

    public static string ImportRoute(string sourcePath, string? mapPath)
    {
        string full = Path.GetFullPath(sourcePath);
        string folder = RoutesFolderFor(mapPath);
        if (IsInside(full, folder))
            return full;

        string target = UniquePath(folder, Path.GetFileName(full));
        File.Copy(full, target, overwrite: false);
        return target;
    }

    public static void StoreBlockRoutes(Scenario scenario, string? mapPath, string scenarioName)
    {
        int index = 0;
        string? activeMap = mapPath ?? scenario.MapFile;
        foreach (ScenarioBlock block in Scenario.Walk(scenario.Blocks))
        {
            if (block is UseMapBlock useMap && !string.IsNullOrWhiteSpace(useMap.MapFile))
            {
                activeMap = useMap.MapFile;
                continue;
            }

            if (block is not FollowBlock follow)
                continue;
            if (follow.Anchors.All(anchor => anchor.Source != AnchorSource.Fixed))
                continue;

            string? routeMap = string.IsNullOrWhiteSpace(follow.MapFile) ? activeMap : follow.MapFile;
            follow.MapFile = routeMap;

            index++;
            var document = new RouteDocument { MapFile = routeMap };
            foreach (Anchor anchor in follow.Anchors)
            {
                if (anchor.Source != AnchorSource.Fixed)
                    continue;
                document.Points.Add(new RoutePointDto
                {
                    X = anchor.Position.X,
                    Y = anchor.Position.Y,
                    Keys = anchor.Keys.Count == 0
                        ? null
                        : anchor.Keys.Select(key => new RouteKeyDto
                        {
                            Key = key.Key.ToString(),
                            IntervalMilliseconds = key.Interval,
                        }).ToList(),
                });
            }

            string preferred = SafeName(scenarioName) + " " + index + ".route.json";
            follow.RouteFile = StoreRoute(document, routeMap, follow.RouteFile, preferred);
        }
    }

    public static bool IsInside(string path, string folder)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public void Save()
    {
        var dto = new LibraryDto
        {
            Maps = Maps.Select(map => new LibraryMapDto
            {
                Image = ToStoredPath(map.ImagePath),
                Source = ToStoredSource(map.SourcePath),
            }).ToList(),
            Scenarios = Scenarios
                .Where(item => !string.IsNullOrWhiteSpace(item.FilePath))
                .Select(item => new LibraryScenarioDto
                {
                    Name = item.Name,
                    File = ToStoredPath(item.FilePath!),
                })
                .ToList(),
            Chains = Chains
                .Where(item => !string.IsNullOrWhiteSpace(item.FilePath))
                .Select(item => new LibraryChainDto
                {
                    Name = item.Name,
                    File = ToStoredPath(item.FilePath!),
                })
                .ToList(),
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(dto, JsonOptions));
    }

    private static string ResolveFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "WalkerEngine.sln")))
                continue;
            return Ensure(Path.Combine(directory.FullName, "Database"));
        }

        return Ensure(Path.Combine(AppContext.BaseDirectory, "Database"));
    }

    private static string Ensure(string folder)
    {
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void MigrateLegacyLibrary()
    {
        if (File.Exists(FilePath))
            return;

        string legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WalkerRouteBuilder",
            "library.json");
        if (!File.Exists(legacy))
            return;

        LibraryDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<LibraryDto>(File.ReadAllText(legacy), JsonOptions);
        }
        catch
        {
            return;
        }

        if (dto?.Maps is null)
            return;

        var scenarios = new List<LibraryScenarioDto>();
        foreach (LibraryMapDto map in dto.Maps)
        {
            foreach (LibraryScenarioDto scenario in map.Scenarios ?? [])
            {
                if (string.IsNullOrWhiteSpace(scenario.File) || !File.Exists(scenario.File))
                    continue;
                string name = string.IsNullOrWhiteSpace(scenario.Name)
                    ? Path.GetFileNameWithoutExtension(scenario.File)
                    : scenario.Name;
                string target = UniquePath(ScenariosFolder, SafeName(name) + ".json");
                File.Copy(scenario.File, target, overwrite: false);
                scenarios.Add(new LibraryScenarioDto
                {
                    Name = name,
                    File = ToStoredPath(target),
                });
            }

            map.Scenarios = null;
        }

        dto.Scenarios = scenarios;
        File.WriteAllText(FilePath, JsonSerializer.Serialize(dto, JsonOptions));
    }

    private static string UniquePath(string folder, string fileName)
    {
        string path = Path.Combine(folder, fileName);
        int index = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(folder, Path.GetFileNameWithoutExtension(fileName) + " " + index + Path.GetExtension(fileName));
            index++;
        }

        return path;
    }

    private static bool ImportExternalMaps(MapLibrary library)
    {
        bool changed = false;
        foreach (MapRecord map in library.Maps)
        {
            if (string.IsNullOrWhiteSpace(map.ImagePath) || !File.Exists(map.ImagePath) || IsInside(map.ImagePath, MapsFolder))
                continue;

            string source = Path.GetFullPath(map.ImagePath);
            map.ImagePath = ImportMap(source);
            map.SourcePath = null;
            changed = true;
        }

        return changed;
    }

    private static string StoreRoute(RouteDocument document, string? mapPath, string? existingPath, string preferredName)
    {
        string folder = RoutesFolderFor(mapPath);
        string path = !string.IsNullOrWhiteSpace(existingPath) && IsInside(existingPath, folder)
            ? Path.GetFullPath(existingPath)
            : UniquePath(folder, preferredName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? preferredName : preferredName + ".json");
        if (!string.IsNullOrWhiteSpace(document.MapFile))
            document.MapFile = ToStoredPath(ToFullPath(document.MapFile));
        document.Save(path);
        return path;
    }

    public static string ToFullPath(string stored)
        => Path.IsPathRooted(stored) ? Path.GetFullPath(stored) : Path.GetFullPath(Path.Combine(Folder, stored));

    /// <summary>
    /// Путь для записи в JSON: всегда относительно Database/, без диска и абсолютных корней.
    /// Пути вне Database сжимаются через .. (например ..\WalkerEngine.Tests\003.png).
    /// </summary>
    public static string ToStoredPath(string fullPath)
    {
        string absolute = Path.GetFullPath(fullPath);
        string relative = Path.GetRelativePath(Folder, absolute);
        if (Path.IsPathRooted(relative))
            throw new InvalidOperationException($"Нельзя сохранить абсолютный путь вне диска базы: {fullPath}");
        return relative;
    }

    /// <summary>
    /// Source карты в library.json — только если файл лежит внутри Database; иначе null.
    /// </summary>
    public static string? ToStoredSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;
        if (string.Equals(source, "session-capture", StringComparison.OrdinalIgnoreCase))
            return source;
        try
        {
            string absolute = ToFullPath(source);
            if (!IsInside(absolute, Folder))
                return null;
            return Path.GetRelativePath(Folder, absolute);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveStoredPath(string stored)
    {
        try
        {
            return ToFullPath(stored);
        }
        catch
        {
            return stored;
        }
    }

    public static string NewScenarioFilePath(string name)
    {
        string safe = SafeName(name);
        return UniquePath(ScenariosFolder, safe + ".json");
    }

    public static string NewChainFilePath(string name)
    {
        string safe = SafeName(name);
        return UniquePath(ChainsFolder, safe + ".json");
    }

    public static string ImportScenarioFile(string sourcePath, string name)
    {
        string full = Path.GetFullPath(sourcePath);
        if (IsInside(full, ScenariosFolder))
            return full;

        string safe = SafeName(name);
        string path = UniquePath(ScenariosFolder, safe + ".json");
        File.Copy(full, path, overwrite: false);
        return path;
    }

    public ScenarioRecord? FindScenarioByPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        string full = ToFullPath(path);
        return Scenarios.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.FilePath)
            && string.Equals(ToFullPath(item.FilePath), full, StringComparison.OrdinalIgnoreCase));
    }

    private static string SafeName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        name = name.Trim();
        return name.Length == 0 ? "scenario" : name;
    }

    private sealed class LibraryDto
    {
        public List<LibraryMapDto>? Maps { get; set; }

        public List<LibraryScenarioDto>? Scenarios { get; set; }

        public List<LibraryChainDto>? Chains { get; set; }
    }

    private sealed class LibraryMapDto
    {
        public string? Image { get; set; }

        public string? Source { get; set; }

        /// <summary>Устарело: сценарии перенесены на уровень библиотеки.</summary>
        public List<LibraryScenarioDto>? Scenarios { get; set; }
    }

    private sealed class LibraryScenarioDto
    {
        public string? Name { get; set; }

        public string? File { get; set; }
    }

    private sealed class LibraryChainDto
    {
        public string? Name { get; set; }

        public string? File { get; set; }
    }
}
