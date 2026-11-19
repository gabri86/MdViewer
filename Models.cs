using System.Text.Json;

namespace MdViewer;

public class Project
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? LastFile { get; set; }
    public int LastLine { get; set; }
    public double LastDelta { get; set; }
    public override string ToString() => Name;
}

public class AppState
{
    public List<Project> Projects { get; set; } = new();
    public string? LastProjectPath { get; set; }
    public bool Dark { get; set; } = true;
    public double TreeWidth { get; set; } = 260;
}

public static class StateStore
{
    static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MdViewer", "state.json");

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath)) ?? new AppState();
        }
        catch { }
        return new AppState();
    }

    public static void Save(AppState state)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, Options));
            File.Move(tmp, FilePath, true);
        }
        catch { }
    }
}

public class FileNode
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsFile { get; init; }
    public List<FileNode> Children { get; } = new();

    public static FileNode? Build(string dir, bool root = true)
    {
        var node = new FileNode { Name = root ? System.IO.Path.GetFileName(dir.TrimEnd('\\', '/')) : System.IO.Path.GetFileName(dir), FullPath = dir };
        try
        {
            foreach (var d in Directory.EnumerateDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var name = System.IO.Path.GetFileName(d);
                if (name.StartsWith('.') || name is "node_modules" or "bin" or "obj") continue;
                var child = Build(d, false);
                if (child != null) node.Children.Add(child);
            }
            foreach (var f in Directory.EnumerateFiles(dir)
                         .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                node.Children.Add(new FileNode { Name = System.IO.Path.GetFileName(f), FullPath = f, IsFile = true });
        }
        catch { }
        return node.Children.Count > 0 || root ? node : null;
    }
}
