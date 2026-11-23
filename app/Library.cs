using System.Text.Json;
using System.Text.Json.Serialization;

namespace RightMenu;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum ActionKind { File, Folder, Cmd, PowerShell }

internal sealed class Item
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Scope { get; set; }
    public ActionKind Kind { get; set; }
    public string Target { get; set; } = "";
    [JsonIgnore] public bool Terminal => Kind is ActionKind.Cmd or ActionKind.PowerShell;
    public string Status()
    {
        if (Terminal) return "内置";
        if (!Directory.Exists(Target) && !File.Exists(Target)) return "位置失效";
        if (!Path.GetExtension(Target).Equals(".lnk",StringComparison.OrdinalIgnoreCase)) return "可用";
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            string destination = Environment.ExpandEnvironmentVariables((string)shell.CreateShortcut(Target).TargetPath);
            if (Path.IsPathFullyQualified(destination) && !File.Exists(destination) && !Directory.Exists(destination))
                return "链接目标失效";
        }
        catch (System.Runtime.InteropServices.COMException) { return "链接待检查"; }
        return "可用";
    }
    [JsonIgnore] public string TypeName => Kind switch
    {
        ActionKind.Folder => "文件夹", ActionKind.Cmd => "CMD", ActionKind.PowerShell => "PowerShell",
        _ => Path.GetExtension(Target).ToLowerInvariant() switch { ".lnk" => "快捷方式", ".exe" => "应用", _ => "文件" }
    };
}

internal sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Scope { get; set; }
    public List<Item> Items { get; set; } = [];
    public override string ToString() => Name;
}

internal sealed class Library
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; }
    public List<Category> Categories { get; set; } = [];
    public Category SystemCategory { get; set; } = new() { Name = "系统" };
    public static Library Initial() => new() { Categories = [
        new() { Name = "图片" }, new() { Name = "应用" },
        new() { Name = "程序工具", Items = [
            new() { Name = "在此处打开 CMD", Kind = ActionKind.Cmd },
            new() { Name = "在此处打开 PowerShell", Kind = ActionKind.PowerShell }
        ] }
    ] };
    public static int Add(Category group, IEnumerable<string> targets)
    {
        var seen = group.Items.Select(x => x.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var target in targets)
        {
            string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
            bool folder = Directory.Exists(path);
            if ((!folder && !File.Exists(path)) || !seen.Add(path)) continue;
            var label = folder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
            group.Items.Add(new() { Target = path, Name = string.IsNullOrWhiteSpace(label) ? path : label,
                Kind = folder ? ActionKind.Folder : ActionKind.File });
            added++;
        }
        return added;
    }
}

internal static class Files
{
    internal static readonly string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RightMenuFavorites");
    internal static readonly string App = Path.Combine(Home,"右键菜单.exe");
    internal static readonly string Config = Path.Combine(Home,"收藏.json");
    internal static readonly string Receipt = Path.Combine(Home,"菜单状态.json");
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Options)
        ?? throw new InvalidDataException("无法读取 " + path);
    public static Library Load()
    {
        if (!File.Exists(Config)) return Library.Initial();
        var data = Read<Library>(Config);
        if (data.Version != 1) throw new InvalidDataException("收藏格式版本不受支持，已保留原文件。");
        return data;
    }
    public static void Save<T>(string path,T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                JsonSerializer.Serialize(stream,value,Options);
                stream.Flush(true);
            }
            File.Move(temp,path,true);
        }
        finally { File.Delete(temp); }
    }
}
