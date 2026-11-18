using System;
using System.Collections.Generic;
using System.IO;
using Production.Saves;

/// <summary>A disk in memory that can be told to fail, so the lab can break a write at any step and look at what is left.</summary>
sealed class FakeFiles : ISaveFiles
{
    public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
    /// <summary>Called with (operation, path) before every operation; throw to make it fail.</summary>
    public Action<string, string> Fail;
    public readonly List<string> Operations = new List<string>();

    void Before(string op, string path) { Operations.Add(op + " " + path); Fail?.Invoke(op, path); }

    public bool Exists(string path) => Data.ContainsKey(path);
    public string ReadAllText(string path) { Before("read", path); if (!Data.TryGetValue(path, out var text)) throw new FileNotFoundException(path); return text; }
    public void WriteAllText(string path, string text) { Before("write", path); Data[path] = text; }
    public void Move(string from, string to) { Before("move", to); if (!Data.TryGetValue(from, out var text)) throw new FileNotFoundException(from); Data.Remove(from); Data[to] = text; }
    public void Replace(string source, string destination) { Before("replace", destination); if (!Data.TryGetValue(source, out var text)) throw new FileNotFoundException(source); Data.Remove(source); Data[destination] = text; }
    public void Copy(string from, string to) { Before("copy", to); if (!Data.TryGetValue(from, out var text)) throw new FileNotFoundException(from); Data[to] = text; }
    public void Delete(string path) { Before("delete", path); Data.Remove(path); }
    public void CreateDirectory(string path) { }
    public IReadOnlyList<string> Files(string folder)
    {
        var list = new List<string>();
        foreach (string key in Data.Keys) if (string.Equals(Path.GetDirectoryName(key), folder, StringComparison.Ordinal)) list.Add(key);
        return list;
    }

    public static string P(params string[] parts) => Path.Combine(parts);
}
