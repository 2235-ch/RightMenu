namespace RightMenu;

internal sealed class ExistingMenuWindow : Form
{
    readonly ExistingMenus catalog = ExistingMenus.Current();
    readonly ListView list = new() { Dock=DockStyle.Fill, View=View.Details, FullRowSelect=true, MultiSelect=false, HideSelection=false };
    readonly ComboBox scope = new() { DropDownStyle=ComboBoxStyle.DropDownList, Width=180 };
    readonly TextBox filter = new() { PlaceholderText="搜索名称或注册项，例如 Git", Width=235 };
    readonly TextBox details = new() { Dock=DockStyle.Fill, Multiline=true, ReadOnly=true, Height=60, ScrollBars=ScrollBars.Vertical };
    readonly Button toggle = new() { Text="从右键菜单移除",AutoSize=true };
    readonly Button rename=new() { Text="改名",AutoSize=true };
    readonly Button resetName=new() { Text="恢复名称",AutoSize=true };
    readonly Label count = new() { AutoSize=true, Anchor=AnchorStyles.Left };
    List<ExistingEntry> rows = [];
    ExistingEntry? Selected => list.SelectedItems.Count==1 ? list.SelectedItems[0].Tag as ExistingEntry : null;
    internal ExistingMenuWindow()
    {
        Text="管理现有右键菜单"; Icon=new Icon(typeof(Manager),"右键菜单.ico");
        StartPosition=FormStartPosition.CenterParent; Font=new Font("Microsoft YaHei UI",9);
        ClientSize=new Size(850,520); MinimumSize=new Size(670,420);
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=5, Padding=new Padding(12) };
        foreach (var style in new[]{SizeType.AutoSize,SizeType.Percent,SizeType.AutoSize,SizeType.AutoSize,SizeType.AutoSize})
            layout.RowStyles.Add(new RowStyle(style,style==SizeType.Percent ? 100 : 0));
        Controls.Add(layout);
        var top=new FlowLayoutPanel { Dock=DockStyle.Fill, AutoSize=true };
        var refresh=new Button { Text="刷新",AutoSize=true };
        scope.Items.Add("全部位置");
        scope.Items.AddRange(ExistingMenus.Scopes.Values.ToArray()); scope.SelectedIndex=0;
        var take=new Button { Text="备份并接管全部静态命令",AutoSize=true };
        var release=new Button { Text="撤回接管",AutoSize=true };
        take.Click+=(_,_)=>TakeoverDialog(this,catalog,false,()=>Reload());
        release.Click+=(_,_)=>TakeoverDialog(this,catalog,true,()=>Reload());
        top.Controls.AddRange([scope,filter,refresh,take,release]); layout.Controls.Add(top,0,0);
        foreach (var (name,width) in new[]{("条目名称",255),("注册位置",180),("状态",180)})
            list.Columns.Add(name,width);
        layout.Controls.Add(list,0,1); layout.Controls.Add(details,0,2);
        var actions=new FlowLayoutPanel { Dock=DockStyle.Fill, AutoSize=true };
        actions.Controls.AddRange([rename,resetName,toggle,count]); layout.Controls.Add(actions,0,3);
        layout.Controls.Add(new Label { AutoSize=true, Dock=DockStyle.Fill,
            Text="更改作用于当前用户。桌面可能同时使用文件夹空白处的命令。查看、排序、刷新等由资源管理器直接生成。" },0,4);
        refresh.Click+=(_,_)=>Reload();
        filter.TextChanged+=(_,_)=>Display(); scope.SelectedIndexChanged+=(_,_)=>Display();
        list.SelectedIndexChanged+=(_,_)=>SelectionChanged();
        toggle.Click+=(_,_)=>{
            if (Selected is not {} row || !row.Editable) return;
            try { catalog.SetHidden(row,!row.Hidden); }
            catch (Exception ex) { MessageBox.Show(this,ex.Message,Text); }
            Reload(row);
        };
        rename.Click+=(_,_)=>ChangeName(false);
        resetName.Click+=(_,_)=>ChangeName(true);
        Shown+=(_,_)=>Reload();
    }
    internal static void TakeoverDialog(Form owner,ExistingMenus catalog,bool release,Action reload)
    {
        try
        {
            if (release) { Files.Save(Program.ManageStatic,false); catalog.ReleaseAll(); }
            else
            {
                var result=catalog.TakeOver(); Files.Save(Program.ManageStatic,true);
                MessageBox.Show(owner,result.Count==0 ? "没有新的可接管命令。" : $"已接管 {result.Count} 项。备份保存在：\n{result.File}","右键菜单");
            }
        }
        catch(Exception ex){MessageBox.Show(owner,ex.Message,"右键菜单");}
        finally { reload(); }
    }
    void ChangeName(bool restore)
    {
        if (Selected is not {} row) return;
        var name=restore ? null : Manager.Ask(this,"已有菜单名称",row.Name);
        if (!restore && name==null) return;
        try { catalog.Rename(row,name); }
        catch(Exception ex) { MessageBox.Show(this,ex.Message,Text); }
        Reload(row);
    }
    void Reload(ExistingEntry? selected=null)
    {
        try { rows=catalog.Read(); Display(selected); }
        catch (Exception ex) { rows.Clear(); Display(); details.Text="读取失败："+ex.Message; }
    }
    void Display(ExistingEntry? selected=null)
    {
        list.BeginUpdate(); list.Items.Clear();
        foreach (var entry in rows)
        {
            string location=ExistingMenus.Scopes[entry.Scope];
            if (scope.SelectedIndex>0 && !Equals(scope.SelectedItem,location)) continue;
            if (!(entry.Name+" "+entry.Detail).Contains(filter.Text.Trim(),StringComparison.OrdinalIgnoreCase)) continue;
            var item=new ListViewItem([entry.Name,location,entry.State]) { Tag=entry };
            list.Items.Add(item);
            if (selected?.Key==entry.Key && selected.Scope==entry.Scope && selected.Editable==entry.Editable)
            { item.Selected=true; item.EnsureVisible(); }
        }
        list.EndUpdate(); count.Text=$"共 {list.Items.Count} 项"; SelectionChanged();
    }
    void SelectionChanged()
    {
        var row=Selected;
        details.Text=row?.Detail ?? "";
        toggle.Text=row?.Hidden==true ? "恢复显示" : "从右键菜单移除";
        rename.Enabled=row?.Editable==true; resetName.Enabled=row?.Renamed==true;
        toggle.Enabled=row?.Editable==true;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) catalog.Dispose();
        base.Dispose(disposing);
    }
}

