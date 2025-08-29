using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Core;

public sealed class RuleSet
{
    private readonly HashSet<string> _blockedExecutablePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _storagePath;

    public RuleSet(string storagePath)
    {
        _storagePath = storagePath;
    }

    public bool IsBlocked(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        return _blockedExecutablePaths.Contains(Normalize(executablePath));
    }

    public bool AddBlock(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        return _blockedExecutablePaths.Add(Normalize(executablePath));
    }

    public bool RemoveBlock(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        return _blockedExecutablePaths.Remove(Normalize(executablePath));
    }

    public IReadOnlyCollection<string> GetBlockedPaths()
    {
        return _blockedExecutablePaths;
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_storagePath)) return;
            var json = File.ReadAllText(_storagePath);
            var model = JsonSerializer.Deserialize<RuleModel>(json) ?? new RuleModel();
            _blockedExecutablePaths.Clear();
            foreach (var p in model.BlockedPaths)
            {
                if (!string.IsNullOrWhiteSpace(p)) _blockedExecutablePaths.Add(Normalize(p));
            }
        }
        catch
        {
            // ignore load errors
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var model = new RuleModel { BlockedPaths = new List<string>(_blockedExecutablePaths) };
            var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storagePath, json);
        }
        catch
        {
            // ignore save errors
        }
    }

    private static string Normalize(string path)
    {
        return Path.GetFullPath(path).Trim();
    }

    private sealed class RuleModel
    {
        public List<string> BlockedPaths { get; set; } = new();
    }
}


