using Microsoft.Win32;

namespace RightMenu;

internal sealed record ExistingEntry(string Scope,string Key,string Name,string State,string Detail,bool Hidden,bool Editable,bool Renamed=false,bool Managed=false,bool CanTake=false);

internal sealed partial class ExistingMenus(RegistryKey user,RegistryKey machine,string? backupFolder=null) : IDisposable
{
    internal static readonly Dictionary<string,string> Scopes = new()
    {
        [@"Directory\Background"]="桌面和文件夹空白处", ["DesktopBackground"]="桌面专属",
        ["Directory"]="文件夹", ["Folder"]="所有文件夹对象", ["*"]="文件",
        ["AllFilesystemObjects"]="文件和文件夹", ["Drive"]="驱动器"
    };
    const string Marker = "RightMenuFavorites.Hidden.v2";
    const string AliasMarker = "RightMenuFavorites.Name.v2";
    sealed record Alias(string? Original, RegistryValueKind Kind, string Applied, bool Created);
    const string Invisible = "ProgrammaticAccessOnly";
    static readonly HashSet<string> protectedVerbs = new(StringComparer.OrdinalIgnoreCase)
        { "open","opennewwindow","opennewprocess","explore","runas","runasuser","find","properties","printto" };
    internal static ExistingMenus Current() => new(Registry.CurrentUser.CreateSubKey(@"Software\Classes"),
        Registry.LocalMachine.OpenSubKey(@"Software\Classes")!);
    public void Dispose() { user.Dispose(); machine.Dispose(); }
    static bool Has(RegistryKey? key,string name) => key?.GetValueNames().Contains(name,StringComparer.OrdinalIgnoreCase) == true;
    static string? Value(RegistryKey? a,RegistryKey? b,string name) =>
        (a?.GetValue(name) ?? b?.GetValue(name)) as string;
    static IEnumerable<string> Names(RegistryKey? a,RegistryKey? b) =>
        (a?.GetSubKeyNames() ?? []).Union(b?.GetSubKeyNames() ?? [],StringComparer.OrdinalIgnoreCase);
    internal List<ExistingEntry> Read()
    {
        var rows = new List<ExistingEntry>();
        foreach (string scope in Scopes.Keys)
        {
            using var u = user.OpenSubKey(scope + @"\shell");
            using var m = machine.OpenSubKey(scope + @"\shell");
            foreach (string verb in Names(u,m))
            {
                if (verb.StartsWith(ManagedPrefix,StringComparison.OrdinalIgnoreCase) || ShellMenus.IsProductVerb(verb)) continue;
                using var uk = u?.OpenSubKey(verb);
                using var mk = m?.OpenSubKey(verb);
                using var commandU = uk?.OpenSubKey("command");
                using var commandM = mk?.OpenSubKey("command");
                string label = Value(uk,mk,"MUIVerb") ?? Value(uk,mk,"") ?? verb;
                if (label.StartsWith('@')) label = verb switch { "Display"=>"显示设置","Personalize"=>"个性化",_=>verb };
                label = label.Replace("&&","\0").Replace("&","").Replace("\0","&");
                bool own = uk?.GetValue(Marker) is int flag && flag is 1 or 2;
                bool external = Has(uk,Invisible) || Has(mk,Invisible) || Has(uk,"LegacyDisable") || Has(mk,"LegacyDisable");
                bool conditional = new[]{"Extended","AppliesTo","HideBasedOnVelocityId","ShowBasedOnVelocityId","SuppressionPolicy","SuppressionPolicyEx"}
                    .Any(x=>Has(uk,x)||Has(mk,x));
                bool locked = protectedVerbs.Contains(verb) || (Has(uk,Marker) && !own);
                bool canTake=scope!="DesktopBackground" && !locked && PlainCommand(uk,mk,commandU,commandM);
                string path=scope+@"\shell\"+verb;
                bool managed=uk?.GetValue(ControlMarker) is int;
                using var mirror=managed ? user.OpenSubKey(Mirror(scope,verb)) : null;
                bool intact=managed && Owned(mirror,path) && mirror!.GetValue(Backup) is string;
                string state = own ? "已由本程序隐藏" : external ? "已隐藏" : conditional ? "按条件显示" : "已注册";
                string detail = (uk != null && mk != null ? "用户及系统" : uk != null ? "当前用户" : "系统") +
                    " · " + scope + @"\shell\" + verb + Environment.NewLine + (Value(commandU,commandM,"") ?? "");
                bool renamed = Has(uk,AliasMarker);
                if (managed)
                {
                    bool hidden=Has(mirror,Invisible); conditional=Has(mirror,"AppliesTo") || Has(mirror,"CommandStateHandler");
                    label=(mirror?.GetValue("MUIVerb") is string text && !text.StartsWith('@') ? text.Replace("&&","&") : label);
                    renamed=Has(mirror,AliasMarker);
                    state=!intact ? "接管中断 · 可撤回" : hidden ? "已接管 · 已隐藏" : conditional ? "已接管 · 按条件显示" : "已接管 · 已启用";
                    detail+=Environment.NewLine+"备份："+mirror?.GetValue(Backup);
                    rows.Add(new(scope,verb,label,state,detail,hidden,intact,renamed,true,canTake));
                }
                else rows.Add(new(scope,verb,label,renamed ? state+" · 已改名" : state,detail,own||external,own || (!locked && (!external||canTake)),renamed,false,canTake));
            }
            string handlers = scope + @"\shellex\ContextMenuHandlers";
            using var uh = user.OpenSubKey(handlers);
            using var mh = machine.OpenSubKey(handlers);
            foreach (string name in Names(uh,mh))
                rows.Add(new(scope,name,name.Trim(),"动态扩展 · 只读",handlers + "\\" + name,false,false));
        }
        return rows;
    }
    internal void SetHidden(ExistingEntry entry,bool hide)
    {
        if (!Scopes.ContainsKey(entry.Scope) || entry.Key.Contains('\\')) throw new ArgumentException("无效的菜单位置。");
        string path = entry.Scope + @"\shell\" + entry.Key;
        var row=Read().FirstOrDefault(x=>x.Scope==entry.Scope && x.Key==entry.Key && x.Editable);
        if (row==null) throw new InvalidOperationException("条目已变更，请刷新。");
        if (!row.Managed && row.CanTake && row.Hidden && !HasOldHide(path))
        {
            TakeOver([row]);
            row=Read().Single(x=>x.Scope==entry.Scope && x.Key==entry.Key && x.Managed);
        }
        if (row.Managed)
        {
            using var copy=user.OpenSubKey(Mirror(row.Scope,row.Key),true);
            if (!Owned(copy,path)) throw new IOException("接管入口已变更。");
            if (hide) copy!.SetValue(Invisible,""); else copy!.DeleteValue(Invisible,false);
            ShellMenus.Refresh(); return;
        }
        if (hide)
        {
            var current = Read().FirstOrDefault(x=>x.Scope==entry.Scope && x.Key==entry.Key && x.Editable);
            if (current == null) throw new InvalidOperationException("条目已变更，请刷新列表。");
            if (current.Hidden) return;
            using var previous = user.OpenSubKey(path);
            using var key = user.CreateSubKey(path);
            key.SetValue(Marker,previous==null || ReadAlias(previous)?.Created==true ? 2 : 1,RegistryValueKind.DWord);
            key.SetValue(Invisible,"",RegistryValueKind.String);
        }
        else
        {
            bool prune;
            using (var key = user.OpenSubKey(path,true))
            {
                if (key?.GetValue(Marker) is not int marker || marker is not (1 or 2)) return;
                if (key.GetValue(Invisible) is string {Length:0} && key.GetValueKind(Invisible)==RegistryValueKind.String)
                    key.DeleteValue(Invisible,false);
                key.DeleteValue(Marker);
                prune = marker==2 && key.ValueCount==0 && key.SubKeyCount==0;
            }
            if (prune) user.DeleteSubKey(path,false);
        }
        ShellMenus.Refresh();
    }
    bool HasOldHide(string path)
    {
        using var key=user.OpenSubKey(path);
        return key?.GetValue(Marker) is int n && n is 1 or 2;
    }
    static Alias? ReadAlias(RegistryKey key) => key.GetValue(AliasMarker) is string json
        ? System.Text.Json.JsonSerializer.Deserialize<Alias>(json) : null;

    internal void Rename(ExistingEntry entry,string? name)
    {
        var row=Read().FirstOrDefault(x=>x.Scope==entry.Scope && x.Key==entry.Key && (x.Editable || x.Renamed))
            ?? throw new InvalidOperationException("条目已变更，请刷新。");
        string path=row.Scope+@"\shell\"+row.Key;
        if (!row.Managed && row.Hidden && row.CanTake && !HasOldHide(path) && name!=null)
        {
            TakeOver([row]);
            row=Read().Single(x=>x.Scope==entry.Scope && x.Key==entry.Key && x.Managed);
        }
        if (row.Managed) path=Mirror(row.Scope,row.Key);
        using var before=user.OpenSubKey(path);
        var alias=before==null ? null : ReadAlias(before);
        if (name==null && alias==null) return;
        bool prune=false;
        using (var key=user.CreateSubKey(path))
        {
            var raw=key.GetValue("MUIVerb",null,RegistryValueOptions.DoNotExpandEnvironmentNames);
            var kind=raw==null ? RegistryValueKind.None : key.GetValueKind("MUIVerb");
            if (name==null)
            {
                if (Equals(raw,alias!.Applied) && kind==RegistryValueKind.String)
                {
                    if (alias.Original==null) key.DeleteValue("MUIVerb",false);
                    else key.SetValue("MUIVerb",alias.Original,alias.Kind);
                }
                key.DeleteValue(AliasMarker,false);
                prune=alias.Created && key.SubKeyCount==0 && key.ValueCount==0;
            }
            else
            {
                if (!row.Editable || string.IsNullOrWhiteSpace(name) || name.Contains('\0'))
                    throw new InvalidOperationException("此条目无法使用该名称。");
                if (alias!=null && (!Equals(raw,alias.Applied) || kind!=RegistryValueKind.String))
                    throw new IOException("名称已被其他程序修改，请先恢复名称记录。");
                if (raw!=null && kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                    throw new IOException("名称值类型不支持。");
                string applied=name.Trim().Replace("&","&&");
                alias=(alias ?? new Alias(raw as string,kind,applied,before==null || Equals(key.GetValue(Marker),2))) with { Applied=applied };
                key.SetValue(AliasMarker,System.Text.Json.JsonSerializer.Serialize(alias));
                key.SetValue("MUIVerb",applied,RegistryValueKind.String);
            }
        }
        if (prune) user.DeleteSubKey(path,false);
        ShellMenus.Refresh();
    }

    internal void RestoreAll()
    {
        ReleaseAll();
        foreach (var row in Read().Where(x=>x.Renamed)) Rename(row,null);
        foreach (var row in Read().Where(x=>x.Editable && HasOldHide(x.Scope+@"\shell\"+x.Key))) SetHidden(row,false);
    }
}


