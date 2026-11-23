using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace RightMenu;

internal static class DesktopMenu
{
    internal sealed record Row(string Name, bool Enabled);
    // Documented COM vtable slots include IUnknown and the inherited interface methods.
    static T Method<T>(nint instance,int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot * IntPtr.Size));
    delegate int FindWindow(nint self, nint location, nint root, int kind, out int hwnd,int flags,out nint dispatch);
    delegate int Service(nint self,ref Guid service,ref Guid iid,out nint result);
    delegate int ActiveView(nint self,out nint result);
    delegate int ItemObject(nint self,uint item,ref Guid iid,out nint result);
    delegate int QueryMenu(nint self,nint menu,uint index,uint first,uint last,uint flags);
    delegate int CommandString(nint self,nuint id,uint flags,nint reserved,[In,Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer,uint length);
    [DllImport("user32")] static extern nint CreatePopupMenu();
    [DllImport("user32")] static extern bool DestroyMenu(nint menu);
    [DllImport("user32")] static extern int GetMenuItemCount(nint menu);
    [DllImport("user32",CharSet=CharSet.Unicode)] static extern int GetMenuString(nint menu,int pos,StringBuilder name,int length,uint flags);
    [DllImport("user32")] static extern uint GetMenuState(nint menu,uint pos,uint flags);
    [StructLayout(LayoutKind.Sequential)]
    struct MenuInfo
    {
        public uint Size,Mask,Type,State,Id;
        public nint Submenu,Checked,Unchecked,Data,Text;
        public uint Length;
        public nint Bitmap;
    }
    [DllImport("user32",CharSet=CharSet.Unicode)] static extern bool GetMenuItemInfo(nint menu,uint pos,bool byPosition,ref MenuInfo item);

    static void Check(int hr,nint ptr)
    {
        Marshal.ThrowExceptionForHR(hr);
        if (ptr == 0) throw new InvalidOperationException("资源管理器桌面视图不可用");
    }
    internal static List<Row> Read()
    {
        object windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!)!;
        nint unk=0, collection=0, dispatch=0, provider=0, browser=0, view=0, context=0, menu=0;
        nint empty=Marshal.AllocCoTaskMem(32);
        try
        {
            Marshal.Copy(new byte[32],0,empty,32);
            unk=Marshal.GetIUnknownForObject(windows);
            Guid iid=new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
            Check(Marshal.QueryInterface(unk,ref iid,out collection),collection);
            Check(Method<FindWindow>(collection,15)(collection,empty,empty,8,out _,1,out dispatch),dispatch);
            iid=new("6D5140C1-7436-11CE-8034-00AA006009FA");
            Check(Marshal.QueryInterface(dispatch,ref iid,out provider),provider);
            Guid service=new("4C96BE40-915C-11CF-99D3-00AA004AE837");
            iid=new("000214E2-0000-0000-C000-000000000046");
            Check(Method<Service>(provider,3)(provider,ref service,ref iid,out browser),browser);
            Check(Method<ActiveView>(browser,15)(browser,out view),view);
            iid=new("000214E4-0000-0000-C000-000000000046");
            Check(Method<ItemObject>(view,15)(view,0,ref iid,out context),context);
            menu=CreatePopupMenu();
            if (menu==0) throw new InvalidOperationException("无法创建菜单快照");
            Marshal.ThrowExceptionForHR(Method<QueryMenu>(context,3)(context,menu,0,1,0x7fff,0));
            var ownedVerbs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach (string scope in new[]{@"Directory\Background","DesktopBackground"})
            {
                using var shell=Registry.ClassesRoot.OpenSubKey(scope+@"\shell");
                if (shell==null) continue;
                foreach (var key in shell.GetSubKeyNames().Where(ShellMenus.IsProductVerb))
                {
                    using var verb=shell.OpenSubKey(key);
                    if (Equals(verb?.GetValue("Owner"),ShellMenus.Owner) && verb.GetValue("MUIVerb") is string label)
                        ownedVerbs[key]=label;
                }
            }
            var snapshot=new List<(string Raw,string Verb,bool Enabled)>();
            for (uint i=0;i<GetMenuItemCount(menu);i++)
            {
                var name=new StringBuilder(1024);
                if (GetMenuString(menu,(int)i,name,name.Capacity,0x400)==0) continue;
                var info=new MenuInfo { Size=(uint)Marshal.SizeOf<MenuInfo>(),Mask=2 };
                var verb=new StringBuilder(512);
                if (GetMenuItemInfo(menu,i,true,ref info) && info.Id is >0 and <=0x7fff)
                    if (Method<CommandString>(context,5)(context,info.Id-1,4,0,verb,(uint)verb.Capacity)!=0) verb.Clear();
                string raw=name.ToString();
                snapshot.Add((raw,verb.ToString(),(GetMenuState(menu,i,0x400)&3)==0));
            }
            // Some handlers omit the canonical verb. Consume each remaining owned label
            // at most once so an unrelated command with the same caption stays visible.
            var known=snapshot.Where(x=>x.Verb.Length>0).Select(x=>x.Verb).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var fallback=ownedVerbs.Where(x=>!known.Contains(x.Key)).GroupBy(x=>x.Value)
                .ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);
            List<Row> rows=[];
            foreach (var entry in snapshot)
            {
                if (ownedVerbs.ContainsKey(entry.Verb)) continue;
                if (entry.Verb.Length==0 && fallback.GetValueOrDefault(entry.Raw)>0)
                {
                    fallback[entry.Raw]--;
                    continue;
                }
                string label=entry.Raw.Split('\t')[0].Replace("&&","\0").Replace("&","").Replace("\0","&");
                rows.Add(new(label,entry.Enabled));
            }
            return rows;
        }
        finally
        {
            if (menu!=0) DestroyMenu(menu);
            foreach (var ptr in new[]{context,view,browser,provider,dispatch,collection,unk})
                if (ptr!=0) Marshal.Release(ptr);
            Marshal.FreeCoTaskMem(empty);
            Marshal.ReleaseComObject(windows);
        }
    }
}

