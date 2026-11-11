using System.Text.Json;
using System.Text.Json.Serialization;

namespace WalkerRouteBuilder;

/// <summary>Шаг цепочки — живая ссылка на сценарий из библиотеки.</summary>
internal sealed class ChainStep
{
    public string ScenarioFile { get; set; } = "";

    public int PauseAfterMilliseconds { get; set; }

    public ChainStep Clone()
        => new()
        {
            ScenarioFile = ScenarioFile,
            PauseAfterMilliseconds = PauseAfterMilliseconds,
        };
}

/// <summary>Последовательность сценариев.</summary>
internal sealed class ScenarioChain
{
    public bool Loop { get; set; }

    public List<ChainStep> Steps { get; } = [];

    public ScenarioChain Clone()
    {
        var copy = new ScenarioChain { Loop = Loop };
        foreach (ChainStep step in Steps)
            copy.Steps.Add(step.Clone());
        return copy;
    }
}

internal sealed class ChainRecord
{
    public string Name { get; set; } = "Цепочка";

    public string? FilePath { get; set; }

    public ScenarioChain Document { get; set; } = new();

    public bool Dirty { get; set; }
}

internal static class ChainFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Save(string path, ScenarioChain chain)
    {
        var dto = new ChainDto
        {
            Version = 1,
            Loop = chain.Loop,
            Steps = chain.Steps.Select(step => new StepDto
            {
                ScenarioFile = string.IsNullOrWhiteSpace(step.ScenarioFile)
                    ? null
                    : MapLibrary.ToStoredPath(step.ScenarioFile),
                PauseAfterMilliseconds = Math.Clamp(step.PauseAfterMilliseconds, 0, 120_000),
            }).ToList(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    public static ScenarioChain Load(string path)
    {
        var dto = JsonSerializer.Deserialize<ChainDto>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Файл цепочки пуст.");
        var chain = new ScenarioChain { Loop = dto.Loop };
        foreach (StepDto step in dto.Steps ?? [])
        {
            if (string.IsNullOrWhiteSpace(step.ScenarioFile))
                continue;
            chain.Steps.Add(new ChainStep
            {
                ScenarioFile = MapLibrary.ToFullPath(step.ScenarioFile),
                PauseAfterMilliseconds = Math.Clamp(step.PauseAfterMilliseconds, 0, 120_000),
            });
        }

        return chain;
    }

    private sealed class ChainDto
    {
        public int Version { get; set; } = 1;

        public bool Loop { get; set; }

        public List<StepDto>? Steps { get; set; }
    }

    private sealed class StepDto
    {
        public string? ScenarioFile { get; set; }

        public int PauseAfterMilliseconds { get; set; }
    }
}
