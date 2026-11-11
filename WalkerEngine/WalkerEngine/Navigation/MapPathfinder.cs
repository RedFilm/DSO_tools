namespace WalkerEngine;

/// <summary>
/// Кратчайший путь по полу миникарты. Пол — тёмно-серый. Чёрный фон, светлый контур стен
/// и цветные значки непроходимы: значок закрывает то, что под ним нарисовано.
/// Точка персонажа тоже нарисована поверх пола. В узком проходе она не стена:
/// иначе, коснувшись обеих границ, она запирает коридор.
/// Запас от стены держит путь в центре, но узкий коридор не запечатывает.
/// </summary>
public static class MapPathfinder
{
    public const int WallClearance = 6;

    private const int SnapRadius = 30;
    private const int ClearancePenalty = 18;
    private const int FloorCeiling = 110;
    private const int FloorSaturation = 28;
    private const int MarkerTouch = 4;

    public static bool TryFind(
        MapScan scan,
        ColorImage image,
        MapPoint from,
        MapPoint to,
        out List<MapPoint> path,
        int clearance = WallClearance,
        IReadOnlyList<MapPoint>? blocked = null,
        int blockRadius = 0,
        int standRadius = 0)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(image);
        path = [];
        if (!scan.HasMap || image.Width != scan.Width || image.Height != scan.Height)
            return false;

        clearance = Math.Clamp(clearance, 0, 40);
        byte[] floor = BuildFloor(scan, image);
        Block(floor, scan, blocked, blockRadius);
        Stamp(floor, scan, to, standRadius);
        byte[] room = Measure(floor, scan.Width, scan.Height);

        var (fromX, fromY) = scan.Bounds.ToPixel(from);
        var (toX, toY) = scan.Bounds.ToPixel(to);
        int originX = (int)Math.Round(fromX);
        int originY = (int)Math.Round(fromY);
        if (!TrySnap(room, scan.Width, scan.Height, originX, originY, null, out int startX, out int startY))
            return false;
        bool[] reachable = Reachable(room, scan.Width, scan.Height, startX, startY);
        if (!TrySnap(room, scan.Width, scan.Height, (int)Math.Round(toX), (int)Math.Round(toY), reachable, out int goalX, out int goalY))
            return false;

        if (startX == goalX && startY == goalY)
        {
            path.Add(from);
            path.Add(to);
            return true;
        }

        List<(int X, int Y)>? pixels = Search(room, scan.Width, scan.Height, startX, startY, goalX, goalY, clearance);
        if (pixels is null || pixels.Count < 2)
            return false;

        if (pixels[0].X != originX || pixels[0].Y != originY)
            pixels.Insert(0, (originX, originY));

        var simplified = Simplify(image, room, scan.Width, pixels, clearance);
        foreach (var (x, y) in simplified)
            path.Add(scan.Bounds.ToNormalized(x, y));
        if (path.Count < 2)
            return false;

        path[0] = from;
        if (Math.Abs(goalX - (int)Math.Round(toX)) <= 1 && Math.Abs(goalY - (int)Math.Round(toY)) <= 1)
            path[^1] = to;
        return true;
    }

    private static byte[] BuildFloor(MapScan scan, ColorImage image)
    {
        byte[] pixels = image.Pixels;
        var floor = new byte[scan.Width * scan.Height];
        var player = ColorProfile.GreenMarker;
        for (int i = 0; i < floor.Length; i++)
        {
            int x = i % scan.Width;
            int y = i / scan.Width;
            if (!scan.ContainsPixel(x, y))
                continue;

            int pixel = i * 4;
            byte r = pixels[pixel];
            byte g = pixels[pixel + 1];
            byte b = pixels[pixel + 2];
            if (IsFloor(r, g, b) || IsCharacter(player, scan, x, y, r, g, b))
                floor[i] = 1;
        }

        return floor;
    }

    /// <summary>Значок цели закрывает пол. Диск вокруг неё снова проходимый, чтобы встать на сам значок.</summary>
    private static void Stamp(byte[] floor, MapScan scan, MapPoint goal, int radius)
    {
        if (radius <= 0)
            return;

        int width = scan.Width;
        int height = scan.Height;
        int limit = radius * radius;
        var (pixelX, pixelY) = scan.Bounds.ToPixel(goal);
        int centerX = (int)Math.Round(pixelX);
        int centerY = (int)Math.Round(pixelY);
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (!Inside(width, height, x, y) || !scan.ContainsPixel(x, y))
                    continue;
                int dx = x - centerX;
                int dy = y - centerY;
                if (dx * dx + dy * dy <= limit)
                    floor[y * width + x] = 1;
            }
        }
    }

    /// <summary>Место застревания вырезается из пола, чтобы следующий путь обошёл его.</summary>
    private static void Block(byte[] floor, MapScan scan, IReadOnlyList<MapPoint>? blocked, int radius)
    {
        if (blocked is null || blocked.Count == 0 || radius <= 0)
            return;

        int width = scan.Width;
        int height = scan.Height;
        int limit = radius * radius;
        foreach (MapPoint point in blocked)
        {
            var (pixelX, pixelY) = scan.Bounds.ToPixel(point);
            int centerX = (int)Math.Round(pixelX);
            int centerY = (int)Math.Round(pixelY);
            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (!Inside(width, height, x, y))
                        continue;
                    int dx = x - centerX;
                    int dy = y - centerY;
                    if (dx * dx + dy * dy <= limit)
                        floor[y * width + x] = 0;
                }
            }
        }
    }

    private static bool IsCharacter(ColorProfile player, MapScan scan, int x, int y, byte r, byte g, byte b)
    {
        if (!scan.PlayerFound || !player.IsPlayer(r, g, b))
            return false;

        double dx = x - scan.PlayerX;
        double dy = y - scan.PlayerY;
        return dx * dx + dy * dy <= 16 * 16;
    }

    private static bool IsFloor(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        return max - min <= FloorSaturation && max > 21 && max <= FloorCeiling;
    }

    /// <summary>Расстояние Чебышёва до ближайшего не-пола. 0 — сам не пол. Край кадра тоже стена.</summary>
    private static byte[] Measure(byte[] floor, int width, int height)
    {
        var room = new byte[floor.Length];
        for (int i = 0; i < floor.Length; i++)
            room[i] = floor[i] == 0 ? (byte)0 : byte.MaxValue;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                Relax(room, width, height, x, y, forward: true);
        }

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = width - 1; x >= 0; x--)
                Relax(room, width, height, x, y, forward: false);
        }

        return room;
    }

    private static void Relax(byte[] room, int width, int height, int x, int y, bool forward)
    {
        int index = y * width + x;
        if (room[index] == 0)
            return;

        int best = room[index];
        if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
            best = Math.Min(best, 1);

        if (forward)
        {
            if (x > 0)
                best = Math.Min(best, Step(room[index - 1]));
            if (y > 0)
                best = Math.Min(best, Step(room[index - width]));
            if (x > 0 && y > 0)
                best = Math.Min(best, Step(room[index - width - 1]));
            if (x + 1 < width && y > 0)
                best = Math.Min(best, Step(room[index - width + 1]));
        }
        else
        {
            if (x + 1 < width)
                best = Math.Min(best, Step(room[index + 1]));
            if (y + 1 < height)
                best = Math.Min(best, Step(room[index + width]));
            if (x + 1 < width && y + 1 < height)
                best = Math.Min(best, Step(room[index + width + 1]));
            if (x > 0 && y + 1 < height)
                best = Math.Min(best, Step(room[index + width - 1]));
        }

        room[index] = (byte)Math.Min(best, 255);
    }

    private static int Step(byte neighbor) => neighbor >= byte.MaxValue ? byte.MaxValue : neighbor + 1;

    private static bool[] Reachable(byte[] room, int width, int height, int startX, int startY)
    {
        var reach = new bool[room.Length];
        var stack = new Stack<int>();
        int start = startY * width + startX;
        reach[start] = true;
        stack.Push(start);
        while (stack.Count > 0)
        {
            int current = stack.Pop();
            int x = current % width;
            int y = current / width;
            foreach (var (dx, dy) in Cardinal)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (!Inside(width, height, nx, ny))
                    continue;
                int neighbor = ny * width + nx;
                if (reach[neighbor] || room[neighbor] == 0)
                    continue;
                reach[neighbor] = true;
                stack.Push(neighbor);
            }
        }

        return reach;
    }

    private static bool TrySnap(byte[] room, int width, int height, int x, int y, bool[]? reachable, out int snappedX, out int snappedY)
    {
        snappedX = x;
        snappedY = y;
        if (Inside(width, height, x, y) && room[y * width + x] != 0 && (reachable is null || reachable[y * width + x]))
            return true;

        var queue = new Queue<(int X, int Y, int Distance)>();
        var seen = new bool[width * height];
        if (Inside(width, height, x, y))
            seen[y * width + x] = true;
        queue.Enqueue((x, y, 0));
        while (queue.Count > 0)
        {
            var (cx, cy, distance) = queue.Dequeue();
            if (distance > SnapRadius)
                continue;
            if (distance > 0 && room[cy * width + cx] != 0 && (reachable is null || reachable[cy * width + cx]))
            {
                snappedX = cx;
                snappedY = cy;
                return true;
            }

            foreach (var (dx, dy) in Cardinal)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if (!Inside(width, height, nx, ny))
                    continue;
                int neighbor = ny * width + nx;
                if (seen[neighbor])
                    continue;
                seen[neighbor] = true;
                queue.Enqueue((nx, ny, distance + 1));
            }
        }

        return false;
    }

    private static List<(int X, int Y)>? Search(
        byte[] room,
        int width,
        int height,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int clearance)
    {
        int count = width * height;
        var gScore = new int[count];
        Array.Fill(gScore, int.MaxValue);
        var cameFrom = new int[count];
        Array.Fill(cameFrom, -1);
        var closed = new bool[count];
        int start = startY * width + startX;
        int goal = goalY * width + goalX;
        gScore[start] = 0;
        cameFrom[start] = start;

        var frontier = new PriorityQueue<int, int>();
        frontier.Enqueue(start, Heuristic(startX, startY, goalX, goalY));

        while (frontier.Count > 0)
        {
            int current = frontier.Dequeue();
            if (closed[current])
                continue;
            if (current == goal)
                return Rebuild(cameFrom, width, start, goal);

            closed[current] = true;
            int x = current % width;
            int y = current / width;
            foreach (var (dx, dy, cost) in Steps)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (!Inside(width, height, nx, ny))
                    continue;
                int neighbor = ny * width + nx;
                if (room[neighbor] == 0 || closed[neighbor])
                    continue;
                if (dx != 0 && dy != 0 && (room[y * width + (x + dx)] == 0 || room[(y + dy) * width + x] == 0))
                    continue;

                int space = room[neighbor] == byte.MaxValue ? clearance : room[neighbor];
                int penalty = space >= clearance ? 0 : (clearance - space) * ClearancePenalty;
                int tentative = gScore[current] + cost + penalty;
                if (tentative >= gScore[neighbor])
                    continue;

                gScore[neighbor] = tentative;
                cameFrom[neighbor] = current;
                frontier.Enqueue(neighbor, tentative + Heuristic(nx, ny, goalX, goalY));
            }
        }

        return null;
    }

    private static List<(int X, int Y)> Rebuild(int[] cameFrom, int width, int start, int goal)
    {
        var reverse = new List<(int X, int Y)>();
        int current = goal;
        while (current != start)
        {
            reverse.Add((current % width, current / width));
            current = cameFrom[current];
            if (current < 0)
                break;
        }

        reverse.Add((start % width, start / width));
        reverse.Reverse();
        return reverse;
    }

    private static List<(int X, int Y)> Simplify(
        ColorImage image,
        byte[] room,
        int width,
        List<(int X, int Y)> pixels,
        int clearance)
    {
        var result = new List<(int X, int Y)> { pixels[0] };
        int anchor = 0;
        var tripStart = pixels[0];
        var tripGoal = pixels[^1];
        for (int i = 2; i < pixels.Count; i++)
        {
            if (Clear(image, room, width, pixels[anchor], pixels[i], tripStart, tripGoal, clearance))
                continue;
            result.Add(pixels[i - 1]);
            anchor = i - 1;
        }

        result.Add(pixels[^1]);
        return result;
    }

    private static bool Clear(
        ColorImage image,
        byte[] room,
        int width,
        (int X, int Y) from,
        (int X, int Y) to,
        (int X, int Y) tripStart,
        (int X, int Y) tripGoal,
        int clearance)
    {
        int x0 = from.X;
        int y0 = from.Y;
        int x1 = to.X;
        int y1 = to.Y;
        int demand = Math.Min(clearance, Tightest(room, width, clearance, x0, y0, x1, y1));
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int stepX = x0 < x1 ? 1 : -1;
        int stepY = y0 < y1 ? 1 : -1;
        int error = dx - dy;
        int x = x0;
        int y = y0;
        while (true)
        {
            if (!Fits(image, room, width, x, y, tripStart, tripGoal, demand, x0, y0, x1, y1))
                return false;
            if (x == x1 && y == y1)
                return true;

            int doubled = error * 2;
            int nextX = x;
            int nextY = y;
            if (doubled > -dy)
            {
                error -= dy;
                nextX += stepX;
            }

            if (doubled < dx)
            {
                error += dx;
                nextY += stepY;
            }

            if (nextX != x && nextY != y
                && (!Passable(image, room, width, nextX, y, tripStart)
                    || !Passable(image, room, width, x, nextY, tripStart)))
                return false;

            x = nextX;
            y = nextY;
        }
    }

    private static bool Fits(
        ColorImage image,
        byte[] room,
        int width,
        int x,
        int y,
        (int X, int Y) tripStart,
        (int X, int Y) tripGoal,
        int demand,
        int x0,
        int y0,
        int x1,
        int y1)
    {
        if (!Passable(image, room, width, x, y, tripStart))
            return false;

        bool endpoint = (x == x0 && y == y0) || (x == x1 && y == y1);
        bool atTripEnd = Math.Max(Math.Abs(x - tripStart.X), Math.Abs(y - tripStart.Y)) <= 1
            || Math.Max(Math.Abs(x - tripGoal.X), Math.Abs(y - tripGoal.Y)) <= 1;
        if (endpoint || atTripEnd || CoveredByMarker(image, x, y, tripStart.X, tripStart.Y))
            return true;

        return Space(room, width, demand, x, y) >= demand;
    }

    private static bool Passable(ColorImage image, byte[] room, int width, int x, int y, (int X, int Y) tripStart)
    {
        if (!Inside(width, room.Length / width, x, y))
            return false;
        if (room[y * width + x] != 0)
            return true;
        return CoveredByMarker(image, x, y, tripStart.X, tripStart.Y);
    }

    /// <summary>
    /// Зелёная точка персонажа закрывает пол. Рядом со стартом её пиксели можно пересечь,
    /// светлый контур стены — нет.
    /// </summary>
    private static bool CoveredByMarker(ColorImage image, int x, int y, int startX, int startY)
    {
        if (Math.Max(Math.Abs(x - startX), Math.Abs(y - startY)) > MarkerTouch)
            return false;

        Rgba32 color = image[x, y];
        int max = Math.Max(color.R, Math.Max(color.G, color.B));
        int min = Math.Min(color.R, Math.Min(color.G, color.B));
        bool wall = max - min <= FloorSaturation && max > FloorCeiling;
        return max > 21 && !wall;
    }

    /// <summary>
    /// Прямая не должна подходить к стене ближе, чем уже подошёл более тесный конец.
    /// Ноль у конца — это маркер персонажа, а не пол.
    /// </summary>
    private static int Tightest(byte[] room, int width, int clearance, int x0, int y0, int x1, int y1)
    {
        int a = Space(room, width, clearance, x0, y0);
        int b = Space(room, width, clearance, x1, y1);
        if (a == 0)
            return b;
        if (b == 0)
            return a;
        return Math.Min(a, b);
    }

    private static int Space(byte[] room, int width, int clearance, int x, int y)
    {
        if (!Inside(width, room.Length / width, x, y))
            return 0;
        int space = room[y * width + x];
        if (space == 0)
            return 0;
        return space == byte.MaxValue ? clearance : space;
    }

    private static int Heuristic(int x, int y, int goalX, int goalY)
    {
        int dx = Math.Abs(x - goalX);
        int dy = Math.Abs(y - goalY);
        return 10 * (dx + dy) - 6 * Math.Min(dx, dy);
    }

    private static bool Inside(int width, int height, int x, int y)
        => (uint)x < (uint)width && (uint)y < (uint)height;

    private static readonly (int X, int Y)[] Cardinal = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private static readonly (int X, int Y, int Cost)[] Steps =
    [
        (1, 0, 10), (-1, 0, 10), (0, 1, 10), (0, -1, 10),
        (1, 1, 14), (1, -1, 14), (-1, 1, 14), (-1, -1, 14),
    ];
}
