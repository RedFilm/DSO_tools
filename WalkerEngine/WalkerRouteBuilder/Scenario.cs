using System.Text.Json;
using System.Text.Json.Serialization;
using WalkerEngine;

namespace WalkerRouteBuilder;

internal readonly record struct RouteKey(Keys Key, int Interval);

internal enum TravelMode
{
    Vectors,
    Shortest,
}

internal enum RepeatCondition
{
    WhileRed,
}

internal enum AnchorSource
{
    Fixed,
    Player,
    Skull,
    RouteStart,
    Chest,
    QuestQuestion,
    QuestExclaim,
}

internal enum GoalWait
{
    UntilAppears,
    Skip,
    Timed,
}

internal sealed class Scenario
{
    public bool Loop { get; set; }

    public bool PressKeys { get; set; } = true;

    /// <summary>Активная карта сценария (для холста и блоков без своей привязки).</summary>
    public string? MapFile { get; set; }

    /// <summary>Все карты, которые использует сценарий. Может быть несколько локаций.</summary>
    public List<string> MapFiles { get; } = [];

    public List<ScenarioBlock> Blocks { get; } = [];

    public Scenario Clone()
    {
        var copy = new Scenario
        {
            Loop = Loop,
            PressKeys = PressKeys,
            MapFile = MapFile,
        };
        copy.MapFiles.AddRange(MapFiles);
        foreach (ScenarioBlock block in Blocks)
            copy.Blocks.Add(block.Clone());
        return copy;
    }

    public void EnsureMap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        string full = Path.GetFullPath(path);
        if (MapFiles.All(item => !string.Equals(item, full, StringComparison.OrdinalIgnoreCase)))
            MapFiles.Add(full);
        MapFile ??= full;
    }

    public IReadOnlyList<string> AllMaps()
    {
        var list = new List<string>();
        foreach (string path in MapFiles)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            string full = Path.GetFullPath(path);
            if (list.All(item => !string.Equals(item, full, StringComparison.OrdinalIgnoreCase)))
                list.Add(full);
        }

        if (MapFile is { Length: > 0 } active)
        {
            string full = Path.GetFullPath(active);
            if (list.All(item => !string.Equals(item, full, StringComparison.OrdinalIgnoreCase)))
                list.Insert(0, full);
        }

        return list;
    }

    public static Scenario CreateTemplate(int pauseMilliseconds)
    {
        var route = new FollowBlock { Travel = TravelMode.Vectors };
        var hunt = new FollowBlock
        {
            Travel = TravelMode.Shortest,
            DelayMilliseconds = Math.Clamp(pauseMilliseconds, 0, 120_000),
        };
        hunt.Anchors.Add(new Anchor { Source = AnchorSource.Player });
        hunt.Anchors.Add(new Anchor { Source = AnchorSource.Skull });

        var repeat = new RepeatBlock();
        repeat.Children.Add(hunt);
        repeat.Children.Add(new LockBlock());

        var back = new FollowBlock { Travel = TravelMode.Shortest };
        back.Anchors.Add(new Anchor { Source = AnchorSource.Player });
        back.Anchors.Add(new Anchor { Source = AnchorSource.RouteStart });

        var scenario = new Scenario { Loop = true };
        scenario.Blocks.Add(route);
        scenario.Blocks.Add(repeat);
        scenario.Blocks.Add(back);
        scenario.Blocks.Add(new SequenceBlock());
        return scenario;
    }

    public MapPoint? FirstFixedPoint()
    {
        foreach (ScenarioBlock block in Walk(Blocks))
        {
            if (block is not FollowBlock follow)
                continue;
            foreach (Anchor anchor in follow.Anchors)
            {
                if (anchor.Source == AnchorSource.Fixed)
                    return anchor.Position;
            }
        }

        return null;
    }

    public static IEnumerable<ScenarioBlock> Walk(IEnumerable<ScenarioBlock> blocks)
    {
        foreach (ScenarioBlock block in blocks)
        {
            yield return block;
            if (block is RepeatBlock repeat)
            {
                foreach (ScenarioBlock child in Walk(repeat.Children))
                    yield return child;
            }
        }
    }
}

internal abstract class ScenarioBlock
{
    public int PauseAfterMilliseconds { get; set; }

    public abstract ScenarioBlock Clone();

    public abstract string Title { get; }
}

internal sealed class Anchor
{
    public AnchorSource Source { get; set; } = AnchorSource.Fixed;

    public MapPoint Position { get; set; }

    public List<RouteKey> Keys { get; } = [];

    public GoalWait Wait { get; set; } = GoalWait.Timed;

    public int WaitMilliseconds { get; set; } = 2000;

    public Anchor Clone()
    {
        var copy = new Anchor
        {
            Source = Source,
            Position = Position,
            Wait = Wait,
            WaitMilliseconds = WaitMilliseconds,
        };
        copy.Keys.AddRange(Keys);
        return copy;
    }
}

internal sealed class FollowBlock : ScenarioBlock
{
    public TravelMode Travel { get; set; } = TravelMode.Vectors;

    public int DelayMilliseconds { get; set; }

    /// <summary>
    /// Сколько ждать пропажу live-цели (череп/сундук/знак) уже в пути.
    /// 0 — не прерывать погоню, если значок исчез.
    /// </summary>
    public int ChaseLostMilliseconds { get; set; } = 1500;

    public List<Anchor> Anchors { get; } = [];

    public string? RouteFile { get; set; }

    /// <summary>Карта, к которой относятся точки этого маршрута. Null — активная карта сценария.</summary>
    public string? MapFile { get; set; }

    public override string Title
        => Travel == TravelMode.Shortest ? "Следовать маршруту · кратчайший" : "Следовать маршруту · по векторам";

    public override ScenarioBlock Clone()
    {
        var copy = new FollowBlock
        {
            Travel = Travel,
            DelayMilliseconds = DelayMilliseconds,
            ChaseLostMilliseconds = ChaseLostMilliseconds,
            RouteFile = RouteFile,
            MapFile = MapFile,
            PauseAfterMilliseconds = PauseAfterMilliseconds,
        };
        foreach (Anchor anchor in Anchors)
            copy.Anchors.Add(anchor.Clone());
        return copy;
    }
}

internal sealed class UseMapBlock : ScenarioBlock
{
    public string? MapFile { get; set; }

    public override string Title
        => string.IsNullOrWhiteSpace(MapFile)
            ? "Сменить карту"
            : "Сменить карту · " + Path.GetFileNameWithoutExtension(MapFile);

    public override ScenarioBlock Clone()
        => new UseMapBlock
        {
            MapFile = MapFile,
            PauseAfterMilliseconds = PauseAfterMilliseconds,
        };
}

internal sealed class LockBlock : ScenarioBlock
{
    public AnchorSource Target { get; set; } = AnchorSource.Skull;

    public int RadiusPixels { get; set; } = 80;

    /// <summary>Сколько ждать появления цели и подтверждения её исчезновения.</summary>
    public int WaitMilliseconds { get; set; } = 1500;

    public List<RouteKey> Keys { get; } = [];

    public override string Title => "Зафиксироваться на цели";

    public override ScenarioBlock Clone()
    {
        var copy = new LockBlock
        {
            Target = Target,
            RadiusPixels = RadiusPixels,
            WaitMilliseconds = WaitMilliseconds,
            PauseAfterMilliseconds = PauseAfterMilliseconds,
        };
        copy.Keys.AddRange(Keys);
        return copy;
    }
}

internal sealed class OpenChestBlock : ScenarioBlock
{
    /// <summary>Сколько ждать появление сундука и подтверждение исчезновения после открытия.</summary>
    public int WaitMilliseconds { get; set; } = 1500;

    public override string Title => "Открыть ближайший сундук";

    public override ScenarioBlock Clone()
        => new OpenChestBlock
        {
            WaitMilliseconds = WaitMilliseconds,
            PauseAfterMilliseconds = PauseAfterMilliseconds,
        };
}

internal sealed class DoorBlock : ScenarioBlock
{
    /// <summary>Сложность локации 1…7 (Обычный…Кровопролитный).</summary>
    public int Difficulty { get; set; } = 1;

    public override string Title => "Войти в ближайшую дверь";

    public string DifficultyName => DoorDifficulties.Name(Difficulty);

    public override ScenarioBlock Clone()
        => new DoorBlock
        {
            Difficulty = DoorDifficulties.Clamp(Difficulty),
            PauseAfterMilliseconds = PauseAfterMilliseconds,
        };
}

/// <summary>Сложности окна входа после клика по двери.</summary>
internal static class DoorDifficulties
{
    public static readonly string[] Names =
    [
        "Обычный",
        "Болезненный",
        "Мучительный",
        "Смертельный",
        "Адский",
        "Беспощадный",
        "Кровопролитный",
    ];

    public static int Clamp(int difficulty)
        => Math.Clamp(difficulty, 1, Names.Length);

    public static string Name(int difficulty)
        => Names[Clamp(difficulty) - 1];
}

internal sealed class ClickQuestMarkBlock : ScenarioBlock
{
    public AnchorSource Mark { get; set; } = AnchorSource.QuestQuestion;

    public override string Title => Mark == AnchorSource.QuestExclaim
        ? "Клик по знаку восклицания"
        : "Клик по знаку вопроса";

    public override ScenarioBlock Clone()
        => new ClickQuestMarkBlock
        {
            Mark = Mark == AnchorSource.QuestExclaim ? AnchorSource.QuestExclaim : AnchorSource.QuestQuestion,
            PauseAfterMilliseconds = PauseAfterMilliseconds,
        };
}

internal sealed class AcceptDialogBlock : ScenarioBlock
{
    public override string Title => "Принять диалог";

    public override ScenarioBlock Clone()
        => new AcceptDialogBlock { PauseAfterMilliseconds = PauseAfterMilliseconds };
}

/// <summary>Жмёт O, пока не появится голубая полоска посадки, затем ждёт её исчезновения.</summary>
internal sealed class MountHorsesBlock : ScenarioBlock
{
    /// <summary>Интервал повторного нажатия O, пока полоска не появилась.</summary>
    public int KeyIntervalMilliseconds { get; set; } = 1000;

    public override string Title => "По кОням!";

    public override ScenarioBlock Clone()
        => new MountHorsesBlock
        {
            PauseAfterMilliseconds = PauseAfterMilliseconds,
            KeyIntervalMilliseconds = KeyIntervalMilliseconds,
        };
}

internal sealed class SequenceBlock : ScenarioBlock
{
    public List<ScreenClick> Clicks { get; } = [];

    public override string Title => "Воспроизвести последовательность";

    public override ScenarioBlock Clone()
    {
        var copy = new SequenceBlock { PauseAfterMilliseconds = PauseAfterMilliseconds };
        foreach (ScreenClick click in Clicks)
            copy.Clicks.Add(click.Clone());
        return copy;
    }
}

internal sealed class ScreenClick
{
    public int X { get; set; }

    public int Y { get; set; }

    public int PauseMilliseconds { get; set; }

    public int HoldMilliseconds { get; set; } = 40;

    public string? Name { get; set; }

    public ScreenClick Clone()
        => new()
        {
            X = X,
            Y = Y,
            PauseMilliseconds = PauseMilliseconds,
            HoldMilliseconds = HoldMilliseconds,
            Name = Name,
        };
}

internal sealed class RepeatBlock : ScenarioBlock
{
    public List<ScenarioBlock> Children { get; } = [];

    public RepeatCondition Condition { get; set; } = RepeatCondition.WhileRed;

    /// <summary>Сколько ждать первое появление красного, прежде чем пропустить повтор.</summary>
    public int WaitMilliseconds { get; set; } = 8000;

    public override string Title => Condition switch
    {
        RepeatCondition.WhileRed => "Повторять, пока есть красный",
        _ => "Повторять",
    };

    public override ScenarioBlock Clone()
    {
        var copy = new RepeatBlock
        {
            PauseAfterMilliseconds = PauseAfterMilliseconds,
            Condition = Condition,
            WaitMilliseconds = WaitMilliseconds,
        };
        foreach (ScenarioBlock child in Children)
            copy.Children.Add(child.Clone());
        return copy;
    }
}

internal static class ScenarioFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Save(string path, Scenario scenario)
    {
        var maps = scenario.AllMaps()
            .Select(MapLibrary.ToStoredPath)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var document = new ScenarioDto
        {
            Version = 2,
            MapFile = string.IsNullOrWhiteSpace(scenario.MapFile) ? null : MapLibrary.ToStoredPath(scenario.MapFile),
            Maps = maps.Count == 0 ? null : maps,
            Loop = scenario.Loop,
            PressKeys = scenario.PressKeys,
            Blocks = scenario.Blocks.Select(BlockDto.From).ToList(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
    }

    public static Scenario Load(string path)
        => Load(path, out _);

    public static Scenario Load(string path, out bool legacy)
    {
        legacy = false;
        string json = File.ReadAllText(path);
        using var parsed = JsonDocument.Parse(json);
        if (!parsed.RootElement.TryGetProperty("blocks", out JsonElement blocks) || blocks.ValueKind != JsonValueKind.Array)
        {
            legacy = true;
            return FromLegacy(RouteDocument.Load(path));
        }

        var document = JsonSerializer.Deserialize<ScenarioDto>(json, JsonOptions)
            ?? throw new InvalidDataException("Файл сценария пуст.");
        return document.ToScenario();
    }

    private static Scenario FromLegacy(RouteDocument document)
    {
        var follow = new FollowBlock { Travel = TravelMode.Vectors };
        bool legacy = document.Points.Count <= 1 || document.Points.Skip(1).All(point => point.Keys is null);
        for (int i = 0; i < document.Points.Count; i++)
        {
            RoutePointDto point = document.Points[i];
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new InvalidDataException("В маршруте есть координата, которая не является числом.");

            var anchor = new Anchor
            {
                Source = AnchorSource.Fixed,
                Position = new MapPoint(point.X, point.Y),
            };
            if (!legacy && i > 0 && point.Keys is { } stored)
            {
                foreach (RouteKeyDto item in stored)
                {
                    if (TryKey(item.Key, item.IntervalMilliseconds, out RouteKey key))
                        anchor.Keys.Add(key);
                }
            }

            follow.Anchors.Add(anchor);
        }

        var scenario = new Scenario { MapFile = document.MapFile };
        if (follow.Anchors.Count > 0)
            scenario.Blocks.Add(follow);
        return scenario;
    }

    internal static bool TryKey(string? name, int interval, out RouteKey key)
    {
        key = new RouteKey(Keys.None, 1000);
        if (string.IsNullOrWhiteSpace(name) || !Enum.TryParse(name, ignoreCase: true, out Keys parsed) || parsed == Keys.None)
            return false;
        if (parsed is Keys.F6 or Keys.F7 or Keys.F9)
            return false;

        key = new RouteKey(parsed, Math.Clamp(interval, 50, 120_000));
        return true;
    }

    private sealed class ScenarioDto
    {
        public int Version { get; set; } = 2;

        public string? MapFile { get; set; }

        public List<string>? Maps { get; set; }

        public bool Loop { get; set; }

        public bool PressKeys { get; set; } = true;

        public List<BlockDto> Blocks { get; set; } = [];

        public Scenario ToScenario()
        {
            var scenario = new Scenario
            {
                Loop = Loop,
                PressKeys = PressKeys,
                MapFile = string.IsNullOrWhiteSpace(MapFile) ? null : MapLibrary.ToFullPath(MapFile),
            };
            if (Maps is not null)
            {
                foreach (string map in Maps)
                {
                    if (!string.IsNullOrWhiteSpace(map))
                        scenario.EnsureMap(MapLibrary.ToFullPath(map));
                }
            }

            if (scenario.MapFile is { Length: > 0 })
                scenario.EnsureMap(scenario.MapFile);
            foreach (BlockDto block in Blocks)
                scenario.Blocks.Add(block.Materialize());
            return scenario;
        }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(FollowBlockDto), "followRoute")]
    [JsonDerivedType(typeof(RepeatBlockDto), "repeatWhileSkull")]
    [JsonDerivedType(typeof(LockBlockDto), "lockOnTarget")]
    [JsonDerivedType(typeof(OpenChestBlockDto), "openChest")]
    [JsonDerivedType(typeof(DoorBlockDto), "enterDoor")]
    [JsonDerivedType(typeof(ClickQuestMarkBlockDto), "clickQuestMark")]
    [JsonDerivedType(typeof(AcceptDialogBlockDto), "acceptDialog")]
    [JsonDerivedType(typeof(MountHorsesBlockDto), "mountHorses")]
    [JsonDerivedType(typeof(UseMapBlockDto), "useMap")]
    [JsonDerivedType(typeof(SequenceBlockDto), "playSequence")]
    private abstract class BlockDto
    {
        public int PauseAfterMilliseconds { get; set; }

        public abstract ScenarioBlock ToBlock();

        public ScenarioBlock Materialize()
        {
            ScenarioBlock block = ToBlock();
            block.PauseAfterMilliseconds = Math.Clamp(PauseAfterMilliseconds, 0, 120_000);
            return block;
        }

        public static BlockDto From(ScenarioBlock block)
        {
            BlockDto dto = block switch
            {
                FollowBlock follow => FollowBlockDto.From(follow),
                RepeatBlock repeat => RepeatBlockDto.From(repeat),
                LockBlock locked => LockBlockDto.From(locked),
                OpenChestBlock chest => OpenChestBlockDto.From(chest),
                DoorBlock door => DoorBlockDto.From(door),
                ClickQuestMarkBlock quest => ClickQuestMarkBlockDto.From(quest),
                AcceptDialogBlock dialog => AcceptDialogBlockDto.From(dialog),
                MountHorsesBlock mount => MountHorsesBlockDto.From(mount),
                UseMapBlock useMap => UseMapBlockDto.From(useMap),
                SequenceBlock sequence => SequenceBlockDto.From(sequence),
                _ => throw new InvalidDataException("Неизвестный блок сценария."),
            };
            dto.PauseAfterMilliseconds = block.PauseAfterMilliseconds;
            return dto;
        }
    }

    private sealed class FollowBlockDto : BlockDto
    {
        public TravelMode Travel { get; set; }

        public int DelayMilliseconds { get; set; }

        public int ChaseLostMilliseconds { get; set; } = 1500;

        public List<AnchorDto> Anchors { get; set; } = [];

        public string? RouteFile { get; set; }

        public string? MapFile { get; set; }

        public override ScenarioBlock ToBlock()
        {
            var block = new FollowBlock
            {
                Travel = Travel,
                DelayMilliseconds = Math.Clamp(DelayMilliseconds, 0, 120_000),
                ChaseLostMilliseconds = Math.Clamp(ChaseLostMilliseconds, 0, 120_000),
                RouteFile = string.IsNullOrWhiteSpace(RouteFile) ? null : MapLibrary.ToFullPath(RouteFile),
                MapFile = string.IsNullOrWhiteSpace(MapFile) ? null : MapLibrary.ToFullPath(MapFile),
            };
            foreach (AnchorDto anchor in Anchors)
                block.Anchors.Add(anchor.ToAnchor());
            return block;
        }

        public static FollowBlockDto From(FollowBlock block)
            => new()
            {
                Travel = block.Travel,
                DelayMilliseconds = block.DelayMilliseconds,
                ChaseLostMilliseconds = block.ChaseLostMilliseconds,
                RouteFile = string.IsNullOrWhiteSpace(block.RouteFile) ? null : MapLibrary.ToStoredPath(block.RouteFile),
                MapFile = string.IsNullOrWhiteSpace(block.MapFile) ? null : MapLibrary.ToStoredPath(block.MapFile),
                Anchors = block.Anchors.Select(AnchorDto.From).ToList(),
            };
    }

    private sealed class UseMapBlockDto : BlockDto
    {
        public string? MapFile { get; set; }

        public override ScenarioBlock ToBlock()
            => new UseMapBlock
            {
                MapFile = string.IsNullOrWhiteSpace(MapFile) ? null : MapLibrary.ToFullPath(MapFile),
            };

        public static UseMapBlockDto From(UseMapBlock block)
            => new()
            {
                MapFile = string.IsNullOrWhiteSpace(block.MapFile) ? null : MapLibrary.ToStoredPath(block.MapFile),
            };
    }

    private sealed class RepeatBlockDto : BlockDto
    {
        public RepeatCondition Condition { get; set; } = RepeatCondition.WhileRed;

        public int WaitMilliseconds { get; set; } = 8000;

        public List<BlockDto> Blocks { get; set; } = [];

        public override ScenarioBlock ToBlock()
        {
            var block = new RepeatBlock
            {
                Condition = Condition,
                WaitMilliseconds = Math.Clamp(WaitMilliseconds, 0, 120_000),
            };
            foreach (BlockDto child in Blocks)
                block.Children.Add(child.Materialize());
            return block;
        }

        public static RepeatBlockDto From(RepeatBlock block)
            => new()
            {
                Condition = block.Condition,
                WaitMilliseconds = block.WaitMilliseconds,
                Blocks = block.Children.Select(BlockDto.From).ToList(),
            };
    }

    private sealed class LockBlockDto : BlockDto
    {
        public AnchorSource Target { get; set; } = AnchorSource.Skull;

        public int RadiusPixels { get; set; } = 80;

        public int WaitMilliseconds { get; set; } = 1500;

        public List<RouteKeyDto> Keys { get; set; } = [];

        public override ScenarioBlock ToBlock()
        {
            var block = new LockBlock
            {
                Target = Target == AnchorSource.Chest ? AnchorSource.Chest : AnchorSource.Skull,
                RadiusPixels = Math.Clamp(RadiusPixels, 0, 400),
                WaitMilliseconds = Math.Clamp(WaitMilliseconds, 0, 120_000),
            };
            foreach (RouteKeyDto item in Keys)
            {
                if (TryKey(item.Key, item.IntervalMilliseconds, out RouteKey key))
                    block.Keys.Add(key);
            }

            return block;
        }

        public static LockBlockDto From(LockBlock block)
            => new()
            {
                Target = block.Target == AnchorSource.Chest ? AnchorSource.Chest : AnchorSource.Skull,
                RadiusPixels = block.RadiusPixels,
                WaitMilliseconds = block.WaitMilliseconds,
                Keys = block.Keys.Select(key => new RouteKeyDto { Key = key.Key.ToString(), IntervalMilliseconds = key.Interval }).ToList(),
            };
    }

    private sealed class OpenChestBlockDto : BlockDto
    {
        public int WaitMilliseconds { get; set; } = 1500;

        public override ScenarioBlock ToBlock()
            => new OpenChestBlock { WaitMilliseconds = Math.Clamp(WaitMilliseconds, 0, 120_000) };

        public static OpenChestBlockDto From(OpenChestBlock block)
            => new() { WaitMilliseconds = block.WaitMilliseconds };
    }

    private sealed class DoorBlockDto : BlockDto
    {
        public int Difficulty { get; set; } = 1;

        public override ScenarioBlock ToBlock()
            => new DoorBlock { Difficulty = DoorDifficulties.Clamp(Difficulty) };

        public static DoorBlockDto From(DoorBlock block)
            => new() { Difficulty = DoorDifficulties.Clamp(block.Difficulty) };
    }

    private sealed class ClickQuestMarkBlockDto : BlockDto
    {
        public AnchorSource Mark { get; set; } = AnchorSource.QuestQuestion;

        public override ScenarioBlock ToBlock()
            => new ClickQuestMarkBlock
            {
                Mark = Mark == AnchorSource.QuestExclaim ? AnchorSource.QuestExclaim : AnchorSource.QuestQuestion,
            };

        public static ClickQuestMarkBlockDto From(ClickQuestMarkBlock block)
            => new()
            {
                Mark = block.Mark == AnchorSource.QuestExclaim ? AnchorSource.QuestExclaim : AnchorSource.QuestQuestion,
            };
    }

    private sealed class AcceptDialogBlockDto : BlockDto
    {
        public override ScenarioBlock ToBlock() => new AcceptDialogBlock();

        public static AcceptDialogBlockDto From(AcceptDialogBlock block) => new();
    }

    private sealed class MountHorsesBlockDto : BlockDto
    {
        public int KeyIntervalMilliseconds { get; set; } = 1000;

        public override ScenarioBlock ToBlock()
            => new MountHorsesBlock
            {
                KeyIntervalMilliseconds = Math.Clamp(KeyIntervalMilliseconds, 200, 10_000),
            };

        public static MountHorsesBlockDto From(MountHorsesBlock block)
            => new()
            {
                KeyIntervalMilliseconds = Math.Clamp(block.KeyIntervalMilliseconds, 200, 10_000),
            };
    }

    private sealed class SequenceBlockDto : BlockDto
    {
        public List<ClickDto> Clicks { get; set; } = [];

        public override ScenarioBlock ToBlock()
        {
            var block = new SequenceBlock();
            foreach (ClickDto click in Clicks)
            {
                block.Clicks.Add(new ScreenClick
                {
                    X = click.X,
                    Y = click.Y,
                    PauseMilliseconds = Math.Clamp(click.PauseMilliseconds, 0, 120_000),
                    HoldMilliseconds = Math.Clamp(click.HoldMilliseconds, 0, 2_000),
                    Name = string.IsNullOrWhiteSpace(click.Name) ? null : click.Name.Trim(),
                });
            }

            return block;
        }

        public static SequenceBlockDto From(SequenceBlock block)
            => new()
            {
                Clicks = block.Clicks.Select(click => new ClickDto
                {
                    X = click.X,
                    Y = click.Y,
                    PauseMilliseconds = click.PauseMilliseconds,
                    HoldMilliseconds = click.HoldMilliseconds,
                    Name = click.Name,
                }).ToList(),
            };
    }

    private sealed class AnchorDto
    {
        public AnchorSource Source { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public List<RouteKeyDto>? Keys { get; set; }

        public GoalWait Wait { get; set; } = GoalWait.Timed;

        public int WaitMilliseconds { get; set; } = 2000;

        public Anchor ToAnchor()
        {
            if (Source == AnchorSource.Fixed && (!double.IsFinite(X) || !double.IsFinite(Y)))
                throw new InvalidDataException("В сценарии есть точка без координат.");

            var anchor = new Anchor
            {
                Source = Source,
                Position = new MapPoint(X, Y),
                Wait = Wait,
                WaitMilliseconds = Math.Clamp(WaitMilliseconds, 0, 120_000),
            };
            if (Keys is not null)
            {
                foreach (RouteKeyDto item in Keys)
                {
                    if (TryKey(item.Key, item.IntervalMilliseconds, out RouteKey key))
                        anchor.Keys.Add(key);
                }
            }

            return anchor;
        }

        public static AnchorDto From(Anchor anchor)
            => new()
            {
                Source = anchor.Source,
                X = anchor.Position.X,
                Y = anchor.Position.Y,
                Wait = anchor.Wait,
                WaitMilliseconds = anchor.WaitMilliseconds,
                Keys = anchor.Keys.Count == 0
                    ? null
                    : anchor.Keys.Select(key => new RouteKeyDto { Key = key.Key.ToString(), IntervalMilliseconds = key.Interval }).ToList(),
            };
    }

    private sealed class ClickDto
    {
        public int X { get; set; }

        public int Y { get; set; }

        public int PauseMilliseconds { get; set; }

        public int HoldMilliseconds { get; set; } = 40;

        public string? Name { get; set; }
    }
}
