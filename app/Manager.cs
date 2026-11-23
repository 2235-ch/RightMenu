namespace RightMenu;

internal sealed class Manager : Form
{
    readonly System.ComponentModel.Container popupMenus = new();
    readonly Library data = Files.Load();
    readonly ShellMenus menus = ShellMenus.Current();
    readonly ListBox groups = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly ListView entries = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        HideSelection = false, MultiSelect = true, AllowDrop = true };
    readonly Label status = new() { AutoSize = true, Margin = new Padding(12,9,0,0) };
    readonly Button enable;
    readonly List<(Button Button, Func<bool> Available)> selectionActions = [];
    readonly Label heading = new() { AutoSize = true };
    readonly ComboBox scopeFilter = new() { DropDownStyle=ComboBoxStyle.DropDownList, Width=180 };
    string? Filter => scopeFilter.SelectedIndex>0 ? ExistingMenus.Scopes.Keys.ElementAt(scopeFilter.SelectedIndex-1) : null;
    ExistingEntry? Existing => entries.SelectedItems.Count==1 ? entries.SelectedItems[0].Tag as ExistingEntry : null;
    bool InSystem => Group == data.SystemCategory;
    bool rebuildingGroups;
    int revision;
    bool checking;
    bool closing;
    Category? Group => groups.SelectedItem as Category;
    Item? Selected => entries.SelectedItems.Count == 1 ? entries.SelectedItems[0].Tag as Item : null;
    public Manager()
    {
        Text = "右键菜单 · 收藏管理";
        Icon = new Icon(typeof(Manager), "右键菜单.ico");
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI",9);
        ClientSize = new Size(1040,580);
        MinimumSize = new Size(880,480);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new(SizeType.Absolute,250));
        layout.ColumnStyles.Add(new(SizeType.Percent,100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent,100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "分类", AutoSize = true },0,0);
        var header=Bar();
        scopeFilter.Items.Add("全部位置");
        scopeFilter.Items.AddRange(ExistingMenus.Scopes.Values.ToArray());
        scopeFilter.SelectedIndex=0;
        header.Controls.AddRange([scopeFilter,heading]);
        layout.Controls.Add(header,1,0);
        groups.FormattingEnabled = true;
        groups.Format += (_,e)=> {
            if (e.ListItem is Category g && g != data.SystemCategory && g.Name == "系统") e.Value = "系统（自定义分类）";
        };
        var left = Pane(groups,out var categoryTools);
        layout.Controls.Add(left,0,1);
        var right = Pane(entries,out var tools);
        layout.Controls.Add(right,1,1);
        Button(categoryTools,"新建",()=> {
            string? name = Ask("新建分类","");
            if (name == null) return;
            var cat = new Category { Name = name, Scope=Filter };
            data.Categories.Add(cat); Commit(cat);
        });
        Button(categoryTools,"改名",()=> {
            if (Group is not {} g || Ask("分类名称",g.Name) is not {} name) return;
            g.Name = name; Commit(g);
        },()=>Group != null && !InSystem);
        Button(categoryTools,"↑",()=>MoveGroup(-1),()=>groups.SelectedIndex > 1);
        Button(categoryTools,"↓",()=>MoveGroup(1),()=>Group != null && !InSystem && groups.SelectedIndex < groups.Items.Count-1);
        Button(categoryTools,"移除",()=> {
            if (Group is not {} g || MessageBox.Show($"移除「{g.Name}」及其中 {g.Items.Count} 个引用？","移除分类",
                MessageBoxButtons.OKCancel,MessageBoxIcon.Question) != DialogResult.OK) return;
            int index = data.Categories.IndexOf(g);
            data.Categories.Remove(g);
            Commit(data.Categories.ElementAtOrDefault(Math.Min(index,data.Categories.Count-1)));
        },()=>Group != null && !InSystem);
        void AddLocationButton(Control parent,string label,bool wholeGroup,Func<bool> available)
        {
            var menu=new ContextMenuStrip(popupMenus);
            void Choice(string text,string? scope)
            {
                var choice=new ToolStripMenuItem(text) { Tag=scope };
                choice.Click+=(_,_)=>Guard(()=>{
                    if (Group is not {} group) return;
                    if (wholeGroup) group.Scope=scope;
                    else foreach (var item in entries.SelectedItems.Cast<ListViewItem>().Select(row=>row.Tag).OfType<Item>())
                        item.Scope=scope;
                    Commit(group);
                });
                menu.Items.Add(choice);
            }
            Choice(wholeGroup ? "默认（兼容旧版）" : "跟随分类",null);
            Choice("全部位置","all");
            foreach (var pair in ExistingMenus.Scopes) Choice(pair.Value,pair.Key);
            menu.Opening+=(_,e)=>{
                if (Group is not {} group || !available()) { e.Cancel=true; return; }
                var selected=entries.SelectedItems.Cast<ListViewItem>().Select(row=>row.Tag).OfType<Item>().ToArray();
                foreach (ToolStripMenuItem choice in menu.Items)
                    choice.Checked=wholeGroup ? Equals(group.Scope,choice.Tag) : selected.All(item=>Equals(item.Scope,choice.Tag));
            };
            var button=Button(parent,label,()=>{},available);
            button.Click+=(_,_)=>Guard(()=>menu.Show(button,new Point(0,button.Height)));
        }
        AddLocationButton(categoryTools,"分类位置",true,()=>Group!=null);
        AddLocationButton(tools,"条目位置",false,()=>entries.SelectedItems.Count>0 &&
            entries.SelectedItems.Cast<ListViewItem>().All(r=>r.Tag is Item));
        foreach (var (name,width) in new[] { ("名称",150),("类型",78),("目标位置",315),("状态",120),("菜单位置",135) })
            entries.Columns.Add(name,width);
        Button(tools,"添加文件",()=> {
            if (!HasGroup()) return;
            using var picker = new OpenFileDialog { Multiselect = true, DereferenceLinks = false };
            if (picker.ShowDialog(this) == DialogResult.OK) Add(picker.FileNames);
        },()=>Group != null);
        Button(tools,"添加文件夹",()=> {
            if (!HasGroup()) return;
            using var picker = new FolderBrowserDialog();
            if (picker.ShowDialog(this) == DialogResult.OK) Add([picker.SelectedPath]);
        },()=>Group != null);
        var terminal = new ContextMenuStrip(popupMenus);
        foreach (var (kind,name) in new[]{ (ActionKind.Cmd,"在此处打开 CMD"),(ActionKind.PowerShell,"在此处打开 PowerShell") })
            terminal.Items.Add(name,null,(_,_)=>Guard(()=> {
                if (!HasGroup()) return;
                var item = new Item { Kind = kind, Name = name, Scope=Filter };
                Group!.Items.Add(item); Commit(Group,item);
            }));
        var addTool = Button(tools,"添加终端",()=>{},()=>Group != null);
        addTool.Click += (_,_)=>terminal.Show(addTool,new Point(0,addTool.Height));
        Button(tools,"改名",RenameItem,()=>Selected != null || Existing?.Editable==true);
        Button(tools,"恢复名称",()=>EditExisting(e=>e.Rename(Existing!,null)),()=>Existing?.Renamed==true);
        Button(tools,"恢复显示",()=>EditExisting(e=>e.SetHidden(Existing!,false)),()=>Existing is {Hidden:true,Editable:true});
        Button(tools,"重新定位",Relocate,()=>Selected is { Terminal: false });
        Button(tools,"↑",()=>MoveItem(-1),()=>Selected != null && Group!.Items.IndexOf(Selected)>0);
        Button(tools,"↓",()=>MoveItem(1),()=>Selected != null && Group!.Items.IndexOf(Selected) < Group.Items.Count-1);
        Button(tools,"移除",()=> {
            if (Existing is {} existing)
            {
                EditExisting(e=>e.SetHidden(existing,true));
                return;
            }
            var selected = entries.SelectedItems.Cast<ListViewItem>().Select(row=>row.Tag).OfType<Item>().ToList();
            if (Group == null || selected.Count == 0) return;
            foreach (var item in selected) Group.Items.Remove(item);
            Commit(Group);
        },()=>Existing is {Editable:true,Hidden:false} || (entries.SelectedItems.Count>0 && entries.SelectedItems.Cast<ListViewItem>().All(r=>r.Tag is Item)));
        var takeover=Button(tools,"备份并接管全部静态命令",()=> {
            using var catalog=ExistingMenus.Current();
            ExistingMenuWindow.TakeoverDialog(this,catalog,false,()=>ShowEntries(null));
        });
        groups.SelectedIndexChanged+=(_,_)=>takeover.Visible=InSystem;
        var refreshDesktop = Button(tools,"刷新已有菜单",()=>ShowEntries(null));
        groups.SelectedIndexChanged += (_,_)=>refreshDesktop.Visible = InSystem;
        var copyMenu = new ContextMenuStrip(popupMenus);
        var copy = copyMenu.Items.Add("");
        copy.Click += (_,_)=>Guard(()=> {
            if (copy.Tag is string value) Clipboard.SetText(value);
        });
        entries.MouseUp += (_,e)=> {
            if (e.Button != MouseButtons.Right) return;
            var hit = entries.HitTest(e.Location);
            if (hit.Item?.Tag is not Item item || hit.SubItem == null) return;
            int column = hit.Item.SubItems.IndexOf(hit.SubItem);
            if (column != 0 && (column != 2 || item.Terminal)) return;
            copy.Text = column == 0 ? "复制名称" : "复制位置";
            copy.Tag = column == 0 ? item.Name : item.Target;
            copyMenu.Show(entries,e.Location);
        };
        entries.MouseDoubleClick += (_,e)=> { if (e.Button == MouseButtons.Left) Guard(RenameItem); };
        groups.SelectedIndexChanged += (_,_)=> { if (!rebuildingGroups) ShowEntries(null); };
        entries.SelectedIndexChanged += (_,_)=>UpdateActions();
        entries.SizeChanged += (_,_)=> {
            int fixedWidth = entries.Columns[0].Width + entries.Columns[1].Width + entries.Columns[3].Width + entries.Columns[4].Width;
            entries.Columns[2].Width = Math.Max(100, entries.ClientSize.Width-fixedWidth-SystemInformation.VerticalScrollBarWidth-2);
        };
        scopeFilter.SelectedIndexChanged+=(_,_)=>ShowEntries(null);
        Shown += (_,_)=>CheckLocations();
        FormClosed += (_,_)=>closing=true;
        entries.DragEnter += (_,e)=>e.Effect = Group != null && e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy : DragDropEffects.None;
        entries.DragDrop += (_,e)=>Guard(()=> { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) Add(paths); });
        var footer = Bar();
        layout.Controls.Add(footer,0,2); layout.SetColumnSpan(footer,2);
        Button(footer,"管理现有右键菜单",()=> { using var window = new ExistingMenuWindow(); window.ShowDialog(this); if (InSystem) ShowEntries(null); });
        enable = Button(footer,"启用分类菜单",EnableMenus);
        Button(footer,"移除菜单并恢复外观",()=> {
            menus.Remove(); data.Enabled = false; Files.Save(Files.Config,data); State();
        });
        Button(footer,"完整卸载",()=> { if (Program.Uninstall()) Close(); });
        footer.Controls.Add(status);
        ShowGroups(data.SystemCategory,null);
        State();
        if (data.Enabled && File.Exists(Files.Receipt) && menus.HasLegacyMenus())
            Guard(()=>menus.Apply(data));
        State();
        if (File.Exists(Files.Receipt) != data.Enabled) status.Text = "上次更改中断，可重新启用或移除菜单。";
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closing=true;
            popupMenus.Dispose();
        }
        base.Dispose(disposing);
    }
    static FlowLayoutPanel Bar() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
    static Control Pane(Control content,out FlowLayoutPanel buttons)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0,4,8,10) };
        panel.RowStyles.Add(new(SizeType.Percent,100)); panel.RowStyles.Add(new(SizeType.AutoSize));
        buttons = Bar(); panel.Controls.Add(content,0,0); panel.Controls.Add(buttons,0,1);
        return panel;
    }
    void Guard(Action work)
    {
        try { work(); }
        catch (Exception ex) { status.Text = "操作未完成"; MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Warning); }
    }
    Button Button(Control parent,string label,Action action,Func<bool>? available = null)
    {
        var b = new Button { Text = label, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0,5,4,0), Padding = new Padding(4,1,4,1) };
        if (available != null) selectionActions.Add((b,available));
        b.Click += (_,_)=>Guard(action); parent.Controls.Add(b); return b;
    }
    bool HasGroup()
    {
        if (Group != null) return true;
        status.Text = "请先新建并选择分类。"; return false;
    }
    void EnableMenus()
    {
        bool wasEnabled = data.Enabled;
        try { menus.Apply(data); data.Enabled = true; Files.Save(Files.Config,data); State(); }
        catch
        {
            if (!wasEnabled) { menus.Remove(); data.Enabled = false; }
            throw;
        }
    }
    void State()
    {
        enable.Text = data.Enabled ? "重新同步菜单" : "启用分类菜单";
        status.Text = data.Enabled ? "已启用 · 关闭窗口后仍可使用" : "分类菜单未启用";
    }
    void Commit(Category? selected,Item? item = null)
    {
        Files.Save(Files.Config,data);
        ShowGroups(selected,item);
        State();
        if (!data.Enabled) return;
        try { menus.Apply(data); }
        catch (Exception ex) { throw new IOException("收藏已保存，菜单待修复。请点「重新同步菜单」。\n" + ex.Message,ex); }
    }
    void ShowGroups(Category? selected,Item? item)
    {
        rebuildingGroups = true;
        groups.BeginUpdate(); groups.Items.Clear(); groups.Items.Add(data.SystemCategory); groups.Items.AddRange(data.Categories.ToArray());
        groups.SelectedItem = selected ?? data.SystemCategory; groups.EndUpdate(); rebuildingGroups = false; ShowEntries(item);
    }
    void ShowEntries(Item? select)
    {
        revision++;
        entries.BeginUpdate(); entries.Items.Clear();
        if (Group != null) foreach (var item in Group.Items)
        {
            if (Filter is {} filter && !ShellMenus.Applies(Group,item,filter)) continue;
            string state = item.Terminal ? "内置" : "检查中";
            var row = new ListViewItem([item.Name,item.TypeName,item.Terminal ? "右键所在目录" : item.Target,state,item.Scope switch { null=>"跟随分类", "all"=>"全部位置", var key=>ExistingMenus.Scopes.GetValueOrDefault(key,key) }]) { Tag = item };
            entries.Items.Add(row); row.Selected = item == select;
            if (row.Selected) row.EnsureVisible();
        }
        string groupScope=Group?.Scope switch { null => "旧版默认", "all" => "全部位置", var key => ExistingMenus.Scopes.GetValueOrDefault(key,key) };
        heading.Text=(InSystem ? "系统" : "收藏条目")+" · 分类位置："+groupScope;
        if (InSystem)
        {
            try
            {
                using var catalog=ExistingMenus.Current();
                var registered=catalog.Read();
                foreach (var row in registered.Where(r=>Filter==null || r.Scope==Filter))
                    entries.Items.Add(new ListViewItem([row.Name,"已有菜单",row.Key,row.State,ExistingMenus.Scopes[row.Scope]]) { Tag=row });
                if (Filter is null or "DesktopBackground")
                {
                    var desktopNames=registered.Where(r=>r.Scope is "DesktopBackground" or @"Directory\Background")
                        .Select(r=>r.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
                    foreach (var row in DesktopMenu.Read().Where(r=>!desktopNames.Contains(System.Text.RegularExpressions.Regex.Replace(r.Name,@"\([A-Za-z]\)$","").TrimEnd()) && !desktopNames.Contains(r.Name)))
                        entries.Items.Add(new ListViewItem([row.Name,"桌面实况","桌面空白处",row.Enabled ? "资源管理器提供 · 只读" : "当前不可用"]));
                }
            }
            catch (Exception ex)
            {
                entries.Items.Add(new ListViewItem(["读取桌面菜单失败","","可点击「刷新已有菜单」重试",ex.Message]));
            }
        }
        entries.EndUpdate();
        UpdateActions();
        if (Visible) CheckLocations();
    }
    void UpdateActions()
    {
        foreach (var action in selectionActions) action.Button.Enabled = action.Available();
    }
    async void CheckLocations()
    {
        if (checking || closing || IsDisposed) return;
        checking = true;
        try
        {
            int version;
            do
            {
                version = revision;
                var rows = entries.Items.Cast<ListViewItem>().ToArray();
                foreach (var row in rows)
                {
                    if (row.Tag is not Item item || item.Terminal) continue;
                    var snapshot = new Item { Kind = item.Kind, Target = item.Target };
                    string result;
                    try { result = await Task.Run(snapshot.Status); }
                    catch (Exception) { result = "待检查"; }
                    if (closing || IsDisposed) return;
                    if (version != revision) break;
                    row.SubItems[3].Text = result;
                }
            } while (version != revision);
        }
        finally { checking = false; }
    }
    void Add(string[] paths)
    {
        if (!HasGroup()) return;
        int start=Group!.Items.Count;
        int added = Library.Add(Group,paths);
        foreach (var item in Group.Items.Skip(start)) item.Scope=Filter;
        Commit(Group); status.Text += $" · 添加 {added} 项，跳过 {paths.Length-added} 项";
    }
    void EditExisting(Action<ExistingMenus> change)
    {
        using var catalog=ExistingMenus.Current();
        change(catalog);
        ShowEntries(null);
    }
    void RenameItem()
    {
        if (Existing is {Editable:true} row)
        {
            if (Ask("已有菜单名称",row.Name) is {} alias) EditExisting(e=>e.Rename(row,alias));
            return;
        }
        if (Selected is not {} item || Ask("条目名称",item.Name) is not {} name) return;
        item.Name = name; Commit(Group,item);
    }
    void Relocate()
    {
        if (Selected is not {} item || item.Terminal) return;
        string path;
        if (item.Kind == ActionKind.Folder)
        {
            using var picker = new FolderBrowserDialog();
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            path = picker.SelectedPath;
        }
        else
        {
            using var picker = new OpenFileDialog { DereferenceLinks = false };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            path = picker.FileName;
        }
        if (Group!.Items.Any(x=>x!=item && string.Equals(x.Target,path,StringComparison.OrdinalIgnoreCase)))
            throw new IOException("该位置已在本分类中。");
        item.Target = path; Commit(Group,item);
    }
    void MoveGroup(int delta)
    {
        if (Group is not {} group) return;
        int i = data.Categories.IndexOf(group), j = i + delta;
        if (j < 0 || j >= data.Categories.Count) return;
        data.Categories.RemoveAt(i); data.Categories.Insert(j,group); Commit(group);
    }
    void MoveItem(int delta)
    {
        if (Selected is not {} item || Group is not {} g) return;
        int i = g.Items.IndexOf(item), j = i + delta;
        if (j < 0 || j >= g.Items.Count) return;
        g.Items.RemoveAt(i); g.Items.Insert(j,item); Commit(g,item);
    }
    string? Ask(string title,string initial) => Ask(this,title,initial);
    internal static string? Ask(Form owner,string title,string initial)
    {
        using var dialog = new Form { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScaleMode = AutoScaleMode.Font, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, Font = owner.Font };
        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        var input = new TextBox { Text = initial, MinimumSize = new Size(310,0), Dock = DockStyle.Fill };
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right, WrapContents = false, Margin = new Padding(0,6,0,0) };
        var ok = new Button { Text = "确定", AutoSize = true, Padding = new Padding(6,3,6,3), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", AutoSize = true, Padding = ok.Padding, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([ok,cancel]);
        layout.Controls.Add(input,0,0); layout.Controls.Add(actions,0,1);
        dialog.Controls.Add(layout); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        input.SelectAll();
        return dialog.ShowDialog(owner) == DialogResult.OK && input.Text.Trim().Length > 0 ? input.Text.Trim() : null;
    }
}

