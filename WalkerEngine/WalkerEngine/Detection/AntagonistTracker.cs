namespace WalkerEngine;

/// <summary>
/// Временной фильтр красных: цель принимается только после нескольких подряд кадров.
/// Отсев bleed-through делает <see cref="AntagonistFinder"/> (форма, маска карты, цвет);
/// здесь только устойчивость к мерцанию, без эвристик «далеко/стою», которые
/// ломали и ожидание, и погоню к реальному черепу.
/// </summary>
public sealed class AntagonistTracker
{
    public const int ConfirmHits = 3;

    public const double MatchPixels = 20;

    private readonly List<Track> _tracks = [];

    public void Reset()
        => _tracks.Clear();

    public List<AntagonistMark> Update(MapScan scan, IReadOnlyList<AntagonistMark> observed)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(observed);
        if (!scan.HasMap)
        {
            Reset();
            return [];
        }

        foreach (Track track in _tracks)
            track.Seen = false;

        var used = new bool[observed.Count];
        foreach (Track track in _tracks)
        {
            int best = -1;
            double bestDistance = MatchPixels;
            for (int i = 0; i < observed.Count; i++)
            {
                if (used[i])
                    continue;
                (double mx, double my) = scan.Bounds.ToPixel(observed[i].Point);
                double distance = Hypot(mx - track.X, my - track.Y);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                best = i;
            }

            if (best < 0)
                continue;

            used[best] = true;
            track.Seen = true;
            (double nx, double ny) = scan.Bounds.ToPixel(observed[best].Point);
            track.Observe(nx, ny, observed[best]);
        }

        for (int i = 0; i < observed.Count; i++)
        {
            if (used[i])
                continue;
            (double nx, double ny) = scan.Bounds.ToPixel(observed[i].Point);
            var track = new Track();
            track.Observe(nx, ny, observed[i]);
            track.Seen = true;
            _tracks.Add(track);
        }

        _tracks.RemoveAll(static track => !track.Seen);

        var confirmed = new List<AntagonistMark>();
        foreach (Track track in _tracks)
        {
            if (track.Hits < ConfirmHits)
                continue;
            confirmed.Add(track.Mark);
        }

        confirmed.Sort(static (a, b) => b.Pixels.CompareTo(a.Pixels));
        return confirmed;
    }

    private static double Hypot(double x, double y)
        => Math.Sqrt(x * x + y * y);

    private sealed class Track
    {
        public double X { get; private set; }

        public double Y { get; private set; }

        public AntagonistMark Mark { get; private set; }

        public int Hits { get; private set; }

        public bool Seen { get; set; }

        public void Observe(double x, double y, AntagonistMark mark)
        {
            X = x;
            Y = y;
            Mark = mark;
            Hits++;
        }
    }
}
