using System.Text.Json;

namespace WalkerRouteBuilder;

/// <summary>Один круг в сохранённом запуске.</summary>
internal sealed class RunLapEntry
{
    public TimeSpan Duration { get; set; }

    public int ChestsOpened { get; set; }

    public int RedsKilled { get; set; }

    public int Skipped { get; set; }

    public bool Incomplete { get; set; }
}

/// <summary>Запись одного прогона сценария или блока.</summary>
internal sealed class RunHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? EndedAt { get; set; }

    public string ScenarioName { get; set; } = "";

    public string Scope { get; set; } = "Сценарий";

    public bool Looping { get; set; }

    public bool Cancelled { get; set; }

    public bool InProgress { get; set; }

    public TimeSpan Duration { get; set; }

    public List<RunLapEntry> Laps { get; set; } = [];

    public int ChestsOpened => Laps.Sum(lap => lap.ChestsOpened);

    public int RedsKilled => Laps.Sum(lap => lap.RedsKilled);

    public int Skipped => Laps.Sum(lap => lap.Skipped);

    /// <summary>Среднее время завершённых кругов; null, если считать не из чего.</summary>
    public TimeSpan? AverageLapDuration
    {
        get
        {
            List<RunLapEntry> done = Laps
                .Where(lap => !lap.Incomplete && lap.Duration > TimeSpan.Zero)
                .ToList();
            if (done.Count == 0)
                return null;
            return TimeSpan.FromTicks((long)done.Average(lap => lap.Duration.Ticks));
        }
    }

    public string Caption
    {
        get
        {
            string when = StartedAt.ToLocalTime().ToString("dd.MM HH:mm");
            string name = string.IsNullOrWhiteSpace(ScenarioName) ? "без имени" : ScenarioName;
            string state = InProgress ? "идёт" : Cancelled ? "стоп" : "готово";
            string average = AverageLapDuration is TimeSpan avg
                ? $" · ср. {FormatShort(avg)}"
                : "";
            return $"{when} · {name} · {Scope} · {Laps.Count} кр.{average} · сундуки {ChestsOpened} · красные {RedsKilled} · пропуск {Skipped} · {state}";
        }
    }

    private static string FormatShort(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    public override string ToString() => Caption;
}

/// <summary>История прогонов, сохраняется рядом с библиотекой карт.</summary>
internal sealed class RunHistoryStore
{
    private const int MaxEntries = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public List<RunHistoryEntry> Entries { get; } = [];

    public static string FilePath => Path.Combine(MapLibrary.Folder, "run-history.json");

    public static RunHistoryStore Load()
    {
        var store = new RunHistoryStore();
        try
        {
            if (!File.Exists(FilePath))
                return store;

            HistoryDto? dto = JsonSerializer.Deserialize<HistoryDto>(File.ReadAllText(FilePath), JsonOptions);
            if (dto?.Runs is null)
                return store;

            foreach (RunDto run in dto.Runs)
            {
                if (run is null)
                    continue;
                RunHistoryEntry entry = FromDto(run);
                if (run.InProgress)
                {
                    entry.Cancelled = true;
                    entry.EndedAt ??= DateTimeOffset.Now;
                }

                store.Entries.Add(entry);
            }
        }
        catch
        {
            // Пустая история лучше, чем падение при старте.
        }

        return store;
    }

    public void Save()
    {
        Directory.CreateDirectory(MapLibrary.Folder);
        var dto = new HistoryDto
        {
            Runs = Entries.Take(MaxEntries).Select(ToDto).ToList(),
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(dto, JsonOptions));
    }

    public RunHistoryEntry Begin(string scenarioName, string scope, bool looping)
    {
        foreach (RunHistoryEntry open in Entries.Where(item => item.InProgress))
        {
            open.InProgress = false;
            open.Cancelled = true;
            open.EndedAt ??= DateTimeOffset.Now;
        }

        var entry = new RunHistoryEntry
        {
            ScenarioName = scenarioName,
            Scope = scope,
            Looping = looping,
            InProgress = true,
            StartedAt = DateTimeOffset.Now,
        };
        Entries.Insert(0, entry);
        Trim();
        SaveQuiet();
        return entry;
    }

    public void ApplyProgress(RunHistoryEntry entry, RunProgress progress)
    {
        int before = entry.Laps.Count;
        entry.Duration = progress.TotalElapsed;
        entry.Looping = progress.Looping;
        entry.Laps = BuildLaps(progress, incomplete: true);
        if (entry.Laps.Count != before)
            SaveQuiet();
    }

    public void Finish(RunHistoryEntry entry, RunProgress? progress, bool cancelled)
    {
        entry.InProgress = false;
        entry.Cancelled = cancelled;
        entry.EndedAt = DateTimeOffset.Now;
        if (progress is RunProgress snapshot)
        {
            entry.Duration = snapshot.TotalElapsed;
            entry.Looping = snapshot.Looping;
            entry.Laps = BuildLaps(snapshot, incomplete: cancelled);
        }
        else if (entry.EndedAt is DateTimeOffset ended)
            entry.Duration = ended - entry.StartedAt;

        Trim();
        SaveQuiet();
    }

    public void Clear()
    {
        Entries.Clear();
        SaveQuiet();
    }

    public void Remove(RunHistoryEntry entry)
    {
        Entries.Remove(entry);
        SaveQuiet();
    }

    private void Trim()
    {
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(Entries.Count - 1);
    }

    private void SaveQuiet()
    {
        try
        {
            Save();
        }
        catch
        {
            // История — удобство; сбой записи не должен мешать прогону.
        }
    }

    private static List<RunLapEntry> BuildLaps(RunProgress progress, bool incomplete)
    {
        var laps = new List<RunLapEntry>(progress.Laps.Count + 1);
        foreach (LapRecord lap in progress.Laps)
        {
            laps.Add(new RunLapEntry
            {
                Duration = lap.Duration,
                ChestsOpened = lap.ChestsOpened,
                RedsKilled = lap.RedsKilled,
                Skipped = lap.Skipped,
            });
        }

        bool hasOpenLap = progress.Lap > progress.Laps.Count;
        bool hasCounts = progress.ChestsOpened + progress.RedsKilled + progress.Skipped > 0;
        if (incomplete && hasOpenLap && (hasCounts || progress.LapElapsed > TimeSpan.FromSeconds(1)))
        {
            laps.Add(new RunLapEntry
            {
                Duration = progress.LapElapsed,
                ChestsOpened = progress.ChestsOpened,
                RedsKilled = progress.RedsKilled,
                Skipped = progress.Skipped,
                Incomplete = true,
            });
        }

        return laps;
    }

    private static RunHistoryEntry FromDto(RunDto dto)
        => new()
        {
            Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
            StartedAt = dto.StartedAt == default ? DateTimeOffset.Now : dto.StartedAt,
            EndedAt = dto.EndedAt,
            ScenarioName = dto.ScenarioName ?? "",
            Scope = string.IsNullOrWhiteSpace(dto.Scope) ? "Сценарий" : dto.Scope,
            Looping = dto.Looping,
            Cancelled = dto.Cancelled,
            InProgress = false,
            Duration = dto.Duration,
            Laps = (dto.Laps ?? []).Select(lap => new RunLapEntry
            {
                Duration = lap.Duration,
                ChestsOpened = lap.ChestsOpened,
                RedsKilled = lap.RedsKilled,
                Skipped = lap.Skipped,
                Incomplete = lap.Incomplete,
            }).ToList(),
        };

    private static RunDto ToDto(RunHistoryEntry entry)
        => new()
        {
            Id = entry.Id,
            StartedAt = entry.StartedAt,
            EndedAt = entry.EndedAt,
            ScenarioName = entry.ScenarioName,
            Scope = entry.Scope,
            Looping = entry.Looping,
            Cancelled = entry.Cancelled || entry.InProgress,
            InProgress = entry.InProgress,
            Duration = entry.Duration,
            Laps = entry.Laps.Select(lap => new LapDto
            {
                Duration = lap.Duration,
                ChestsOpened = lap.ChestsOpened,
                RedsKilled = lap.RedsKilled,
                Skipped = lap.Skipped,
                Incomplete = lap.Incomplete,
            }).ToList(),
        };

    private sealed class HistoryDto
    {
        public List<RunDto>? Runs { get; set; }
    }

    private sealed class RunDto
    {
        public Guid Id { get; set; }

        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset? EndedAt { get; set; }

        public string? ScenarioName { get; set; }

        public string? Scope { get; set; }

        public bool Looping { get; set; }

        public bool Cancelled { get; set; }

        public bool InProgress { get; set; }

        public TimeSpan Duration { get; set; }

        public List<LapDto>? Laps { get; set; }
    }

    private sealed class LapDto
    {
        public TimeSpan Duration { get; set; }

        public int ChestsOpened { get; set; }

        public int RedsKilled { get; set; }

        public int Skipped { get; set; }

        public bool Incomplete { get; set; }
    }
}
