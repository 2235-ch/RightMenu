using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace RightMenu;

internal sealed partial class ExistingMenus
{
    const string ControlMarker="RightMenuFavorites.Control.v2";
    const string ManagedPrefix="RMManaged";
    const string Source="RightMenuFavorites.Source";
    const string Backup="RightMenuFavorites.Backup";

    static string Mirror(string scope,string verb)
    {
        var id=SHA256.HashData(Encoding.UTF8.GetBytes(verb.ToUpperInvariant()));
        return scope+@"\shell\"+ManagedPrefix+Convert.ToHexString(id.AsSpan(0,12));
    }
    static bool PlainCommand(RegistryKey? u,RegistryKey? m,RegistryKey? cu,RegistryKey? cm)
    {
        return Value(cu,cm,"") is {Length:>0} &&
            !Has(cu,"DelegateExecute") && !Has(cm,"DelegateExecute") &&
            !new[]{"ExplorerCommandHandler","ExtendedSubCommandsKey","SubCommands","SuppressionPolicy","SuppressionPolicyEx"}
                .Any(n=>Has(u,n)||Has(m,n)) &&
            Names(u,m).All(n=>n.Equals("command",StringComparison.OrdinalIgnoreCase));
    }
    static bool Owned(RegistryKey? key,string path) => Equals(key?.GetValue(Source),path);
    static void CopyValues(RegistryKey? src,RegistryKey dest,bool root)
    {
        if(src==null) return;
        foreach(var n in src.GetValueNames())
        {
            if(root && (n.StartsWith("RightMenuFavorites.",StringComparison.OrdinalIgnoreCase) ||
                n.Equals(Invisible,StringComparison.OrdinalIgnoreCase) || n.Equals("LegacyDisable",StringComparison.OrdinalIgnoreCase) || n.Equals("HideBasedOnVelocityId",StringComparison.OrdinalIgnoreCase) || n.Equals("ShowBasedOnVelocityId",StringComparison.OrdinalIgnoreCase) || n.Equals("Extended",StringComparison.OrdinalIgnoreCase))) continue;
            dest.SetValue(n,src.GetValue(n,null,RegistryValueOptions.DoNotExpandEnvironmentNames)!,src.GetValueKind(n));
        }
    }

    internal (int Count,string? File) TakeOver(IEnumerable<ExistingEntry>? selection=null)
    {
        var wanted=selection?.Select(r=>(r.Scope,r.Key)).ToHashSet();
        var rows=Read().Where(r=>r.CanTake && !r.Managed && (wanted==null || wanted.Contains((r.Scope,r.Key)))).ToList();
        if(rows.Count==0) return (0,null);
        string folder=backupFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RightMenuBackups","接管");
        Directory.CreateDirectory(folder);
        string file=Path.Combine(folder,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".reg");
        using(var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        using(var writer=new StreamWriter(stream,Encoding.Unicode))
        {
            writer.WriteLine("Windows Registry Editor Version 5.00");
            foreach(var row in rows)
            {
                string path=row.Scope+@"\shell\"+row.Key;
                using var u=user.OpenSubKey(path);
                using var m=machine.OpenSubKey(path);
                Export(writer,u,user.Name+"\\"+path);
                Export(writer,m,machine.Name+"\\"+path);
            }
            writer.Flush(); stream.Flush(true);
        }
        var applied=new List<ExistingEntry>();
        try
        {
            foreach(var row in rows)
            {
                string path=row.Scope+@"\shell\"+row.Key;
                using var before=user.OpenSubKey(path);
                using var m=machine.OpenSubKey(path);
                using var cu=before?.OpenSubKey("command");
                using var cm=m?.OpenSubKey("command");
                if(!PlainCommand(before,m,cu,cm)) throw new IOException("命令已变更："+row.Name);
                string mirror=Mirror(row.Scope,row.Key);
                using(var collision=user.OpenSubKey(mirror))
                    if(collision!=null) throw new IOException("接管入口已存在："+row.Name);
                using var original=user.CreateSubKey(path);
                int flags=(before==null ? 2 : 0) | (Has(before,Invisible) ? 1 : 0);
                original.SetValue(ControlMarker,flags,RegistryValueKind.DWord);
                applied.Add(row);
                using var copy=user.CreateSubKey(mirror);
                copy.SetValue(Source,path);
                copy.SetValue(Invisible,"");
                copy.SetValue(Backup,file);
                CopyValues(m,copy,true); CopyValues(before,copy,true);
                if (!Has(copy,"MUIVerb") && !Has(copy,"")) copy.SetValue("MUIVerb",row.Name.Replace("&","&&"));
                using var command=copy.CreateSubKey("command");
                CopyValues(cm,command,false); CopyValues(cu,command,false);
                if((flags&1)==0) original.SetValue(Invisible,"");
                bool normallyHidden=row.Hidden || Has(before,"Extended") || Has(m,"Extended") ||
                    Has(before,"HideBasedOnVelocityId") || Has(m,"HideBasedOnVelocityId");
                if(!normallyHidden) copy.DeleteValue(Invisible,false);
            }
        }
        catch
        {
            foreach(var row in Enumerable.Reverse(applied)) Release(row);
            throw;
        }
        finally { ShellMenus.Refresh(); }
        return (rows.Count,file);
    }

    static void Export(TextWriter writer,RegistryKey? key,string path)
    {
        writer.WriteLine();
        if(key==null){writer.WriteLine("; Originally absent: "+path);return;}
        writer.WriteLine("["+path+"]");
        foreach(var n in key.GetValueNames())
        {
            var kind=key.GetValueKind(n);
            var value=key.GetValue(n,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
            byte[] bytes=value switch {
                string s=>Encoding.Unicode.GetBytes(s+"\0"),
                string[] a=>Encoding.Unicode.GetBytes(string.Join("\0",a)+"\0\0"),
                int i=>BitConverter.GetBytes(i),long l=>BitConverter.GetBytes(l),byte[] b=>b,
                _=>throw new IOException("不支持的备份值："+path+" "+n)
            };
            string label=n.Length==0?"@":"\""+n.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
            int type=kind==RegistryValueKind.None?0:(int)kind;
            writer.WriteLine(label+"=hex("+type.ToString("x")+"):"+string.Join(",",bytes.Select(b=>b.ToString("x2"))));
        }
        foreach(var sub in key.GetSubKeyNames())
        {
            using var child=key.OpenSubKey(sub) ?? throw new IOException("备份期间注册项已变化："+sub);
            Export(writer,child,path+"\\"+sub);
        }
    }
    void Release(ExistingEntry row)
    {
        string path=row.Scope+@"\shell\"+row.Key, mirror=Mirror(row.Scope,row.Key);
        using var original=user.OpenSubKey(path,true);
        if(original?.GetValue(ControlMarker) is not int flags) return;
        using(var copy=user.OpenSubKey(mirror))
            if(copy!=null && !Owned(copy,path)) throw new IOException("接管入口所有者已变化："+row.Name);
        user.DeleteSubKeyTree(mirror,false);
        if((flags&1)==0 && original.GetValue(Invisible) is string {Length:0} && original.GetValueKind(Invisible)==RegistryValueKind.String)
            original.DeleteValue(Invisible,false);
        original.DeleteValue(ControlMarker,false);
        if((flags&2)!=0 && original.SubKeyCount==0 && original.ValueCount==0) user.DeleteSubKey(path,false);
    }
    internal void ReleaseAll()
    {
        try { foreach(var row in Read().Where(r=>r.Managed)) Release(row); }
        finally { ShellMenus.Refresh(); }
    }
}



