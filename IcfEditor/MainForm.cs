using System.ComponentModel;

namespace IcfEditor;

public sealed class MainForm : Form
{
    private static string StartupDirectory => Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
    private IcfDocument doc = new();
    private string? currentPath;
    private readonly BindingList<IcfRecord> rows = new();
    private readonly DataGridView grid = new();
    private readonly TextBox systemId = new() { Width = 70, MaxLength = 4, Text = "SDEZ" };
    private readonly TextBox appId = new() { Width = 60, MaxLength = 3, Text = "ACA" };
    private readonly ToolStripStatusLabel status = new("就绪");

    public MainForm()
    {
        Text = "ICF Editor - Saya"; Width = 1100; Height = 580; MinimumSize = new(850, 420); StartPosition = FormStartPosition.CenterScreen;
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6), WrapContents = false };
        tools.Controls.AddRange(new Control[] { Btn("打开", Open), Btn("保存", Save), Btn("另存为", SaveAs), Space(), new Label { Text = "游戏 ID", AutoSize = true, Margin = new(8,8,3,0) }, systemId, new Label { Text = "系统 ID", AutoSize = true, Margin = new(8,8,3,0) }, appId, Space(), Btn("新增 Pack", (_,_) => Add(RecordKind.Pack)), Btn("新增 App", (_,_) => Add(RecordKind.App)), Btn("新增 Opt", (_,_) => Add(RecordKind.Opt)), Btn("新增补丁", (_,_) => Add(RecordKind.Patch)), Btn("删除选中", Delete), Btn("关于", About) });
        ConfigureGrid();
        var bar = new StatusStrip(); bar.Items.Add(status); bar.Items.Add(new ToolStripStatusLabel("Designed by Saya") { Spring = true, TextAlign = ContentAlignment.MiddleRight });
        Controls.Add(grid); Controls.Add(tools); Controls.Add(bar);
        grid.DataSource = rows;
        FormClosing += (_, e) => { grid.EndEdit(); };
    }

    private static Button Btn(string text, EventHandler action) { var b = new Button { Text = text, AutoSize = true, Height = 28, Margin = new Padding(3,1,3,1) }; b.Click += action; return b; }
    private static Control Space() => new Label { Width = 10 };
    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill; grid.AutoGenerateColumns = false; grid.AllowUserToAddRows = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = true;
        grid.RowHeadersVisible = false; grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells; grid.BackgroundColor = SystemColors.Window;
        grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "类型", DataPropertyName = nameof(IcfRecord.Kind), DataSource = Enum.GetValues<RecordKind>(), Width = 80 });
        AddText("版本 / Opt标识", nameof(IcfRecord.Version), 105); AddDate("日期时间", nameof(IcfRecord.Date)); AddText("依赖系统版本", nameof(IcfRecord.RequiredVersion), 110);
        AddText("来源版本（补丁）", nameof(IcfRecord.SourceVersion), 120); AddDate("来源日期（补丁）", nameof(IcfRecord.SourceDate)); AddText("来源依赖版本", nameof(IcfRecord.SourceRequiredVersion), 115);
        var preview = new DataGridViewTextBoxColumn { HeaderText = "生成的文件名预览", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260 };
        grid.Columns.Add(preview);
        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            bool isPatch = rows[e.RowIndex].Kind is RecordKind.Patch or RecordKind.InstalledPatch;
            if ((e.ColumnIndex is 4 or 5 or 6 && !isPatch) || (e.ColumnIndex == 3 && rows[e.RowIndex].Kind == RecordKind.Opt))
            {
                e.Value = ""; e.CellStyle.BackColor = SystemColors.Control; e.CellStyle.ForeColor = SystemColors.GrayText; e.FormattingApplied = true;
            }
            else if (e.ColumnIndex == grid.Columns.Count - 1)
            {
                e.Value = rows[e.RowIndex].FileName(systemId.Text, appId.Text); e.FormattingApplied = true;
            }
        };
        grid.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if ((e.ColumnIndex is 4 or 5 or 6 && rows[e.RowIndex].Kind is not (RecordKind.Patch or RecordKind.InstalledPatch)) || (e.ColumnIndex == 3 && rows[e.RowIndex].Kind == RecordKind.Opt)) e.Cancel = true;
        };
        grid.CellValueChanged += (_,_) => grid.Invalidate(); systemId.TextChanged += (_,_) => grid.Invalidate(); appId.TextChanged += (_,_) => grid.Invalidate();
        void AddText(string h, string p, int w) => grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h, DataPropertyName = p, Width = w });
        void AddDate(string h, string p) => grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h, DataPropertyName = p, Width = 145, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm:ss" } });
    }

    private void Open(object? s, EventArgs e)
    {
        using var d = new OpenFileDialog { Filter = "ICF 文件 (*.icf; ICF*)|*.icf;ICF*|无扩展名文件 (*)|*|所有文件 (*.*)|*.*", InitialDirectory = StartupDirectory };
        if (d.ShowDialog() != DialogResult.OK) return;
        try { doc = IcfDocument.Load(d.FileName); currentPath = d.FileName; LoadRows(); status.Text = $"已打开：{d.FileName}（{rows.Count} 条记录）"; Text = $"ICF Editor - {Path.GetFileName(d.FileName)} - Saya"; }
        catch (Exception ex) { Error(ex); }
    }
    private void LoadRows() { rows.Clear(); systemId.Text = doc.SystemId; appId.Text = doc.AppId; foreach (var x in doc.Records) rows.Add(x); }
    private void Save(object? s, EventArgs e) { if (currentPath is null) SaveAs(s, e); else SaveTo(currentPath); }
    private void SaveAs(object? s, EventArgs e)
    {
        using var d = new SaveFileDialog { Filter = "ICF 文件 (*.icf)|*.icf|无扩展名文件 (*)|*|所有文件 (*.*)|*.*", AddExtension = false, FileName = currentPath is null ? "output.icf" : Path.GetFileName(currentPath), InitialDirectory = currentPath is null ? StartupDirectory : Path.GetDirectoryName(currentPath) };
        if (d.ShowDialog() == DialogResult.OK && SaveTo(d.FileName)) { currentPath = d.FileName; Text = $"ICF Editor - {Path.GetFileName(d.FileName)} - Saya"; }
    }
    private bool SaveTo(string path)
    {
        try
        {
            grid.EndEdit(); doc.SystemId = systemId.Text.Trim().ToUpperInvariant(); doc.AppId = appId.Text.Trim().ToUpperInvariant(); doc.Records.Clear(); foreach (var x in rows) doc.Records.Add(x);
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            doc.Save(path); status.Text = $"保存成功：{path}（已自动生成 .bak 备份）"; return true;
        }
        catch (Exception ex) { Error(ex); return false; }
    }
    private void Add(RecordKind kind)
    {
        var last = rows.LastOrDefault(x => x.Kind is RecordKind.App or RecordKind.Patch or RecordKind.InstalledPatch);
        rows.Add(new IcfRecord { Kind = kind, Version = kind == RecordKind.Opt ? "A001" : last?.Version ?? "1.00.00", Date = DateTime.Now, RequiredVersion = kind == RecordKind.Opt ? "" : last?.RequiredVersion ?? "111.01.01", SourceVersion = last?.Version ?? "1.00.00", SourceDate = last?.Date ?? DateTime.Now, SourceRequiredVersion = last?.RequiredVersion ?? "111.01.01" });
        grid.CurrentCell = grid.Rows[^1].Cells[0];
    }
    private void Delete(object? s, EventArgs e) { foreach (DataGridViewRow r in grid.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(x => x.Index)) if (r.Index < rows.Count) rows.RemoveAt(r.Index); }
    private void Error(Exception ex) => MessageBox.Show(this, ex.Message, "ICF Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private void About(object? s, EventArgs e) => MessageBox.Show(this, "ICF Editor v1.0.0\n\nDesigned and developed by Saya\nCopyright © Saya 2026", "关于 ICF Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
