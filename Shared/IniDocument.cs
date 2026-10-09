namespace MechaCommunityMod.Configuration;

// Shared by the launcher and plugin migrations; preserve unrelated entries/comments.
internal sealed class IniDocument
{
    private readonly List<string> _lines;
    internal IniDocument(string text) => _lines = text.Replace("\r\n", "\n").Split('\n').ToList();
    internal static IniDocument Read(string path) => new(File.Exists(path) ? File.ReadAllText(path) : "");
    private static string? Section(string line) => line.Trim() is var value && value.StartsWith('[') && value.EndsWith(']') ? value[1..^1] : null;
    private static bool Key(string line, string key) => !line.TrimStart().StartsWith('#') && !line.TrimStart().StartsWith(';')
        && line.IndexOf('=') is var index && index >= 0 && string.Equals(line[..index].Trim(), key, StringComparison.OrdinalIgnoreCase);
    internal string? Get(string section, string key)
    {
        string? current = null, result = null;
        foreach (var line in _lines)
            if (Section(line) is { } heading) current = heading;
            else if (string.Equals(current, section, StringComparison.OrdinalIgnoreCase) && Key(line, key)) result = line[(line.IndexOf('=') + 1)..].Trim();
        return result;
    }
    internal void Set(string section, string key, string? value)
    {
        string? current = null;
        var start = -1; var end = _lines.Count;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (Section(_lines[i]) is { } heading)
            {
                if (start >= 0 && end == _lines.Count && !string.Equals(heading, section, StringComparison.OrdinalIgnoreCase)) end = i;
                current = heading;
                if (string.Equals(heading, section, StringComparison.OrdinalIgnoreCase)) { start = i; end = _lines.Count; }
            }
        }
        // Remove duplicate assignments in every matching section.
        current = null;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (Section(_lines[i]) is { } heading) current = heading;
            else if (string.Equals(current, section, StringComparison.OrdinalIgnoreCase) && Key(_lines[i], key))
            {
                _lines.RemoveAt(i); if (i < start) start--; if (i < end) end--; i--;
            }
        }
        if (value is null) return;
        if (start < 0) { _lines.Add(""); _lines.Add("[" + section + "]"); end = _lines.Count; }
        _lines.Insert(end, key + " = " + value);
    }
    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".launcher.tmp";
        try { File.WriteAllText(temporary, ToString()); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal void MergeMissing(IniDocument previous)
    {
        string? section = null;
        foreach (var line in previous._lines)
        {
            if (Section(line) is { } heading) { section = heading; continue; }
            if (section is null || line.TrimStart().StartsWith('#') || line.TrimStart().StartsWith(';')) continue;
            var equals = line.IndexOf('=');
            if (equals < 0) continue;
            var key = line[..equals].Trim();
            if (Get(section, key) is null) Set(section, key, previous.Get(section, key));
        }
    }
    public override string ToString() => string.Join(Environment.NewLine, _lines);
}
