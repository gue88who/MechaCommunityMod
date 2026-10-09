namespace MechaCommunityMod.Plugins.BattleStatistics;

internal sealed record BoardRect(int X, int Y, int Width, int Height);
internal sealed record BoardPiece(string Id, UnitKey Unit, bool Right, bool Tower, BoardRect[] Cells);
internal readonly record struct BoardEdge(int X, int Y, int Side);
internal readonly record struct BoardMapButton(int Action, double X, double Y, double Width, double Height, bool Enabled = true)
{
    internal bool Contains(double x, double y) => Enabled && x >= X && y >= Y && x < X + Width && y < Y + Height;
}
internal sealed class BoardMapInput
{
    private int? _pressed;
    internal bool Captured => _pressed is not null;
    private static int? Hit(IEnumerable<BoardMapButton> buttons, double x, double y) =>
        buttons.Where(b => b.Contains(x, y)).Select(b => (int?)b.Action).FirstOrDefault();
    internal void Press(IEnumerable<BoardMapButton> buttons, double x, double y) => _pressed = Hit(buttons, x, y);
    internal int? Release(IEnumerable<BoardMapButton> buttons, double x, double y)
    {
        var hit = Hit(buttons, x, y); var pressed = _pressed; _pressed = null;
        return hit == pressed ? hit : null;
    }
    internal void Cancel() => _pressed = null;
}
internal static class BoardOutline
{
    internal static BoardEdge[] Edges(BoardPiece piece)
    {
        var occupied = new HashSet<(int X, int Y)>();
        foreach (var rect in piece.Cells)
            for (var y = rect.Y; y < rect.Y + rect.Height; y++)
                for (var x = rect.X; x < rect.X + rect.Width; x++) occupied.Add((x, y));
        var edges = new List<BoardEdge>();
        foreach (var (x, y) in occupied)
        {
            if (!occupied.Contains((x - 1, y))) edges.Add(new(x, y, 0));
            if (!occupied.Contains((x + 1, y))) edges.Add(new(x, y, 1));
            if (!occupied.Contains((x, y - 1))) edges.Add(new(x, y, 2));
            if (!occupied.Contains((x, y + 1))) edges.Add(new(x, y, 3));
        }
        return edges.ToArray();
    }
}
internal sealed record BoardSnapshot(int Round, BoardRect[] Regions, BoardPiece[] Pieces)
{
    internal static BoardRect DisplayRect(BoardRect rect, BoardRect bounds, bool rotate) => new(
        rotate ? bounds.X + bounds.Width - rect.X - rect.Width : rect.X - bounds.X,
        rotate ? rect.Y - bounds.Y : bounds.Y + bounds.Height - rect.Y - rect.Height,
        rect.Width, rect.Height);

    internal bool FlipVertically()
    {
        double? Center(bool right)
        {
            var team = Pieces.Where(p => p.Right == right).ToArray();
            var anchors = team.Any(p => p.Tower) ? team.Where(p => p.Tower) : team;
            var cells = anchors.SelectMany(p => p.Cells).ToArray();
            return cells.Length == 0 ? null : cells.Average(c => c.Y + c.Height / 2d);
        }
        return Center(false) is { } blue && Center(true) is { } red && blue > red;
    }
    internal BoardRect Bounds()
    {
        var rects = Regions.Concat(Pieces.SelectMany(p => p.Cells)).Where(r => r.Width > 0 && r.Height > 0).ToArray();
        if (rects.Length == 0) return new(0, 0, 0, 0);
        var x = rects.Min(r => r.X); var y = rects.Min(r => r.Y);
        return new(x, y, rects.Max(r => r.X + r.Width) - x, rects.Max(r => r.Y + r.Height) - y);
    }

    internal BoardPiece[] Select(StatsTableRow row)
    {
        var ids = row.MapIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count > 0) return Pieces.Where(p => ids.Contains(p.Id)).ToArray();
        if (row.Id.StartsWith("type:", StringComparison.Ordinal))
            return Pieces.Where(p => p.Right == row.Right && p.Unit == row.Unit).ToArray();
        return Pieces.Where(p => p.Id == row.Id).ToArray();
    }
}

internal sealed class BoardHistory
{
    private readonly SortedDictionary<int, BoardSnapshot> _rounds = new();
    internal void Clear() => _rounds.Clear();
    internal void Resume(int completed)
    { foreach (var round in _rounds.Keys.Where(r => r > completed).ToArray()) _rounds.Remove(round); }
    internal void Put(BoardSnapshot board) => _rounds[board.Round] = board;
    internal BoardSnapshot? Get(int round) => round == 0 ? _rounds.Values.LastOrDefault() : _rounds.GetValueOrDefault(round);
    internal BoardSnapshot? Find(StatsTableRow row, int round) => _rounds.Values
        .Where(b => (round == 0 || b.Round <= round) && b.Select(row).Length > 0).LastOrDefault();
    internal BoardSnapshot[] All() => _rounds.Values.ToArray();
}
