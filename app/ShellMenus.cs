using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace RightMenu;

internal sealed record Appearance(bool HadClass, bool HadServer, bool HadValue, string? OldValue, RegistryValueKind OldKind);
internal sealed class ShellMenus(RegistryKey classes, string executable, string receipt)
{
    const string LegacyPrefix = "RightMenuFavorites.V1.";
    internal const string Prefix = "RMFav.";
    internal static bool IsProductVerb(string name) => name.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)
        || name.StartsWith(LegacyPrefix,StringComparison.OrdinalIgnoreCase);

    internal bool HasLegacyMenus()
    {
        foreach (var context in Contexts)
        {
            using var shell=classes.OpenSubKey(context+@"\shell");
            if (shell==null) continue;
            foreach (var name in shell.GetSubKeyNames().Where(n=>n.StartsWith(LegacyPrefix,StringComparison.OrdinalIgnoreCase)))
            {
                using var verb=shell.OpenSubKey(name);
                if (Equals(verb?.GetValue("Owner"),Owner)) return true;
            }
        }
        return false;
    }
    internal const string Owner = "{4065F0FE-E819-4DEA-A745-04336B5BE003}";
    const string Classic = @"CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    const string Server = Classic + @"\InprocServer32";
    static readonly string[] Contexts = ExistingMenus.Scopes.Keys.ToArray();
    internal static bool Applies(Category category,Item item,string context)
    {
        string? scope = item.Scope ?? category.Scope;
        return scope switch
        {
            null => context.EndsWith("Background") || (item.Terminal && context is "Directory" or "*"),
            "all" => context is @"Directory\Background" or "DesktopBackground" or "*" or "Directory" or "Drive",
            _ => scope == context
        };
    }
    [DllImport("shell32.dll")] static extern void SHChangeNotify(int id, uint flags, IntPtr first, IntPtr second);
    public static ShellMenus Current() => new(Registry.CurrentUser.CreateSubKey(@"Software\Classes"),Files.App,Files.Receipt);
    public static void Refresh() => SHChangeNotify(0x08000000,0,0,0);
    bool IsClassic()
    {
        using var key = classes.OpenSubKey(Server);
        return key?.GetValue("") is string value && value.Length == 0 && key.GetValueKind("") == RegistryValueKind.String;
    }
    void EnableAppearance()
    {
        if (File.Exists(receipt))
        {
            if (!IsClassic()) throw new InvalidOperationException("菜单外观已由其他程序修改。请先移除分类菜单，再决定是否重新启用。");
            return;
        }
        using var parent = classes.OpenSubKey(Classic);
        using var old = classes.OpenSubKey(Server);
        bool hadValue = old?.GetValueNames().Contains("") == true;
        var kind = hadValue ? old!.GetValueKind("") : RegistryValueKind.String;
        if (hadValue && kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            throw new InvalidOperationException("已有自定义菜单设置的类型不受支持，原值已保留。");
        string? value = old?.GetValue("",null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        Files.Save(receipt,new Appearance(parent != null,old != null,hadValue,value,kind));
        if (!IsClassic())
        {
            using var active = classes.CreateSubKey(Server);
            active.SetValue("","",RegistryValueKind.String);
        }
    }
    void PruneEmpty(string path)
    {
        using (var key = classes.OpenSubKey(path))
            if (key == null || key.ValueCount != 0 || key.SubKeyCount != 0) return;
        classes.DeleteSubKey(path,false);
    }
    void RestoreAppearance()
    {
        if (!File.Exists(receipt)) return;
        var old = Files.Read<Appearance>(receipt);
        if (IsClassic())
        {
            using (var key = classes.OpenSubKey(Server,true)!)
            {
                if (old.HadValue) key.SetValue("",old.OldValue!,old.OldKind);
                else key.DeleteValue("",false);
            }
            if (!old.HadServer) PruneEmpty(Server);
            if (!old.HadClass) PruneEmpty(Classic);
        }
        File.Delete(receipt);
    }
    public void Remove()
    {
        foreach (string context in Contexts)
        {
            using var parent = classes.OpenSubKey(context + @"\shell",true);
            if (parent != null) RemoveObsolete(parent,[]);
        }
        RestoreAppearance();
        Refresh();
    }
    static void RemoveObsolete(RegistryKey parent, HashSet<string> keep)
    {
        foreach (var name in parent.GetSubKeyNames().Where(n => IsProductVerb(n) && !keep.Contains(n)))
        {
            using (var child = parent.OpenSubKey(name))
                if (!Equals(child?.GetValue("Owner"),Owner)) continue;
            parent.DeleteSubKeyTree(name,false);
        }
    }
    void WriteAction(RegistryKey parent,string id,Item item,string context)
    {
        using var child = parent.CreateSubKey(id);
        child.SetValue("Owner",Owner);
        child.SetValue("MUIVerb",item.Name.Replace("&","&&"));
        using var command = child.CreateSubKey("command");
        string args = context switch
        {
            "DesktopBackground" => "desktop \"|\"",
            @"Directory\Background" => "folder \"%V|\"",
            "AllFilesystemObjects" => "object \"%1|\"",
            "Directory" or "Drive" or "Folder" => "folder \"%1|\"",
            _ => "file \"%1|\""
        };
        command.SetValue("",$"\"{executable}\" --run {item.Id:D}" + (item.Terminal ? " " + args : ""));
    }
    public void Apply(Library data)
    {
        EnableAppearance();
        foreach (string context in Contexts)
        {
            using var shell = classes.CreateSubKey(context + @"\shell");
            HashSet<string> keep = [];
            for (int n = 0; n < data.Categories.Count; n++)
            {
                var category = data.Categories[n];
                var entries = category.Items.Where(i => Applies(category,i,context)).ToArray();
                if (entries.Length == 0) continue;
                string name = $"{Prefix}{n:D6}.{category.Id:N}";
                keep.Add(name);
                using (var existing = shell.OpenSubKey(name))
                    if (existing != null && !Equals(existing.GetValue("Owner"),Owner))
                        throw new IOException("菜单项名称冲突：" + category.Name);
                using var root = shell.CreateSubKey(name);
                root.SetValue("Owner",Owner);
                root.SetValue("MUIVerb",category.Name.Replace("&","&&"));
                root.SetValue("ExtendedSubCommandsKey",context + @"\shell\" + name);
                using var children = root.CreateSubKey("shell");
                HashSet<string> required = [];
                for (int j = 0; j < entries.Length; j++)
                {
                    var item = entries[j];
                    string id = $"{Prefix}{j:D6}.{item.Id:N}";
                    required.Add(id);
                    WriteAction(children,id,item,context);
                }
                RemoveObsolete(children,required);
            }
            int order = 0;
            foreach (var item in data.SystemCategory.Items.Where(i => Applies(data.SystemCategory,i,context)))
            {
                string name = $"{Prefix}Direct.{order++:D6}.{item.Id:N}";
                using var existing = shell.OpenSubKey(name);
                if (existing != null && !Equals(existing.GetValue("Owner"),Owner))
                    throw new IOException("菜单项名称冲突：" + item.Name);
                keep.Add(name);
                WriteAction(shell,name,item,context);
            }
            RemoveObsolete(shell,keep);
        }
        Refresh();
    }
}



