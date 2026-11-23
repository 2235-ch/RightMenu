using Microsoft.Win32;
using RightMenu;

string nonce = Guid.NewGuid().ToString("N");
string registry = @"Software\RightMenuV1Tests\" + nonce;
string dir = Directory.CreateTempSubdirectory("右键 & ").FullName;
const string server = @"CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
const string bg = @"Directory\Background\shell";
static void Assert(bool condition,string text)
{
    if (!condition) throw new InvalidOperationException(text);
    Console.WriteLine("PASS  " + text);
}
try
{
    using var root = Registry.CurrentUser.CreateSubKey(registry);
    string receipt = Path.Combine(dir,"state.json");
    var menus = new ShellMenus(root,Path.Combine(dir,"右键菜单.exe"),receipt);
    var lib = Library.Initial();
    var broken = new Item { Target = Path.Combine(dir,"失效 & 引用.lnk") };
    Assert(broken.Status() == "位置失效","检测已移走的快捷方式");
    dynamic links = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
    dynamic shortcut = links.CreateShortcut(broken.Target);
    shortcut.TargetPath = Path.Combine(dir,"已移走.exe"); shortcut.Save();
    Assert(broken.Status() == "链接目标失效","检测快捷方式自身存在但目标失效");
    string file = Path.Combine(dir,"图 & 片.png");
    File.WriteAllText(file,"original");
    Assert(Library.Add(lib.Categories[0],[file,file,dir]) == 2,"混合添加与去重");
    var item = lib.Categories[0].Items[0];
    var id = item.Id;
    item.Name = "角色三视图";
    item.Target = Path.Combine(dir,"新位置.png");
    File.Move(file,item.Target);
    lib.Categories[0].Items.Reverse();
    string json = Path.Combine(dir,"收藏.json");
    Files.Save(json,lib);
    var restored = Files.Read<Library>(json);
    Assert(restored.Categories[0].Items[1].Id == id && restored.Categories[0].Items[1].Target == item.Target,"保存、改名、换序与重新定位");
    using (var third = root.CreateSubKey(bg + @"\AnotherApp")) third.SetValue("","保留");
    using (var old=root.CreateSubKey(bg+@"\RightMenuFavorites.V1.Direct.legacy")) old.SetValue("Owner",ShellMenus.Owner);
    Assert(menus.HasLegacyMenus(),"检测需要迁移的旧收藏入口");
    menus.Apply(lib); menus.Apply(lib);
    Assert(!menus.HasLegacyMenus(),"同步后旧入口已移除");
    using (var key = root.OpenSubKey(server)) Assert(Equals(key?.GetValue(""),""),"初始无兼容键时启用");
    using (var key = root.OpenSubKey(bg)!)
    {
        var order = key.GetSubKeyNames().Where(ShellMenus.IsProductVerb).Order().ToArray();
        Assert(order.Length == 2,"非空分类并列，重复启用无重复项");
        using var first = key.OpenSubKey(order[0]);
        Assert(Equals(first?.GetValue("MUIVerb"),"图片"),"菜单顺序");
    }
    Library.Add(lib.SystemCategory,[item.Target]);
    lib.SystemCategory.Items.Add(new Item { Name="直接 CMD",Kind=ActionKind.Cmd });
    menus.Apply(lib); menus.Apply(lib);
    foreach (var scope in new[]{bg,@"DesktopBackground\shell",@"Directory\shell",@"*\shell"})
    {
        using var shell=root.OpenSubKey(scope)!;
        var direct=shell.GetSubKeyNames().Where(n=>n.Contains(".Direct.")).ToArray();
        Assert(direct.All(n=>n.Length<64),"第一层命令使用短注册名");
        Assert(direct.Length==(scope.Contains("Background") ? 2 : 1),"系统条目直接注册且无重复 "+scope);
        using var verb=shell.OpenSubKey(direct[0])!;
        using var command=verb.OpenSubKey("command");
        Assert(verb.GetValue("ExtendedSubCommandsKey")==null && command?.GetValue("") is string cmd && cmd.Contains("--run"),"第一层直接执行");
    }
    lib.SystemCategory.Scope="Drive";
    lib.SystemCategory.Items[0].Scope="*";
    lib.Categories[0].Scope="Folder";
    menus.Apply(lib);
    foreach(var scope in ExistingMenus.Scopes.Keys)
    {
        using var shell=root.OpenSubKey(scope+@"\shell")!;
        var direct=shell.GetSubKeyNames().Where(n=>n.Contains(".Direct.")).ToArray();
        Assert(direct.All(n=>n.Length<64),"第一层命令使用短注册名");
        Assert(direct.Length==(scope is "Drive" or "*" ? 1 : 0),"条目覆盖分类位置并清理旧菜单 "+scope);
        bool hasCategory=shell.GetSubKeyNames().Any(n=>n.EndsWith(lib.Categories[0].Id.ToString("N")));
        Assert(hasCategory==(scope=="Folder"),"整个收藏分类切换位置 "+scope);
    }
    lib.SystemCategory.Items.Clear();
    lib.Categories.RemoveAt(0); menus.Apply(lib);
    Assert(File.ReadAllText(item.Target) == "original","移除分类保留原文件");
    menus.Remove(); menus.Remove();
    using (var key = root.OpenSubKey(server)) Assert(key == null,"连续撤销恢复初始缺失状态");
    using (var key = root.OpenSubKey(bg)!) Assert(key.GetSubKeyNames().SequenceEqual(["AnotherApp"]),"清理产品菜单并保留第三方键");
    using (var key = root.CreateSubKey(server)) key.SetValue("","");
    menus.Apply(lib); menus.Remove();
    using (var key = root.OpenSubKey(server)) Assert(Equals(key?.GetValue(""),""),"原本经典菜单保持原样");
    using (var key = root.CreateSubKey(server)) key.SetValue("","%TEMP%",RegistryValueKind.ExpandString);
    menus.Apply(lib); menus.Remove();
    using (var key = root.OpenSubKey(server)) Assert(Equals(key?.GetValue("",null,RegistryValueOptions.DoNotExpandEnvironmentNames),"%TEMP%")
        && key.GetValueKind("") == RegistryValueKind.ExpandString,"恢复原值和原类型");
    menus.Apply(lib);
    using (var key = root.CreateSubKey(server)) key.SetValue("","其他应用的更改");
    menus.Remove();
    using (var key = root.OpenSubKey(server)) Assert(Equals(key?.GetValue(""),"其他应用的更改"),"保留启用后的外部修改");
    using var u = root.CreateSubKey("testUser");
    using var m = root.CreateSubKey("testMachine");
    using var catalog = new ExistingMenus(u,m,dir);
    const string gitPath = bg + @"\git_shell";
    using (var key = m.CreateSubKey(gitPath)) key.SetValue("","Open Git Ba&sh here");
    using (var key = m.CreateSubKey(gitPath + @"\command")) key.SetValue("","git-bash.exe");
    var git = catalog.Read().Single(x=>x.Key=="git_shell");
    Assert(git.Name=="Open Git Bash here" && git.Editable,"识别整机注册的 Git 菜单");
    catalog.Rename(git,"打开 & 终端");
    Assert(catalog.Read().Single(x=>x.Key=="git_shell") is {Name:"打开 & 终端",Renamed:true},"用户菜单改名保留 &");
    catalog.SetHidden(git,true); catalog.SetHidden(git,true);
    Assert(catalog.Read().Single(x=>x.Key=="git_shell").Hidden,"按当前用户隐藏，可重复操作");
    using (var key = m.OpenSubKey(gitPath)) Assert(key?.GetValue("ProgrammaticAccessOnly")==null,"整机注册项原样保留");
    catalog.RestoreAll();
    using (var key = u.OpenSubKey(gitPath)) Assert(key==null,"恢复后清理本程序创建的覆盖项");
    using (var key = u.CreateSubKey(gitPath))
    {
        key.SetValue("MUIVerb","%TEMP%",RegistryValueKind.ExpandString);
        key.SetValue("Icon","my.ico");
    }
    catalog.Rename(git,"新名"); catalog.Rename(git,"再改名"); catalog.Rename(git,null);
    using (var key=u.OpenSubKey(gitPath)!) Assert(key.GetValueKind("MUIVerb")==RegistryValueKind.ExpandString &&
        Equals(key.GetValue("MUIVerb",null,RegistryValueOptions.DoNotExpandEnvironmentNames),"%TEMP%"),"恢复原显示名称和类型");
    catalog.Rename(git,"临时名");
    using(var key=u.OpenSubKey(gitPath,true)!) key.SetValue("MUIVerb","外部改名");
    catalog.RestoreAll();
    using(var key=u.OpenSubKey(gitPath)!) Assert(Equals(key.GetValue("MUIVerb"),"外部改名"),"恢复时保留外部改名");
    catalog.SetHidden(git,true);
    using (var key = u.OpenSubKey(gitPath,true)!) key.SetValue("ProgrammaticAccessOnly","external");
    catalog.RestoreAll();
    using (var key = u.OpenSubKey(gitPath)) Assert(Equals(key?.GetValue("ProgrammaticAccessOnly"),"external") &&
        Equals(key.GetValue("Icon"),"my.ico"),"恢复时保留已有图标及外部修改");
    var external=catalog.Read().Single(x=>x.Key=="git_shell");
    Assert(external.Editable && external.Hidden,"原本隐藏的命令可以接管");
    var takeover=catalog.TakeOver();
    Assert(takeover.Count==1 && File.ReadAllText(takeover.File!).Contains("git_shell"),"先备份再接管");
    Assert(catalog.TakeOver().Count==0,"重复接管无重复入口");
    catalog.SetHidden(external,false);
    var managed=catalog.Read().Single(x=>x.Key=="git_shell");
    Assert(managed.Managed && !managed.Hidden,"接管后启用曾由外部隐藏的命令");
    catalog.Rename(managed,"自定名称");
    Assert(catalog.Read().Single(x=>x.Key=="git_shell").Name=="自定名称","修改接管入口");
    catalog.ReleaseAll();
    using(var key=u.OpenSubKey(gitPath)) Assert(Equals(key?.GetValue("ProgrammaticAccessOnly"),"external"),"撤回保留原始外部值");
    Assert(catalog.Read().Single(x=>x.Key=="git_shell") is {Hidden:true,Managed:false},"撤回原状");
}
finally
{
    Registry.CurrentUser.DeleteSubKeyTree(registry,false);
    Directory.Delete(dir,true);
}


