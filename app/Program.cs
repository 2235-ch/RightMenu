using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace RightMenu;

internal static class Program
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RightMenuFavorites.4065F0FE";
    static readonly string StartLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"右键菜单收藏管理.lnk");
    static readonly string CacheList = Path.Combine(Files.Home,"运行库缓存.json");
    internal static readonly string ManageStatic = Path.Combine(Files.Home,"静态菜单接管.json");
    static readonly string UninstallTicket = Path.Combine(Files.Home,"卸载请求");
    static readonly string CacheRoot = Path.Combine(Path.GetTempPath(),".net","右键菜单");
    static readonly string[] BundleDlls = ["D3DCompiler_47_cor3.dll","PenImc_cor3.dll","PresentationNative_cor3.dll","vcruntime140_cor3.dll","wpfgfx_cor3.dll"];
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.FirstOrDefault() == "--run") { Run(args); return 0; }
            using var mutex = new Mutex(false,@"Local\RightMenuFavorites.4065F0FE");
            bool owns;
            try { owns = mutex.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
            if (!owns) throw new InvalidOperationException("管理窗口已打开，请先关闭已有窗口。");
            try
            {
                if (args.SequenceEqual(["--uninstall"])) { Uninstall(); return 0; }
                if (args.Length != 0) throw new ArgumentException("无法识别启动参数。");
                Install();
                if (!File.Exists(ManageStatic) || Files.Read<bool>(ManageStatic))
                {
                    try
                    {
                        using var catalog=ExistingMenus.Current();
                        catalog.TakeOver();
                        Files.Save(ManageStatic,true);
                    }
                    catch(Exception ex)
                    {
                        MessageBox.Show("自动接管未完成，可在管理窗口重试或撤回。\n"+ex.Message,"右键菜单");
                    }
                }
                Application.Run(new Manager());
            }
            finally { mutex.ReleaseMutex(); }
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message,"右键菜单",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return 1;
        }
    }
    static void Install()
    {
        Directory.CreateDirectory(Files.Home);
        File.Delete(UninstallTicket);
        string current = Environment.ProcessPath!;
        UpdateBinary(current, Files.App);
        RegisterInstallation();
    }
    static void UpdateBinary(string current,string installed)
    {
        if (string.Equals(current,installed,StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(installed))
        {
            var installedVersion = BuildVersion(installed);
            bool preview = installedVersion == new Version(2,1,0,0) ||
                (installedVersion.Major == 2 && installedVersion.Minor == 0 && installedVersion.Build == 0 &&
                 installedVersion.Revision is >= 0 and <= 5);
            if (BuildVersion(current) < installedVersion && !preview)
                throw new InvalidOperationException("已安装较新的版本，请从开始菜单打开「右键菜单收藏管理」。");
            using var a = File.OpenRead(current);
            using var b = File.OpenRead(installed);
            if (a.Length == b.Length && SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) return;
        }
        string temp = Path.Combine(Path.GetDirectoryName(installed)!,Guid.NewGuid() + ".tmp");
        try
        {
            File.Copy(current,temp);
            File.Move(temp,installed,true);
        }
        finally { File.Delete(temp); }
    }
    static Version BuildVersion(string path)
    {
        var v = FileVersionInfo.GetVersionInfo(path);
        return new Version(v.FileMajorPart,v.FileMinorPart,v.FileBuildPart,v.FilePrivatePart);
    }
    static void RegisterInstallation()
    {
        string[] search = (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries);
        var cacheDirs = File.Exists(CacheList) ? Files.Read<List<string>>(CacheList) : [];
        foreach (string dir in search.Select(Path.TrimEndingDirectorySeparator))
            if (IsBundleCache(dir) && !cacheDirs.Contains(dir,StringComparer.OrdinalIgnoreCase))
                cacheDirs.Add(dir);
        Files.Save(CacheList,cacheDirs);
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName","右键菜单收藏管理");
        key.SetValue("DisplayVersion","1.0");
        key.SetValue("DisplayIcon",Files.App);
        key.SetValue("UninstallString",$"\"{Files.App}\" --uninstall");
        key.SetValue("InstallLocation",Files.Home);
        if (!File.Exists(StartLink))
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic shortcut = shell.CreateShortcut(StartLink);
            shortcut.TargetPath = Files.App;
            shortcut.WorkingDirectory = Files.Home;
            shortcut.Save();
        }
        if (!File.Exists(Files.Config)) Files.Save(Files.Config,Library.Initial());
    }
    internal static bool Uninstall()
    {
        if (MessageBox.Show("移除分类菜单、恢复启用前的菜单外观，并删除收藏配置和已安装的程序？收藏的原文件会保留。",
            "完整卸载",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;
        using (var existing = ExistingMenus.Current()) existing.RestoreAll();
        ShellMenus.Current().Remove();
        var library = Files.Load();
        library.Enabled = false;
        Files.Save(Files.Config,library);
        var caches = File.Exists(CacheList) ? Files.Read<List<string>>(CacheList).Where(IsBundleCache).ToArray() : [];
        string ticket = Guid.NewGuid().ToString("N");
        File.WriteAllText(UninstallTicket,ticket);
        string cleanup = CleanupScript(ticket,caches);
        using var cleaner = Process.Start(new ProcessStartInfo(PowerShellPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", "-" },
            RedirectStandardInput = true,
            StandardInputEncoding = Encoding.ASCII
        }) ?? throw new IOException("未能启动清理程序，已保留收藏。");
        // Encode Unicode paths; using stdin also avoids the Windows command-line length limit.
        var payload = Convert.ToBase64String(Encoding.Unicode.GetBytes(cleanup));
        cleaner.StandardInput.WriteLine("& ([ScriptBlock]::Create([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" + payload + "'))))");
        cleaner.StandardInput.Close();
        return true;
    }
    static string CleanupScript(string ticket,string[] caches)
    {
        static string Quote(string path) => "'" + path.Replace("'","''") + "'";
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference='Stop'");
        script.AppendLine($"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue");
        script.AppendLine(@"$mutex=[Threading.Mutex]::new($false,'Local\RightMenuFavorites.4065F0FE')");
        script.AppendLine("try { try { $null=$mutex.WaitOne() } catch [Threading.AbandonedMutexException] {}");
        script.AppendLine($"if (![IO.File]::Exists({Quote(UninstallTicket)}) -or [IO.File]::ReadAllText({Quote(UninstallTicket)}) -ne '{ticket}') {{ return }}");
        script.AppendLine($"$app={Quote(Files.App)}");
        foreach (string cache in caches)
        {
            foreach (string dll in BundleDlls)
                script.AppendLine($"[IO.File]::Delete({Quote(Path.Combine(cache,dll))})");
            script.AppendLine($"$d={Quote(cache)}; if ([IO.Directory]::Exists($d) -and ![IO.Directory]::GetFileSystemEntries($d).Length) {{ [IO.Directory]::Delete($d) }}");
        }
        script.AppendLine("for ($i=0; $i -lt 6; $i++) { try { [IO.File]::Delete($app); break } catch [IO.IOException] { if ($i -eq 5) { throw }; Start-Sleep -Milliseconds 500 } }");
        script.AppendLine($"$link={Quote(StartLink)}; if ([IO.File]::Exists($link) -and (New-Object -ComObject WScript.Shell).CreateShortcut($link).TargetPath -eq $app) {{ [IO.File]::Delete($link) }}");
        foreach (string file in new[]{CacheList,Files.Config,ManageStatic,UninstallTicket})
            script.AppendLine($"[IO.File]::Delete({Quote(file)})");
        script.AppendLine($"[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree({Quote(UninstallKey)},$false)");
        script.AppendLine($"$d={Quote(Files.Home)}; if (![IO.Directory]::GetFileSystemEntries($d).Length) {{ [IO.Directory]::Delete($d) }}");
        script.AppendLine("} catch { Add-Type -AssemblyName System.Windows.Forms; [void][Windows.Forms.MessageBox]::Show(('卸载未完成，请关闭占用程序后重试。'+$_.Exception.Message),'右键菜单') } finally { $mutex.Dispose() }");
        return script.ToString();
    }
    static bool IsBundleCache(string dir)
    {
        return Directory.Exists(dir)
            && string.Equals(Path.GetDirectoryName(Path.GetFullPath(dir)),CacheRoot,StringComparison.OrdinalIgnoreCase)
            && (File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0;
    }
    static string PowerShellPath => Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe");
    internal static void Run(string[] args)
    {
        var data = Files.Load();
        if (!data.Enabled) throw new InvalidOperationException("分类菜单已移除。");
        var id = Guid.Parse(args[1]);
        var item = data.Categories.Append(data.SystemCategory).SelectMany(g=>g.Items).FirstOrDefault(x=>x.Id == id)
            ?? throw new FileNotFoundException("该收藏已被移除，请重新打开右键菜单。");
        if (!item.Terminal)
        {
            if (!File.Exists(item.Target) && !Directory.Exists(item.Target))
                throw new FileNotFoundException($"「{item.Name}」的位置已失效，可在管理器中重新定位。\n{item.Target}");
            if (item.Status() == "链接目标失效")
                throw new FileNotFoundException($"「{item.Name}」指向的位置已失效。请修复快捷方式，或在管理器中重新定位。\n{item.Target}");
            try { Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true }); }
            catch (Exception ex) { throw new IOException($"无法打开「{item.Name}」\n{item.Target}\n{ex.Message}",ex); }
            return;
        }
        if (args.Length != 4 || !args[3].EndsWith('|')) throw new ArgumentException("无法读取右键所在目录。");
        string path = args[3][..^1];
        string? cwd = args[2] switch {
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "object" => Directory.Exists(path) ? path : Path.GetDirectoryName(path), "file" => Path.GetDirectoryName(path), "folder" => path, _ => null
        };
        if (cwd == null || !Path.IsPathFullyQualified(cwd) || !Directory.Exists(cwd)) throw new DirectoryNotFoundException(cwd ?? path);
        if (item.Kind == ActionKind.Cmd && cwd.StartsWith(@"\\"))
            throw new IOException("CMD 无法将网络共享用作当前目录，请使用 PowerShell。");
        Process.Start(new ProcessStartInfo(item.Kind == ActionKind.Cmd ? Path.Combine(Environment.SystemDirectory,"cmd.exe") : PowerShellPath,
            item.Kind == ActionKind.Cmd ? "/d" : "-NoProfile") { WorkingDirectory = cwd, UseShellExecute = true });
    }
}


