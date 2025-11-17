using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Configer.Models;

public class ConfigManager
{
    private readonly string _root;
    private readonly string _envPath;
    private readonly string _lexiconPath;
    private readonly string _subtitleDir;
    private readonly string _roastDir;

    public ConfigManager(string root)
    {
        _root = root ?? AppContext.BaseDirectory;
        _envPath = Path.Combine(_root, ".env");
        _lexiconPath = Path.Combine(_root, "data", "lexicon", "lexicon.csv");
        _subtitleDir = Path.Combine(_root, "data", "subtitle");
        _roastDir = Path.Combine(_root, "data", "roast");

        Directory.CreateDirectory(Path.GetDirectoryName(_lexiconPath) ?? Path.Combine(_root, "data", "lexicon"));
        Directory.CreateDirectory(_subtitleDir);
        Directory.CreateDirectory(_roastDir);
    }

    public async Task<Dictionary<string, string>> LoadEnvAsync()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_envPath))
        {
            return dict;
        }

        var lines = await File.ReadAllLinesAsync(_envPath);
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (string.IsNullOrEmpty(t) || t.StartsWith("#")) continue;
            var idx = t.IndexOf('=');
            if (idx <= 0) continue;
            var k = t.Substring(0, idx).Trim();
            var v = t.Substring(idx + 1).Trim();
            dict[k] = v;
        }
        return dict;
    }

    public async Task SaveEnvAsync(Dictionary<string, string> dict)
    {
        using var sw = new StreamWriter(_envPath, false);
        foreach (var kv in dict)
        {
            await sw.WriteLineAsync($"{kv.Key}={kv.Value}");
        }
    }

    public async Task<List<LexiconEntry>> LoadLexiconAsync()
    {
        var list = new List<LexiconEntry>();
        if (!File.Exists(_lexiconPath)) return list;
        var lines = await File.ReadAllLinesAsync(_lexiconPath);
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            var parts = t.Split(',');
            if (parts.Length >= 1)
            {
                var word = parts[0].Trim();
                var tag = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                list.Add(new LexiconEntry { Word = word, Tag = tag });
            }
        }
        return list;
    }

    public async Task SaveLexiconAsync(IEnumerable<LexiconEntry> list)
    {
        using var sw = new StreamWriter(_lexiconPath, false);
        foreach (var item in list)
        {
            var word = item.Word?.Replace("\r", "").Replace("\n", "") ?? string.Empty;
            var tag = item.Tag?.Replace("\r", "").Replace("\n", "") ?? string.Empty;
            await sw.WriteLineAsync($"{word},{tag}");
        }
    }

    public List<string> ListSrtFiles()
    {
        var results = new List<string>();
        if (Directory.Exists(_subtitleDir))
        {
            results.AddRange(Directory.GetFiles(_subtitleDir, "*.srt", SearchOption.TopDirectoryOnly));
        }
        return results.Distinct().ToList();
    }

    public List<SrtFileItem> ListSrtFileItems()
    {
        var items = new List<SrtFileItem>();
        if (!Directory.Exists(_subtitleDir)) return items;
        var files = Directory.GetFiles(_subtitleDir, "*.srt", SearchOption.TopDirectoryOnly);
        foreach (var f in files)
        {
            var fileName = Path.GetFileName(f);
            var roastedName = Path.GetFileNameWithoutExtension(f) + "-roasted" + Path.GetExtension(f);
            var roastedPath = Path.Combine(_roastDir, roastedName);
            var status = File.Exists(roastedPath) ? "DONE" : string.Empty;
            items.Add(new SrtFileItem { FilePath = f, FileName = fileName, Enabled = true, Status = status });
        }
        return items;
    }

    public bool HasSubtitleFiles()
    {
        if (!Directory.Exists(_subtitleDir)) return false;
        return Directory.EnumerateFiles(_subtitleDir, "*.srt", SearchOption.TopDirectoryOnly).Any();
    }
}
