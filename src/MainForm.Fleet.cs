using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UwfTeacher
{
    public partial class MainForm
    {
        private List<Machine> _fleet = new List<Machine>();
        private AppSettings _settings = new AppSettings();
        private Dictionary<string, Machine> _byName = new Dictionary<string, Machine>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, DataGridViewRow> _rowsByMachine = new Dictionary<string, DataGridViewRow>(StringComparer.OrdinalIgnoreCase);

        private DataGridView _grid;
        private TextBox _search;
        private CheckBox _onlyIssues;
        private CheckBox _selectAll;
        private Label _fleetStatus;
        private TextBox _detail;
        private Label _detailTitle;
        private StatCard _fsTotal, _fsProtected, _fsMaint, _fsError, _fsPending;
        private Label _selCountLabel;
        private Button _btnDiscover, _btnRefreshFleet, _btnUwfFleet, _btnServiceFleet, _btnRestartFleet, _btnShutdownFleet, _btnCancelFleet, _btnMore;
        private FleetRunner _runner;
        private bool _localLoaded;
        private Label _emptyHint;

        // ------------------------------------------------------------------ page

        private Panel BuildFleetPage()
        {
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Ui.Bg;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.BackColor = Ui.Bg;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            page.Controls.Add(root);

            // 统计卡
            TableLayoutPanel stats = new TableLayoutPanel();
            stats.Dock = DockStyle.Fill;
            stats.ColumnCount = 5;
            stats.RowCount = 1;
            stats.BackColor = Color.Transparent;
            stats.Margin = new Padding(Ui.PagePad, 16, Ui.PagePad, 10);
            for (int i = 0; i < 5; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
            stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _fsTotal = MakeStat("计算机总数", "0", "台");
            _fsProtected = MakeStat("保护中", "0", "台");
            _fsMaint = MakeStat("暂停还原", "0", "台");
            _fsPending = MakeStat("待重启", "0", "台");
            _fsError = MakeStat("离线 / 失败", "0", "台");
            _fsTotal.ValueColor = Ui.Indigo;
            _fsProtected.ValueColor = Ui.Green;
            _fsMaint.ValueColor = Ui.Amber;
            _fsPending.ValueColor = Ui.Accent;
            _fsError.ValueColor = Ui.Red;
            stats.Controls.Add(_fsTotal, 0, 0);
            stats.Controls.Add(_fsProtected, 1, 0);
            stats.Controls.Add(_fsMaint, 2, 0);
            stats.Controls.Add(_fsPending, 3, 0);
            stats.Controls.Add(_fsError, 4, 0);
            root.Controls.Add(stats, 0, 0);

            // 工具栏（两行：操作 / 筛选）
            Panel barHost = new Panel();
            barHost.Dock = DockStyle.Fill;
            barHost.BackColor = Color.Transparent;
            barHost.Margin = new Padding(Ui.PagePad, 0, Ui.PagePad, 8);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = 32;
            bar.FlowDirection = FlowDirection.LeftToRight;
            bar.WrapContents = false;
            bar.BackColor = Color.Transparent;

            _btnDiscover = Ui.B("搜索学生机");
            _btnDiscover.Width = 108;
            _btnDiscover.Height = 32;
            _btnDiscover.Margin = new Padding(0, 0, 0, 0);
            _btnDiscover.Click += delegate { OpenDiscovery(); };

            _btnRefreshFleet = Ui.B("刷新全部状态", true);
            _btnRefreshFleet.Width = 120;
            _btnRefreshFleet.Height = 32;
            _btnRefreshFleet.Margin = new Padding(8, 0, 0, 0);
            _btnRefreshFleet.Click += delegate { RefreshFleet(false); };

            _btnUwfFleet = Ui.B("UWF ▼", true);
            _btnUwfFleet.Width = 92;
            _btnUwfFleet.Height = 32;
            _btnUwfFleet.Margin = new Padding(16, 0, 0, 0);
            _btnUwfFleet.Click += delegate { ShowUwfMenu(); };

            _btnServiceFleet = Ui.B("维修模式 ▼");
            _btnServiceFleet.Width = 104;
            _btnServiceFleet.Height = 32;
            _btnServiceFleet.Margin = new Padding(8, 0, 0, 0);
            _btnServiceFleet.Click += delegate { ShowServicingMenu(); };

            _btnRestartFleet = Ui.B("重启");
            _btnRestartFleet.Width = 68;
            _btnRestartFleet.Height = 32;
            _btnRestartFleet.Margin = new Padding(16, 0, 0, 0);
            _btnRestartFleet.Click += delegate { StartAction("restart", "重启"); };

            _btnShutdownFleet = Ui.B("关机");
            _btnShutdownFleet.Width = 68;
            _btnShutdownFleet.Height = 32;
            _btnShutdownFleet.Margin = new Padding(8, 0, 0, 0);
            _btnShutdownFleet.Click += delegate { StartAction("shutdown", "关机"); };

            _btnMore = Ui.B("更多操作");
            _btnMore.Width = 100;
            _btnMore.Height = 32;
            _btnMore.Margin = new Padding(8, 0, 0, 0);
            _btnMore.Click += delegate { ShowMoreMenu(); };

            _btnCancelFleet = Ui.Danger("取消");
            _btnCancelFleet.Width = 68;
            _btnCancelFleet.Height = 32;
            _btnCancelFleet.Margin = new Padding(8, 0, 0, 0);
            _btnCancelFleet.Visible = false;
            _btnCancelFleet.Click += delegate { if (_runner != null) _runner.Cancel(); };
            bar.Controls.Add(_btnDiscover);
            bar.Controls.Add(_btnRefreshFleet);
            bar.Controls.Add(_btnUwfFleet);
            bar.Controls.Add(_btnServiceFleet);
            bar.Controls.Add(_btnRestartFleet);
            bar.Controls.Add(_btnShutdownFleet);
            bar.Controls.Add(_btnMore);
            bar.Controls.Add(_btnCancelFleet);

            FlowLayoutPanel filterBar = new FlowLayoutPanel();
            filterBar.Dock = DockStyle.Top;
            filterBar.Height = 32;
            filterBar.FlowDirection = FlowDirection.LeftToRight;
            filterBar.WrapContents = false;
            filterBar.BackColor = Color.Transparent;

            _selCountLabel = Ui.L("已勾选：0 台", Ui.F9, Ui.Muted);
            _selCountLabel.AutoSize = true;
            _selCountLabel.Margin = new Padding(0, 7, 0, 0);

            _search = new TextBox();
            Ui.StyleInput(_search);
            _search.Width = 168;
            _search.Height = 28;
            _search.Margin = new Padding(12, 1, 0, 0);
            _search.TextChanged += delegate { ApplyFilter(); };

            _onlyIssues = new CheckBox();
            _onlyIssues.Text = "只看异常";
            _onlyIssues.Font = Ui.F9;
            _onlyIssues.AutoSize = true;
            _onlyIssues.Margin = new Padding(12, 7, 0, 0);
            _onlyIssues.CheckedChanged += delegate { ApplyFilter(); };

            Label searchHint = Ui.L("搜索：", Ui.F9, Ui.Muted);
            searchHint.AutoSize = true;
            searchHint.Margin = new Padding(4, 7, 0, 0);

            _selectAll = new CheckBox();
            _selectAll.Text = "全选";
            _selectAll.Font = Ui.F9;
            _selectAll.AutoSize = true;
            _selectAll.Margin = new Padding(0, 5, 14, 0);
            _selectAll.CheckedChanged += delegate
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.Visible) row.Cells["sel"].Value = _selectAll.Checked;
                }
                UpdateSelectionCount();
            };

            filterBar.Controls.Add(_selectAll);
            filterBar.Controls.Add(_selCountLabel);
            filterBar.Controls.Add(searchHint);
            filterBar.Controls.Add(_search);
            filterBar.Controls.Add(_onlyIssues);

            barHost.Controls.Add(filterBar);
            barHost.Controls.Add(bar);
            root.Controls.Add(barHost, 0, 1);

            // 表格
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.Margin = new Padding(Ui.PagePad, 0, Ui.PagePad, 8);
            Ui.StyleGrid(_grid);
            _grid.ReadOnly = false;
            _grid.MultiSelect = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            DataGridViewCheckBoxColumn colSel = new DataGridViewCheckBoxColumn();
            colSel.HeaderText = "";
            colSel.Name = "sel";
            colSel.Width = 36;
            colSel.FillWeight = 4;
            colSel.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            colSel.Resizable = DataGridViewTriState.False;
            colSel.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(colSel);
            _grid.Columns.Add(TextCol("name", "计算机", 88, true));
            _grid.Columns.Add(TextCol("group", "分组", 52, true));
            _grid.Columns.Add(TextCol("status", "状态", 82, true));
            _grid.Columns.Add(TextCol("protect", "保护", 78, true));
            _grid.Columns.Add(TextCol("maint", "暂停还原", 66, true));
            _grid.Columns.Add(TextCol("pending", "待重启", 56, true));
            _grid.Columns.Add(TextCol("usage", "缓存", 76, true));
            _grid.Columns.Add(TextCol("transport", "通道", 46, true));
            _grid.Columns.Add(TextCol("last", "上次操作", 100, true));
            _grid.Columns.Add(TextCol("note", "说明", 160, true));

            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                UpdateSelectionCount();
            };
            _grid.SelectionChanged += delegate { ShowDetailForSelected(); };
            // 右键先选中鼠标所指的那一行，否则菜单会作用在别的机器上
            _grid.CellMouseDown += delegate(object s, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
                int col = e.ColumnIndex >= 0 ? e.ColumnIndex : 1;
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[Math.Min(col, _grid.Columns.Count - 1)];
            };
            _grid.ContextMenuStrip = BuildGridMenu();
            _grid.ShowCellToolTips = true;
            _grid.CellToolTipTextNeeded += delegate(object s, DataGridViewCellToolTipTextNeededEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                string col = _grid.Columns[e.ColumnIndex].Name;
                Machine m;
                if ((col == "note" || col == "status" || col == "protect") &&
                    _byName.TryGetValue(Convert.ToString(_grid.Rows[e.RowIndex].Tag), out m))
                {
                    if (col == "note")
                    {
                        string note = FleetView.NoteText(m);
                        if (note.Length > 0)
                        {
                            e.ToolTipText = note;
                            return;
                        }
                    }
                    else if (col == "status")
                    {
                        e.ToolTipText = FleetView.StatusText(m);
                        return;
                    }
                    else
                    {
                        e.ToolTipText = FleetView.ProtectText(m.State);
                        return;
                    }
                }
                object v = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
                e.ToolTipText = v == null ? "" : Convert.ToString(v);
            };
            _grid.Resize += delegate { Ui.ApplyRegion(_grid, 10); };
            _grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                // 勾选那一列双击等于“打勾”，别顺手弹出资源管理器
                if (e.ColumnIndex >= 0 &&
                    string.Equals(_grid.Columns[e.ColumnIndex].Name, "sel", StringComparison.Ordinal)) return;
                OpenMachineFolder(MachineAt(e.RowIndex));
            };
            _grid.CellPainting += delegate(object s, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics == null) return;
                if (!string.Equals(_grid.Columns[e.ColumnIndex].Name, "status", StringComparison.Ordinal)) return;
                e.PaintBackground(e.CellBounds, true);

                Machine m = null;
                string name = Convert.ToString(_grid.Rows[e.RowIndex].Tag);
                if (name != null) _byName.TryGetValue(name, out m);
                Color dot = m == null ? Ui.MutedLight : FleetView.ColorFor(m);
                int cy = e.CellBounds.Top + e.CellBounds.Height / 2;
                using (SolidBrush b = new SolidBrush(dot))
                {
                    e.Graphics.FillEllipse(b, e.CellBounds.Left + 9, cy - 4, 8, 8);
                }
                Color fg = (e.State & DataGridViewElementStates.Selected) != 0
                    ? e.CellStyle.SelectionForeColor
                    : (e.CellStyle.ForeColor.IsEmpty ? Ui.TextColor : e.CellStyle.ForeColor);
                Rectangle text = new Rectangle(e.CellBounds.Left + 23, e.CellBounds.Top,
                                               Math.Max(0, e.CellBounds.Width - 27), e.CellBounds.Height);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(e.Value) ?? "", e.CellStyle.Font, text, fg,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                                      TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
                e.Handled = true;
            };
            RoundedPanel tableCard = new RoundedPanel();
            tableCard.Dock = DockStyle.Fill;
            tableCard.Margin = new Padding(Ui.PagePad, 0, Ui.PagePad, 10);
            tableCard.Padding = new Padding(1);
            _grid.Margin = new Padding(0);
            tableCard.Controls.Add(_grid);
            root.Controls.Add(tableCard, 0, 2);

            _emptyHint = Ui.L("还没有计算机\r\n\r\n点左上角「搜索学生机」自动发现同一网段里的机器\r\n也可以到「机器清单」按编号批量添加（PC-01 … PC-50）",
                              Ui.F10, Ui.Muted);
            _emptyHint.AutoSize = false;
            _emptyHint.TextAlign = ContentAlignment.MiddleCenter;
            _emptyHint.BackColor = Ui.CardBg;
            _emptyHint.Visible = false;
            _grid.Controls.Add(_emptyHint);
            _grid.Resize += delegate { LayoutEmptyHint(); };
            _emptyHint.BringToFront();

            // 详情
            RoundedPanel detailPanel = new RoundedPanel();
            detailPanel.Dock = DockStyle.Fill;
            detailPanel.Margin = new Padding(Ui.PagePad, 0, Ui.PagePad, 8);

            _detailTitle = Ui.L("选中一台计算机查看详情", Ui.F9B, Ui.TextColor);
            _detailTitle.Location = new Point(20, 12);
            detailPanel.Controls.Add(_detailTitle);

            FlowLayoutPanel db = new FlowLayoutPanel();
            db.FlowDirection = FlowDirection.LeftToRight;
            db.WrapContents = false;
            db.Height = 30;
            db.AutoSize = true;
            db.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            db.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            db.Location = new Point(300, 8);
            db.BackColor = Ui.CardBg;

            Button bRefreshOne = SmallButton("刷新此台");
            bRefreshOne.Click += delegate { RefreshOne(); };
            Button bDiag = SmallButton("连接体检");
            bDiag.Click += delegate { DiagnoseSelected(); };
            Button bRaw = SmallButton("原始输出");
            bRaw.Click += delegate { ShowRawSelected(); };
            Button bEx = SmallButton("排除项…");
            bEx.Click += delegate { OpenExclusionsDialog(); };
            Button bOpen = SmallButton("打开目录");
            bOpen.Click += delegate
            {
                Machine m = SelectedMachine();
                if (m == null) { MessageBox.Show(this, "请先选中一台计算机。"); return; }
                OpenMachineFolder(m);
            };
            Button bCopy = SmallButton("复制详情");
            bCopy.Click += delegate
            {
                try
                {
                    if (_detail.Text.Length > 0) Clipboard.SetText(_detail.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "复制失败：" + ex.Message);
                }
            };
            db.Controls.Add(bRefreshOne);
            db.Controls.Add(bDiag);
            db.Controls.Add(bRaw);
            db.Controls.Add(bEx);
            db.Controls.Add(bOpen);
            db.Controls.Add(bCopy);
            db.Location = new Point(Math.Max(320, detailPanel.Width - 400), 8);
            detailPanel.Controls.Add(db);
            detailPanel.Resize += delegate { db.Location = new Point(Math.Max(320, detailPanel.Width - db.Width - 20), 8); };

            _detail = new TextBox();
            _detail.Multiline = true;
            _detail.ReadOnly = true;
            _detail.BorderStyle = BorderStyle.None;
            _detail.BackColor = Ui.CardBg;
            _detail.Font = Ui.F9;
            _detail.Location = new Point(20, 40);
            _detail.Size = new Size(detailPanel.Width - 40, detailPanel.Height - 48);
            _detail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _detail.ScrollBars = ScrollBars.Vertical;
            detailPanel.Controls.Add(_detail);
            root.Controls.Add(detailPanel, 0, 3);

            // 状态条
            Panel statusBar = new Panel();
            statusBar.Dock = DockStyle.Fill;
            statusBar.Margin = new Padding(Ui.PagePad, 0, Ui.PagePad, 6);
            statusBar.BackColor = Color.Transparent;

            _fleetStatus = Ui.L("尚未读取机器状态。点“刷新全部状态”开始。", Ui.F85, Ui.Muted);
            _fleetStatus.Location = new Point(0, 2);
            _fleetStatus.AutoSize = true;
            statusBar.Controls.Add(_fleetStatus);
            root.Controls.Add(statusBar, 0, 4);

            return page;
        }

        private StatCard MakeStat(string caption, string value, string unit)
        {
            StatCard c = new StatCard();
            c.Caption = caption;
            c.Value = value;
            c.Unit = unit;
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(0, 0, 10, 0);
            return c;
        }

        private DataGridViewTextBoxColumn TextCol(string name, string header, int weight, bool readOnly)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = header;
            c.FillWeight = weight;
            c.ReadOnly = readOnly;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            return c;
        }

        private Button SmallButton(string text)
        {
            Button b = Ui.B(text);
            b.Height = 26;
            b.Width = 84;
            b.Font = Ui.F85;
            b.Margin = new Padding(0, 0, 4, 0);
            return b;
        }

        // ------------------------------------------------------------------ fleet model

        private void LoadFleet()
        {
            _settings = SettingsStore.Load();
            _fleet = FleetStore.Load();
            Reindex();
            RebuildGrid();
            UpdateFleetStats();
            UpdateSelectionCount();
        }

        private void Reindex()
        {
            _byName.Clear();
            foreach (Machine m in _fleet) _byName[m.Name] = m;
        }

        public void SaveFleet()
        {
            FleetStore.Save(_fleet);
        }

        private void LayoutEmptyHint()
        {
            if (_emptyHint == null || _grid == null) return;
            int top = _grid.ColumnHeadersHeight + 2;
            _emptyHint.Bounds = new Rectangle(2, top, Math.Max(0, _grid.ClientSize.Width - 4),
                                              Math.Max(0, _grid.ClientSize.Height - top - 4));
        }

        private void RebuildGrid()
        {
            // 重建前记住已勾选的机器，重建后恢复：加机器/删机器不该把老师选好的目标清空
            HashSet<string> checkedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow old in _grid.Rows)
            {
                object v = old.Cells["sel"].Value;
                if (v is bool && (bool)v) checkedNames.Add(Convert.ToString(old.Tag));
            }

            _grid.Rows.Clear();
            _rowsByMachine.Clear();
            foreach (Machine m in _fleet)
            {
                int idx = _grid.Rows.Add();
                DataGridViewRow row = _grid.Rows[idx];
                row.Tag = m.Name;
                row.Cells["name"].Value = m.DisplayName;
                row.Cells["group"].Value = m.Group;
                row.Cells["sel"].Value = checkedNames.Contains(m.Name);
                _rowsByMachine[m.Name] = row;
                FillRow(row, m);
            }
            if (_emptyHint != null)
            {
                _emptyHint.Visible = _fleet.Count == 0;
                if (_emptyHint.Visible)
                {
                    LayoutEmptyHint();
                    _emptyHint.BringToFront();
                }
            }
            ApplyFilter();
        }

        private void FillRow(DataGridViewRow row, Machine m)
        {
            RemoteState st = m.State;
            FleetHealth h = FleetView.Health(st);

            row.Cells["status"].Value = FleetView.StatusText(m);
            row.Cells["status"].Style.ForeColor = m.Busy ? Ui.Accent : FleetView.ColorOf(h);

            row.Cells["protect"].Value = FleetView.ProtectText(st);
            row.Cells["protect"].Style.ForeColor =
                h == FleetHealth.Protected ? Ui.Green :
                h == FleetHealth.Queued ? Ui.Accent :
                h == FleetHealth.Maintenance ? Ui.Amber :
                h == FleetHealth.Unprotected ? Ui.Red : Ui.Muted;

            row.Cells["maint"].Value = FleetView.MaintText(st);
            row.Cells["maint"].Style.ForeColor = st != null && st.Ok && st.UwfPresent && st.InMaintenance ? Ui.Amber : Ui.TextColor;

            row.Cells["pending"].Value = FleetView.PendingText(st);
            row.Cells["pending"].Style.ForeColor =
                st != null && st.Ok && st.UwfPresent && (st.PendingReboot || st.QueuedProtect) ? Ui.Accent : Ui.TextColor;

            row.Cells["usage"].Value = st != null && st.Ok && st.UwfPresent && st.Used >= 0 ? st.Used + " MB" : "—";
            row.Cells["usage"].Style.ForeColor = Ui.TextColor;
            row.Cells["transport"].Value = st == null ? "—" : st.TransportText;
            row.Cells["transport"].Style.ForeColor = st != null && st.Ok ? Ui.TextColor : Ui.Muted;
            row.Cells["last"].Value = m.LastAction;
            row.Cells["last"].Style.ForeColor = m.LastAction.Length > 0 ? Ui.TextColor : Ui.Muted;

            string note = FleetView.NoteText(m);
            row.Cells["note"].Value = Ui.Short(note, 52);
            row.Cells["note"].Style.ForeColor =
                h == FleetHealth.Offline || h == FleetHealth.Unprotected ? Ui.Red :
                h == FleetHealth.Queued || h == FleetHealth.Maintenance ? Ui.Amber :
                h == FleetHealth.NoUwf ? Ui.Muted : Ui.MutedLight;
        }

        private void UpdateFleetRow(Machine m)
        {
            DataGridViewRow row;
            if (_rowsByMachine.TryGetValue(m.Name, out row) && row.DataGridView != null)
            {
                FillRow(row, m);
            }
        }

        private void UpdateFleetStats()
        {
            FleetSummary s = FleetSummary.Of(_fleet);
            int checkedCount = CheckedMachines().Count;

            _fsTotal.Value = s.Total.ToString();
            _fsTotal.ValueColor = Ui.TextColor;
            _fsTotal.Sub = s.Total == 0 ? "先搜索或添加机器" : "在线 " + s.Online + " 台";

            _fsProtected.Value = s.Protected.ToString();
            _fsProtected.ValueColor = s.Protected > 0 ? Ui.Green : Ui.TextColor;
            _fsProtected.Sub = s.Total == 0 ? "" : (s.Queued > 0 ? s.Queued + " 台排队待重启" : "当前受保护");

            _fsMaint.Value = s.Maintenance.ToString();
            _fsMaint.ValueColor = s.Maintenance > 0 ? Ui.Amber : Ui.TextColor;
            _fsMaint.Sub = s.Maintenance > 0 ? "装完记得恢复还原" : "";

            _fsPending.Value = s.Pending.ToString();
            _fsPending.ValueColor = s.Pending > 0 ? Ui.Accent : Ui.TextColor;
            _fsPending.Sub = s.Pending > 0 ? "重启后生效" : "";

            _fsError.Value = s.Error.ToString();
            _fsError.ValueColor = s.Error > 0 ? Ui.Red : Ui.TextColor;
            _fsError.Sub = s.NoUwf > 0 ? "其中 " + s.NoUwf + " 台无 UWF" :
                           (s.Unprotected > 0 ? s.Unprotected + " 台未保护磁盘" : "");

            string text;
            if (s.Total == 0)
            {
                text = "还没有机器：点左上角「搜索学生机」自动发现，或到「机器清单」按编号批量添加。";
            }
            else
            {
                text = "共 " + s.Total + " 台 · 在线 " + s.Online + " 台 · 已勾选 " + checkedCount + " 台";
                if (s.Maintenance > 0) text += " · 暂停还原 " + s.Maintenance + " 台";
                if (s.Error > 0) text += " · 异常 " + s.Error + " 台";
            }
            if (_runner != null && _runner.Running) text = "正在处理… " + text;
            _fleetStatus.Text = text;

            if (_emptyHint != null)
            {
                _emptyHint.Visible = s.Total == 0;
                if (_emptyHint.Visible)
                {
                    LayoutEmptyHint();
                    _emptyHint.BringToFront();
                }
            }
            UpdateHeader();
        }

        private List<Machine> CheckedMachines()
        {
            List<Machine> list = new List<Machine>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                // 只看当前筛选后能看见的行：否则“筛出 20 台”会连带操作到隐藏的 30 台
                if (!row.Visible) continue;
                object v = row.Cells["sel"].Value;
                if (v is bool && (bool)v)
                {
                    string name = Convert.ToString(row.Tag);
                    Machine m;
                    if (_byName.TryGetValue(name, out m)) list.Add(m);
                }
            }
            return list;
        }

        /// <summary>
        /// 离线 = 已经查过、而且查询失败（无法连接）。
        /// 没查过的（State == null）不算离线，否则刚添加的清单会什么都点不了。
        /// </summary>
        private static bool IsOffline(Machine m)
        {
            return m != null && m.State != null && !m.State.Ok;
        }

        /// <summary>离线时远程动作只会白等超时，还会被误当成“操作失败”，这里直接挡掉。</summary>
        private bool BlockIfOffline(Machine m, string action)
        {
            if (!IsOffline(m)) return false;
            Log.Warn("跳过离线机器：" + m.Name + "（" + action + "）");
            MessageBox.Show(this,
                m.Name + " 现在处于离线状态，不能" + action + "。\r\n\r\n" +
                "请先确认这台机器已开机、网络通了，再点「刷新此台」或「刷新全部状态」，" +
                "等状态变成「正常」后再操作。",
                "设备离线", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        /// <summary>
        /// 批量操作前剔掉离线机器：全离线直接拦下，部分离线则先告知要跳过哪几台。
        /// 返回 false 表示调用方应当中止。
        /// </summary>
        private bool FilterOffline(ref List<Machine> targets, string action)
        {
            List<Machine> online = new List<Machine>();
            List<Machine> offline = new List<Machine>();
            foreach (Machine m in targets)
            {
                if (IsOffline(m)) offline.Add(m); else online.Add(m);
            }
            if (offline.Count == 0) return true;

            StringBuilder names = new StringBuilder();
            for (int i = 0; i < offline.Count && i < 10; i++)
            {
                names.Append(i > 0 ? "、" : "").Append(offline[i].Name);
            }
            if (offline.Count > 10) names.Append(" 等 " + offline.Count + " 台");

            foreach (Machine m in offline)
            {
                m.StatusText = "离线，已跳过";
                UpdateFleetRow(m);
            }
            Log.Warn("批量" + action + "：跳过 " + offline.Count + " 台离线机器（" + names + "）。");

            if (online.Count == 0)
            {
                MessageBox.Show(this,
                    "勾选的 " + targets.Count + " 台计算机现在全部离线，无法" + action + "。\r\n\r\n" +
                    names + "\r\n\r\n请先确认机器已开机、网络通了，再点「刷新全部状态」。",
                    "设备全部离线", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            MessageBox.Show(this,
                "有 " + offline.Count + " 台机器离线，已自动跳过：\r\n\r\n" + names +
                "\r\n\r\n其余 " + online.Count + " 台继续" + action + "。",
                "部分机器离线", MessageBoxButtons.OK, MessageBoxIcon.Information);
            targets = online;
            return true;
        }

        private Machine SelectedMachine()
        {
            if (_grid.CurrentRow == null) return null;
            string name = Convert.ToString(_grid.CurrentRow.Tag);
            Machine m;
            return _byName.TryGetValue(name, out m) ? m : null;
        }

        private void UpdateSelectionCount()
        {
            int n = CheckedMachines().Count;
            if (_selCountLabel != null) _selCountLabel.Text = "已勾选：" + n + " 台";
            if (_fleetStatus != null && !(_runner != null && _runner.Running)) UpdateFleetStats();
        }

        private void ApplyFilter()
        {
            string q = (_search.Text ?? "").Trim();
            bool onlyBad = _onlyIssues.Checked;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                string name = Convert.ToString(row.Tag);
                Machine m;
                _byName.TryGetValue(name, out m);
                bool visible = true;
                if (q.Length > 0 && m != null)
                {
                    visible = m.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                              || (m.Group ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                              || (m.Note ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (onlyBad && m != null)
                {
                    FleetHealth h = FleetView.Health(m.State);
                    visible = visible && (h == FleetHealth.Offline || h == FleetHealth.NoUwf ||
                                          h == FleetHealth.Unprotected || h == FleetHealth.Unknown);
                }
                row.Visible = visible;
            }
            if (_emptyHint != null && _fleet.Count > 0)
            {
                int visibleRows = 0;
                foreach (DataGridViewRow row in _grid.Rows) if (row.Visible) visibleRows++;
                _emptyHint.Visible = visibleRows == 0;
                if (_emptyHint.Visible)
                {
                    _emptyHint.Text = "没有符合筛选条件的计算机\r\n\r\n清空搜索框或取消「只看异常」";
                    LayoutEmptyHint();
                    _emptyHint.BringToFront();
                }
                else
                {
                    _emptyHint.Text = "还没有计算机\r\n\r\n点左上角「搜索学生机」自动发现同一网段里的机器\r\n也可以到「机器清单」按编号批量添加（PC-01 … PC-50）";
                }
            }
        }

        // ------------------------------------------------------------------ refresh

        private RemoteTarget TargetFor(Machine m)
        {
            RemoteTarget t = new RemoteTarget();
            t.Host = m.Name;
            t.User = string.IsNullOrWhiteSpace(m.User) ? _settings.User : m.User;
            t.Password = string.IsNullOrWhiteSpace(m.User) ? _settings.Password : m.Password;
            return t;
        }

        private void RefreshFleet(bool selectedOnly)
        {
            if (_runner != null && _runner.Running) return;
            List<Machine> targets = selectedOnly ? CheckedMachines() : new List<Machine>(_fleet);
            if (targets.Count == 0)
            {
                MessageBox.Show(this, targets.Count == 0 && _fleet.Count == 0
                    ? "还没有添加计算机。请先到“机器清单”页添加，或扫描网段。"
                    : "请先勾选要操作的计算机。", "没有目标");
                return;
            }

            Log.Info("开始刷新 " + targets.Count + " 台计算机的 UWF 状态。");
            StartBatchButtons(false);

            foreach (Machine m in targets)
            {
                m.Busy = true;
                m.StatusText = "查询中";
                UpdateFleetRow(m);
            }

            BusyForm busy = new BusyForm("正在刷新机器状态",
                "正在读取 " + targets.Count + " 台计算机的 UWF 状态。离线机器会先探活、快速跳过，不会一台台干等超时。",
                targets.Count, delegate { if (_runner != null) _runner.Cancel(); });
            int badSoFar = 0;

            _runner = new FleetRunner(this);
            _runner.Parallelism = _settings.Parallel;
            int doneSoFar = 0;
            _runner.Started = delegate(Machine m)
            {
                // 遮罩上要知道正在连哪台：不然 50 台时只有一个不动的 0 / 50，看着像卡死
                busy.SetProgress(-1, -1, "正在读取 " + m.Name + " 的 UWF 状态…（已完成 " + doneSoFar + " / " + targets.Count + "）");
            };
            _runner.Work = delegate(Machine m)
            {
                RemoteState st = Remote.Query(TargetFor(m), 15000);
                m.State = st;
                m.Busy = false;
                m.LastSeen = DateTime.Now;
                m.StatusText = st.Ok ? "正常" : "失败";
                m.LastError = st.Ok ? "" : st.Error;
                if (m.Group.Length == 0 && st.MachineName.Length > 0 && !string.Equals(st.MachineName, m.Name, StringComparison.OrdinalIgnoreCase))
                {
                    m.Note = "主机名 " + st.MachineName;
                }
            };
            _runner.Completed = delegate(Machine m)
            {
                doneSoFar++;
                if (m.State == null || !m.State.Ok) badSoFar++;
                UpdateFleetRow(m);
            };
            _runner.Progress = delegate(int done, int total)
            {
                _fleetStatus.Text = "已处理 " + done + " / " + total + " 台…";
                busy.SetProgress(done, total, "已完成 " + done + " / " + total + " 台" +
                    (badSoFar > 0 ? "，离线 / 失败 " + badSoFar + " 台" : "") + "。");
            };
            _runner.Finished = delegate
            {
                StartBatchButtons(true);
                UpdateFleetStats();
                int ok = 0, bad = 0;
                foreach (Machine m in targets)
                {
                    if (m.State != null && m.State.Ok) ok++; else bad++;
                }
                _fleetStatus.Text = "刷新完成：成功 " + ok + " 台，失败 " + bad + " 台。";
                Log.Info("刷新完成：成功 " + ok + " 台，失败 " + bad + " 台。");
                busy.CloseFromOwner();
            };
            _runner.Start(targets);
            try { busy.ShowDialog(this); }
            finally { busy.Dispose(); }   // ShowDialog 不会自动释放，遮罩每刷新一次就漏一个句柄

            // 被“取消”提前关掉时后台还在收尾：先把界面和按钮放出来
            if (_runner != null && _runner.Running)
            {
                StartBatchButtons(true);
                UpdateFleetStats();
                _fleetStatus.Text = "已请求取消刷新，正在结束已经发出的查询…";
            }
        }

        private List<Machine> VisibleMachines()
        {
            List<Machine> list = new List<Machine>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!row.Visible) continue;
                string name = Convert.ToString(row.Tag);
                Machine m;
                if (_byName.TryGetValue(name, out m)) list.Add(m);
            }
            return list;
        }

        private void RefreshOne()
        {
            Machine m = SelectedMachine();
            if (m == null) return;
            if (_runner != null && _runner.Running) return;
            StartBatchButtons(false);
            m.Busy = true;
            m.StatusText = "查询中";
            UpdateFleetRow(m);

            BusyForm busy = new BusyForm("正在读取 " + m.Name, "正在连接这台计算机并读取 UWF 状态，请等待。", 1, null);

            _runner = new FleetRunner(this);
            _runner.Parallelism = 1;
            _runner.Work = delegate(Machine x)
            {
                RemoteState st = Remote.Query(TargetFor(x), 15000);
                x.State = st;
                x.StatusText = st.Ok ? "正常" : "失败";
                x.LastError = st.Ok ? "" : st.Error;
                x.LastSeen = DateTime.Now;
            };
            _runner.Completed = delegate(Machine x)
            {
                UpdateFleetRow(x);
                busy.SetProgress(1, 1, null);
            };
            _runner.Finished = delegate
            {
                StartBatchButtons(true);
                UpdateFleetStats();
                ShowDetailForSelected();
                busy.CloseFromOwner();
            };
            _runner.Start(new List<Machine> { m });
            try { busy.ShowDialog(this); }
            finally { busy.Dispose(); }
        }

        // ------------------------------------------------------------------ actions

        private void StartAction(string verb, string title)
        {
            if (_runner != null && _runner.Running) return;
            List<Machine> targets = CheckedMachines();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "请先勾选要操作的计算机（表格最左边的复选框）。", "没有选中计算机");
                return;
            }
            if (!FilterOffline(ref targets, title)) return;

            // 把机器名一起列出来：筛选/勾选不一致时能当场发现打错了目标
            string nameList = "";
            for (int i = 0; i < targets.Count && i < 8; i++) nameList += (i > 0 ? "、" : "") + targets[i].Name;
            if (targets.Count > 8) nameList += " 等 " + targets.Count + " 台";

            // 维修模式：微软留给 Windows 更新用的模式，退出条件很硬，必须用带加粗的风险提示，不能一句“确定继续”带过
            if (verb == "servicing-enable")
            {
                if (!WarnDialog.Show(this, "进入维修模式（仅 Windows 更新用）",
                    "即将让 " + targets.Count + " 台计算机进入维修模式",
                    "维修模式是微软专门留给 Windows 更新的模式：进去后机器会重启并自动检查更新，期间 UWF 不还原。\r\n\r\n" +
                    "**维修模式必须等机房所有电脑的 Windows 更新全部安装完成，才能退出。** 更新装到一半就退出、断电或强制重启，" +
                    "机器会停在“正在配置更新”，甚至黑屏进不了桌面。\r\n\r\n" +
                    "**装软件、改设置不要用维修模式** —— 请用「UWF ▼ → 关闭 UWF」或远程安装向导的「暂停还原」，那只是临时关闭保护，随时可以恢复。\r\n\r\n" +
                    "**日常请保持 UWF 保护开启，一般不要进入维修模式。**\r\n\r\n" +
                    "本次目标：" + nameList,
                    "我已确认这些机器现在开始装更新，并且不会中途退出维修模式",
                    "确认进入维修模式")) return;
            }
            else if (verb == "servicing-disable")
            {
                if (!WarnDialog.Show(this, "退出维修模式",
                    "即将让 " + targets.Count + " 台计算机退出维修模式",
                    "**退出前必须确认所有这些机器的 Windows 更新已经全部装完。** 更新没装完就退出并重启，机器可能停在“正在配置更新”或直接黑屏。\r\n\r\n" +
                    "退出后要重启才回到正常保护：系统会先把更新配置完，再恢复 UWF 还原。\r\n\r\n" +
                    "**重启过程中不要断电、不要反复重启**，耐心等它自己回到桌面。\r\n\r\n" +
                    "本次目标：" + nameList,
                    "我已确认这些机器的 Windows 更新已经全部装完",
                    "确认退出维修模式")) return;
            }
            else
            {
                string extra = "";
                if (verb == "disable")
                    extra = "\r\n\r\n暂停还原后，这些机器重启就不再还原；安装完成后必须再执行“恢复还原”，否则会一直不还原。";
                if (verb == "enable")
                    extra = "\r\n\r\n恢复还原后重启，机器会重新保护，本次安装的软件保留。";
                if (verb == "restart" || verb == "shutdown")
                    extra = "\r\n\r\n这个操作会立即执行，请确认没有学生还在使用。";

                if (!Confirm("批量" + title + "（" + targets.Count + " 台）",
                    "将对以下 " + targets.Count + " 台计算机执行：" + title + extra +
                    "\r\n\r\n" + nameList + "\r\n\r\n确定继续？")) return;
            }

            Log.Info("开始批量" + title + "，共 " + targets.Count + " 台。");
            // 维修模式进出时学生机会自己重启，不再额外提示“现在重启”
            bool servicing = verb == "servicing-enable" || verb == "servicing-disable";
            bool rebootRequired = !servicing && (verb == "enable" || verb == "disable");
            RunBatchAction(targets, title, delegate(Machine m)
            {
                UwfResult r = Remote.Action(TargetFor(m), verb, m.State);
                if (!r.Ok)
                {
                    m.StatusText = "失败";
                    m.LastError = Ui.Short(r.All, 300);
                    return;
                }
                m.LastAction = title;
                m.StatusText = "已提交";
                m.LastError = "";

                if (verb == "restart" || verb == "shutdown" || servicing)
                {
                    // 重启 / 关机 / 进出维修模式之后旧状态立即作废：
                    // 否则会拿重启前的状态去回读，把“机器正在重启”误报成失败。
                    m.State = null;
                    m.StatusText = verb == "restart" ? "已重启"
                                 : verb == "shutdown" ? "已关机"
                                 : (verb == "servicing-enable" ? "维修模式" : "已退出维修");
                    m.LastAction = title;
                    return;
                }

                System.Threading.Thread.Sleep(400);
                RemoteState st = Remote.Query(TargetFor(m), 15000);
                if (st.Ok)
                {
                    m.State = st;
                    m.LastSeen = DateTime.Now;
                    m.StatusText = "正常";
                    m.LastError = "";
                }
                else
                {
                    m.StatusText = "失败";
                    m.LastError = st.Error;
                }
            }, rebootRequired ? (Action<int, int>)delegate(int ok, int bad)
            {
                if (ok > 0) PromptRestartAfterChange(title, ok, bad, targets);
            } : null);
        }

        private void PromptRestartAfterChange(string title, int ok, int bad, List<Machine> targets)
        {
            DialogResult answer = MessageBox.Show(this,
                title + "已提交到 " + ok + " 台计算机" + (bad > 0 ? "，另有 " + bad + " 台失败" : "") +
                "。该设置需要重启后生效。\r\n\r\n是否现在重启已成功提交操作的计算机？",
                "需要重启才能生效", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            List<Machine> restartTargets = new List<Machine>();
            // 只看本次操作的机器：Work 里会把 StatusText 改成“正常”，不能再靠它判断
            foreach (Machine m in targets)
            {
                if (m.LastAction == title) restartTargets.Add(m);
            }
            if (restartTargets.Count == 0)
            {
                MessageBox.Show(this, "没有找到本次成功提交的机器，请手动点“重启”。", "提示");
                return;
            }
            RunBatchAction(restartTargets, "重启以应用设置", delegate(Machine m)
            {
                UwfResult r = Remote.Action(TargetFor(m), "restart", m.State);
                if (r.Ok)
                {
                    m.State = null;
                    m.StatusText = "已重启";
                    m.LastAction = "重启以应用设置";
                    m.LastError = "";
                }
                else
                {
                    m.StatusText = "失败";
                    m.LastError = Ui.Short(r.All, 300);
                }
            });
        }

        private void RunBatchAction(List<Machine> targets, string title, Action<Machine> work, Action<int, int> afterFinished = null)
        {
            StartBatchButtons(false);
            foreach (Machine m in targets)
            {
                m.Busy = true;
                m.StatusText = "处理中";
                UpdateFleetRow(m);
            }

            BusyForm busy = new BusyForm(title,
                "正在对 " + targets.Count + " 台计算机执行「" + title + "」。每台都要等远程返回，请等待，不要重复点击。",
                targets.Count, delegate { if (_runner != null) _runner.Cancel(); });

            int finishedOk = 0, finishedBad = 0;
            _runner = new FleetRunner(this);
            _runner.Parallelism = _settings.Parallel;
            int doneSoFar = 0;
            _runner.Started = delegate(Machine m)
            {
                busy.SetProgress(-1, -1, "正在" + title + "：" + m.Name + "…（已完成 " + doneSoFar + " / " + targets.Count + "）");
            };
            _runner.Work = work;
            _runner.Completed = delegate(Machine m)
            {
                doneSoFar++;
                m.Busy = false;
                UpdateFleetRow(m);
            };
            _runner.Progress = delegate(int done, int total)
            {
                _fleetStatus.Text = title + "：已处理 " + done + " / " + total + " 台…";
                busy.SetProgress(done, total, "已完成 " + done + " / " + total + " 台。");
            };
            _runner.Finished = delegate
            {
                StartBatchButtons(true);
                UpdateFleetStats();
                int ok = 0, bad = 0;
                foreach (Machine m in targets)
                {
                    if (m.StatusText == "失败") bad++; else ok++;
                }
                finishedOk = ok;
                finishedBad = bad;
                _fleetStatus.Text = title + "完成：成功 " + ok + " 台，失败 " + bad + " 台。";
                Log.Info(title + "完成：成功 " + ok + " 台，失败 " + bad + " 台。");
                busy.CloseFromOwner();
            };
            _runner.Start(targets);
            try { busy.ShowDialog(this); }
            finally { busy.Dispose(); }

            // 遮罩被“取消”提前关掉时后台还在收尾：先把界面放出来，别让老师对着禁用的按钮等
            if (_runner != null && _runner.Running)
            {
                StartBatchButtons(true);
                UpdateFleetStats();
                Log.Info(title + "：已请求取消，正在结束已经发出的操作…");
                return;
            }

            // 遮罩关掉后再弹后续对话框，否则提示框会被模态遮罩压住、点不到
            if (afterFinished != null) afterFinished(finishedOk, finishedBad);
        }

        private void StartBatchButtons(bool enabled)
        {
            _btnDiscover.Enabled = enabled;
            _btnRefreshFleet.Enabled = enabled;
            _btnUwfFleet.Enabled = enabled;
            _btnServiceFleet.Enabled = enabled;
            _btnRestartFleet.Enabled = enabled;
            _btnShutdownFleet.Enabled = enabled;
            _btnMore.Enabled = enabled;
            _btnCancelFleet.Visible = !enabled;
        }

        private RemoteTarget TargetForHost(string host)
        {
            RemoteTarget t = new RemoteTarget();
            t.Host = host;
            t.User = _settings.User;
            t.Password = _settings.Password;
            return t;
        }

        private void OpenDiscovery()
        {
            if (_runner != null && _runner.Running)
            {
                MessageBox.Show(this, "正在执行批量任务，请等它结束再搜索。");
                return;
            }
            if (string.IsNullOrEmpty(_settings.User))
            {
                DialogResult r = MessageBox.Show(this,
                    "还没有设置学生机的管理员账号。\r\n\r\n" +
                    "用当前 Windows 账号扫描也可以（同名同密码时能读到状态），但机房通常需要用学生机的管理员账号。\r\n\r\n" +
                    "先去「机器清单」填账号吗？\r\n【是】去填账号　【否】直接扫描　【取消】返回",
                    "提示", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) { SelectPage(2); return; }
                if (r == DialogResult.Cancel) return;
            }
            using (DiscoveryForm dlg = new DiscoveryForm(TargetForHost))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int added = 0;
                foreach (Machine m in dlg.Result)
                {
                    if (_byName.ContainsKey(m.Name)) continue;
                    _fleet.Add(m);
                    _byName[m.Name] = m;
                    added++;
                }
                AfterFleetChanged();
                Log.Info("搜索学生机：发现 " + dlg.Result.Count + " 台，新增 " + added + " 台。");
                if (added > 0)
                {
                    SelectPage(0);
                    RefreshFleet(false);
                }
                else
                {
                    MessageBox.Show(this, "发现的机器都已经在清单里了。", "提示");
                }
            }
        }
        /// <summary>表格右键菜单：最常用的是“打开目录”，也就是双击那一行做的事。</summary>
        private ContextMenuStrip BuildGridMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = Ui.F9;
            string[] names = { @"打开目录（\\机器名\C$）", "连接体检", "刷新这一台", "原始输出", "排除项…" };
            EventHandler[] handlers =
            {
                delegate { OpenMachineFolder(SelectedMachine()); },
                delegate { DiagnoseSelected(); },
                delegate { RefreshOne(); },
                delegate { ShowRawSelected(); },
                delegate { OpenExclusionsDialog(); }
            };
            for (int i = 0; i < names.Length; i++) menu.Items.Add(names[i], null, handlers[i]);
            return menu;
        }

        private void ShowUwfMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = Ui.F9;
            menu.Items.Add("启动 UWF（恢复保护）", null, delegate { StartAction("enable", "启动 UWF"); });
            menu.Items.Add("关闭 UWF（装软件用这个）", null, delegate { StartAction("disable", "关闭 UWF"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("批量设置保护层（缓存）…", null, delegate { BatchSetOverlay(); });
            menu.Show(_btnUwfFleet, new Point(0, _btnUwfFleet.Height));
        }

        private void ShowServicingMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = Ui.F9;
            menu.Items.Add("进入维修模式（仅 Windows 更新用，一般不要用）", null, delegate { StartAction("servicing-enable", "进入维修模式"); });
            menu.Items.Add("退出维修模式（先确认更新全部装完）", null, delegate { StartAction("servicing-disable", "退出维修模式"); });
            menu.Show(_btnServiceFleet, new Point(0, _btnServiceFleet.Height));
        }

        private void ShowMoreMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = Ui.F9;
            menu.Items.Add("搜索局域网学生机…", null, delegate { OpenDiscovery(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("全选（当前显示）", null, delegate
            {
                foreach (DataGridViewRow row in _grid.Rows) if (row.Visible) row.Cells["sel"].Value = true;
                UpdateSelectionCount();
            });
            menu.Items.Add("全不选", null, delegate
            {
                foreach (DataGridViewRow row in _grid.Rows) row.Cells["sel"].Value = false;
                UpdateSelectionCount();
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("批量添加目录排除…", null, delegate { BatchAddExclusion(); });
            menu.Items.Add("批量删除目录排除…", null, delegate { BatchRemoveExclusion(); });
            menu.Items.Add("在目标机执行命令…", null, delegate { BatchRunCommand(); });
            menu.Items.Add("批量设置保护层（缓存）…", null, delegate { BatchSetOverlay(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("远程安装软件向导…", null, delegate { OpenInstallWizard(); });
            menu.Items.Add("导出结果为 CSV…", null, delegate { ExportCsv(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("连接体检（勾选的机器）", null, delegate { DiagnoseChecked(); });
            menu.Items.Add("打开目录（选中的机器 · C$）", null, delegate { OpenMachineFolder(SelectedMachine()); });
            menu.Items.Add("打开机器清单", null, delegate { SelectPage(2); });
            menu.Show(_btnMore, new Point(0, _btnMore.Height));
        }

        private void BatchAddExclusion()
        {
            List<Machine> targets = CheckedMachines();
            if (targets.Count == 0) { MessageBox.Show(this, "请先勾选计算机。"); return; }
            if (!FilterOffline(ref targets, "添加排除")) return;
            string path = Ui.Prompt("批量添加目录排除", "完整路径（如 D:\\作业），写进这个目录的文件不会被还原：", @"D:\作业");
            if (string.IsNullOrEmpty(path)) return;
            if (!Confirm("批量添加排除", "将给 " + targets.Count + " 台计算机添加排除：\r\n" + path + "\r\n\r\n重启后生效。确定继续？")) return;

            Log.Info("批量添加排除 " + path + "，共 " + targets.Count + " 台。");
            RunBatchAction(targets, "添加排除", delegate(Machine m)
            {
                UwfResult r = Remote.AddFileExclusion(TargetFor(m), path);
                m.StatusText = r.Ok ? "已提交" : "失败";
                m.LastError = r.Ok ? "" : Ui.Short(r.All, 200);
                if (r.Ok) m.LastAction = "加排除 " + Ui.Short(path, 12);
            });
        }

        private void BatchRemoveExclusion()
        {
            List<Machine> targets = CheckedMachines();
            if (targets.Count == 0) { MessageBox.Show(this, "请先勾选计算机。"); return; }
            if (!FilterOffline(ref targets, "删除排除")) return;
            string path = Ui.Prompt("批量删除目录排除", "要移除的排除路径：", @"D:\作业");
            if (string.IsNullOrEmpty(path)) return;
            if (!Confirm("批量删除排除", "将从 " + targets.Count + " 台计算机移除排除：\r\n" + path)) return;

            RunBatchAction(targets, "删除排除", delegate(Machine m)
            {
                UwfResult r = Remote.RemoveFileExclusion(TargetFor(m), path);
                m.StatusText = r.Ok ? "已提交" : "失败";
                m.LastError = r.Ok ? "" : Ui.Short(r.All, 200);
            });
        }

        /// <summary>
        /// 批量设置保护层（UWF 缓存）：上限 / 警告 / 严重阈值一起设，带常用预设与当前值参考。
        /// 只改当前生效类型那套参数；改动重启后生效。
        /// </summary>
        private void BatchSetOverlay()
        {
            List<Machine> targets = CheckedMachines();
            if (targets.Count == 0) { MessageBox.Show(this, "请先勾选计算机。"); return; }
            if (!FilterOffline(ref targets, "设置保护层")) return;

            // 取第一台查到缓存状态的机器做参考值
            string refName = "";
            int curMax = -1, curWarn = -1, curCrit = -1, curType = -1;
            foreach (Machine m in targets)
            {
                RemoteState st = m.State;
                if (st != null && st.Ok && st.UwfPresent && st.Max > 0)
                {
                    refName = m.Name;
                    curMax = st.Max;
                    curWarn = st.Warn;
                    curCrit = st.Crit;
                    curType = st.Type;
                    break;
                }
            }

            int size, warn, crit;
            using (OverlayBatchDialog dlg = new OverlayBatchDialog(targets.Count, refName, curMax, curWarn, curCrit, curType))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                size = dlg.SizeMb;
                warn = dlg.WarnMb;
                crit = dlg.CritMb;
            }

            if (!Confirm("批量设置保护层",
                "将把 " + targets.Count + " 台机器的保护层设为：\r\n\r\n" +
                "  上限：" + size + " MB\r\n  警告阈值：" + warn + " MB\r\n  严重阈值：" + crit + " MB\r\n\r\n" +
                "（只改当前生效类型那一套参数，重启后生效。）\r\n\r\n确定继续？")) return;

            Log.Info("批量设置保护层：上限 " + size + " MB、警告 " + warn + " MB、严重 " + crit + " MB，共 " + targets.Count + " 台。");
            RunBatchAction(targets, "设置保护层", delegate(Machine m)
            {
                UwfResult r = Remote.Exec(TargetFor(m), "uwfmgr.exe overlay set-size " + size, 90000);
                if (r.Ok) r = Remote.Exec(TargetFor(m), "uwfmgr.exe overlay set-warningthreshold " + warn, 90000);
                if (r.Ok) r = Remote.Exec(TargetFor(m), "uwfmgr.exe overlay set-criticalthreshold " + crit, 90000);
                m.StatusText = r.Ok ? "已提交" : "失败";
                m.LastError = r.Ok ? "" : Ui.Short(r.All, 200);
                if (r.Ok) m.LastAction = "保护层 " + size + " MB";
            });
        }
        /// <summary>命令里的密码要打码再进日志：操作日志会导进诊断包，明文密码不能留在里面。</summary>
        private static string MaskSecrets(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return "";
            string s = System.Text.RegularExpressions.Regex.Replace(cmd,
                @"(?i)(/user:\S+\s+)\S+", "$1******");
            s = System.Text.RegularExpressions.Regex.Replace(s,
                @"(?i)((?:-p|--password|-password|/password|password=)\s*)\S+", "$1******");
            return s;
        }

        private void BatchRunCommand()
        {
            List<Machine> targets = CheckedMachines();
            if (targets.Count == 0) { MessageBox.Show(this, "请先勾选计算机。"); return; }
            if (!FilterOffline(ref targets, "执行命令")) return;
            string cmd = Ui.Prompt("在目标机执行命令",
                "命令会在每台机器上以管理员身份执行，例如：\r\n\\\\教师机\\软件\\setup.exe /S", "");
            if (string.IsNullOrEmpty(cmd)) return;
            if (!Confirm("批量执行命令", "将在 " + targets.Count + " 台计算机上执行：\r\n\r\n" + cmd + "\r\n\r\n确定继续？")) return;

            Log.Info("批量执行命令：" + MaskSecrets(cmd) + "（" + targets.Count + " 台）");
            RunBatchAction(targets, "执行命令", delegate(Machine m)
            {
                UwfResult r = Remote.Exec(TargetFor(m), cmd, 600000);
                m.StatusText = r.Ok ? "完成" : "失败";
                m.LastError = r.Ok ? "" : Ui.Short(r.All, 200);
                if (r.Ok) m.LastAction = "执行命令";
            });
        }

        private void ExportCsv()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.FileName = "uwf-机房状态-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv";
            dlg.Filter = "CSV 文件|*.csv";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("计算机,分组,状态,保护,暂停还原,待重启,缓存MB,通道,上次操作,说明");
            foreach (Machine m in _fleet)
            {
                RemoteState st = m.State;
                sb.AppendLine(string.Join(",", new string[]
                {
                    Csv(m.Name), Csv(m.Group), Csv(FleetView.StatusText(m)),
                    Csv(FleetView.ProtectText(st)),
                    Csv(st != null && st.Ok && st.UwfPresent && st.InMaintenance ? "是" : "否"),
                    Csv(st != null && st.Ok && st.UwfPresent && (st.PendingReboot || st.QueuedProtect) ? "是" : "否"),
                    Csv(st != null && st.Ok && st.UwfPresent && st.Used >= 0 ? st.Used.ToString() : ""),
                    Csv(st == null ? "" : st.TransportText),
                    Csv(m.LastAction), Csv(FleetView.NoteText(m))
                }));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            Log.Info("已导出结果：" + dlg.FileName);
        }

        private string Csv(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // ------------------------------------------------------------------ detail

        private void ShowDetailForSelected()
        {
            Machine m = SelectedMachine();
            if (m == null)
            {
                _detailTitle.Text = "选中一台计算机查看详情";
                _detail.Text = "提示：点击表格里的任意一行，这里会显示这台机器的连接情况、保护状态和最近一次错误。\r\n" +
                              "双击某一行可以直接打开这台机器的 C 盘（\\\\机器名\\C$），也可以点右下角「打开目录」。";
                return;
            }
            RemoteState st = m.State;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(m.DisplayName + (m.Group.Length > 0 ? "    · " + m.Group : "") + (m.Note.Length > 0 ? "    · " + m.Note : ""));
            sb.AppendLine("状态：" + FleetView.StatusText(m) +
                          (m.LastSeen > DateTime.MinValue ? "（上次查询 " + Ago(m.LastSeen) + "）" : "（还没有查询过）"));
            sb.AppendLine();

            if (st == null)
            {
                sb.AppendLine("还没有读取状态。点「刷新此台」或顶部的「刷新全部」开始。");
                _detail.Text = sb.ToString();
                _detailTitle.Text = m.DisplayName + " · 详情";
                return;
            }

            if (!st.Ok)
            {
                sb.AppendLine("连接：失败（" + st.TransportText + "）");
                string hint = st.ErrorHint;
                if (hint.Length > 0) sb.AppendLine("建议：" + hint);
                sb.AppendLine();
                sb.AppendLine("原始错误：");
                sb.AppendLine(st.Error);
                if (m.LastAction.Length > 0) sb.AppendLine();
                if (m.LastAction.Length > 0) sb.AppendLine("上次操作：" + m.LastAction);
                _detail.Text = sb.ToString();
                _detailTitle.Text = m.DisplayName + " · 无法连接";
                return;
            }

            sb.AppendLine("连接：正常 · 通道 " + st.TransportText);
            sb.AppendLine("UWF：" + (st.UwfPresent ? "已安装" : "未安装（这台机器没有 UWF 组件）"));
            if (st.UwfPresent)
            {
                sb.AppendLine("保护（当前 / 重启后）：" + (st.FilterCurrent ? "开启" : "关闭") + " / " + (st.FilterNext ? "开启" : "关闭") +
                              "    · 暂停还原：" + (st.InMaintenance ? "是" : "否") +
                              (st.ServicingCurrent ? "（维修模式：Windows 更新中）" : ""));
                sb.AppendLine("待重启：" + ((st.PendingReboot || st.QueuedProtect) ? "是（有设置排队，重启后生效）" : "否"));
                sb.AppendLine("受保护卷：" + st.VolumeText +
                              (st.PendingVolumeText != "—" ? "    · 排队保护：" + st.PendingVolumeText : ""));
                sb.AppendLine("缓存：" + (st.Used >= 0 ? st.Used + " MB" : "未知") +
                              (st.Max > 0 ? " / 上限 " + st.Max + " MB" : "") +
                              (st.Free >= 0 ? "    · 剩余 " + st.Free + " MB" : "") +
                              "    · 类型 " + (st.Type == 0 ? "内存 RAM" : (st.Type == 1 ? "磁盘 DISK" : "未知")));
            }
            else
            {
                sb.AppendLine("这台机器能连上，但没装 UWF：只能查看，不能远程开关还原。");
            }
            if (m.LastAction.Length > 0) sb.AppendLine("上次操作：" + m.LastAction);
            if (m.LastError.Length > 0) sb.AppendLine("上次错误：" + Ui.Short(m.LastError, 300));
            _detail.Text = sb.ToString();
            _detailTitle.Text = m.DisplayName + " · " + FleetView.StatusText(m);
        }

        private static string Ago(DateTime t)
        {
            TimeSpan span = DateTime.Now - t;
            if (span.TotalSeconds < 60) return Math.Max(0, (int)span.TotalSeconds) + " 秒前";
            if (span.TotalMinutes < 60) return Math.Max(1, (int)span.TotalMinutes) + " 分钟前";
            if (span.TotalHours < 24) return Math.Max(1, (int)span.TotalHours) + " 小时前";
            return Math.Max(1, (int)span.TotalDays) + " 天前";
        }

        private void ShowRawSelected()
        {
            Machine m = SelectedMachine();
            if (m == null) return;
            string text;
            if (m.State == null) text = "还没有查询过这台计算机。";
            else if (m.State.Raw.Length > 0) text = m.State.Raw;
            else text = "本次读取走 " + m.State.TransportText + " 通道，没有保留原始文本。\r\n\r\n" +
                        (m.State.Ok ? "状态读取成功。" : m.State.Error);
            ShowTextDialog(m.Name + " · 原始输出", text);
        }

        private Machine MachineAt(int rowIndex)
        {
            if (rowIndex < 0 || _grid == null || rowIndex >= _grid.Rows.Count) return null;
            string name = Convert.ToString(_grid.Rows[rowIndex].Tag);
            Machine m;
            if (string.IsNullOrEmpty(name) || !_byName.TryGetValue(name, out m)) return null;
            return m;
        }

        /// <summary>
        /// 双击表格 → 用资源管理器打开这台机器的 C 盘（\\机器名\C$）。
        /// 先用「机器清单」里的账号连一次 IPC$：这样资源管理器复用同一次登录会话，
        /// 不会再单独弹“输入网络密码”。连不上也不拦着打开，让资源管理器自己去认证。
        /// </summary>
        private void OpenMachineFolder(Machine m)
        {
            if (m == null) return;
            if (BlockIfOffline(m, "打开它的目录")) return;

            RemoteTarget t = TargetFor(m);
            string share = @"\\" + m.Name + @"\C$";

            // 连共享可能挂到系统网络超时（机器掉线时尤其久）：放后台跑，
            // 别让双击之后整个界面冻在那里。
            System.Threading.Tasks.Task.Factory.StartNew(delegate
            {
                string credError = "";
                if (!string.IsNullOrEmpty(t.User))
                {
                    try { credError = Remote.ConnectShare(t) ?? ""; }
                    catch (Exception ex) { credError = ex.Message; }
                }

                string startErr = "";
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + share + "\"");
                }
                catch (Exception ex) { startErr = ex.Message; }

                Log.Info("打开机器目录：" + share +
                         (credError.Length == 0 ? "" : "（按清单账号连接失败：" + Ui.Short(credError, 120) + "）") +
                         (startErr.Length == 0 ? "" : "（启动资源管理器失败：" + Ui.Short(startErr, 120) + "）"));

                if (startErr.Length == 0 && credError.Length == 0) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed || Disposing) return;
                        if (startErr.Length > 0)
                        {
                            MessageBox.Show(this, "打不开 " + share + "：\r\n\r\n" + startErr,
                                            "打开目录", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        else
                        {
                            MessageBox.Show(this,
                                "已经尝试打开 " + share + "，但按「机器清单」里的账号连接失败：\r\n\r\n" + Ui.Short(credError, 400) +
                                "\r\n\r\n资源管理器可能会自己弹窗让你输账号密码；如果它直接报错，先点「连接体检」看是共享没开、防火墙还是账号权限的问题。",
                                "打开目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    });
                }
                catch { }
            });
        }

        private void DiagnoseSelected()
        {
            Machine m = SelectedMachine();
            if (m == null) { MessageBox.Show(this, "请先选中一台计算机。"); return; }
            DiagnoseMachines(new List<Machine> { m });
        }

        private void DiagnoseChecked()
        {
            List<Machine> targets = CheckedMachines();
            if (targets.Count == 0) targets = VisibleMachines();
            if (targets.Count == 0) { MessageBox.Show(this, "没有可检查的计算机。"); return; }
            DiagnoseMachines(targets);
        }

        private void DiagnoseMachines(List<Machine> targets)
        {
            // 每台体检含 ping/共享/WMI/读状态，单台最长几十秒：放后台跑，用遮罩顶住界面
            Cursor = Cursors.WaitCursor;
            if (_fleetStatus != null) _fleetStatus.Text = "正在连接体检 " + targets.Count + " 台…";

            CancellationTokenSource cts = new CancellationTokenSource();
            string report = "";
            int checkedCount = 0;

            using (BusyForm busy = new BusyForm("正在连接体检",
                "正在逐台检查 " + targets.Count + " 台计算机的 ping / 共享 / WMI，单台可能要几十秒。",
                targets.Count, delegate { try { cts.Cancel(); } catch { } }))
            {
                SynchronizationContext ui = SynchronizationContext.Current;
                Task.Factory.StartNew(delegate
                {
                    StringBuilder sb = new StringBuilder();
                    try
                    {
                        for (int i = 0; i < targets.Count; i++)
                        {
                            if (cts.IsCancellationRequested)
                            {
                                sb.AppendLine("（已取消，剩余 " + (targets.Count - i) + " 台未检查）");
                                break;
                            }
                            Machine m = targets[i];
                            busy.SetProgress(i, targets.Count, "正在检查 " + m.Name + "（" + (i + 1) + " / " + targets.Count + "）");
                            sb.AppendLine("=== " + m.Name + " ===");
                            sb.AppendLine(Remote.Diagnose(TargetFor(m)));
                            sb.AppendLine();
                            checkedCount = i + 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("体检中断：" + ex.Message);
                    }
                    report = sb.ToString();
                    try
                    {
                        if (ui != null) ui.Post(delegate { busy.CloseFromOwner(); }, null);
                        else busy.BeginInvoke((MethodInvoker)delegate { busy.CloseFromOwner(); });
                    }
                    catch (InvalidOperationException) { }
                });

                try { busy.ShowDialog(this); }
                finally { busy.CloseFromOwner(); }   // 后台异常时也不至于把界面锁死
            }

            Cursor = Cursors.Default;
            if (IsDisposed || Disposing) return;
            if (_fleetStatus != null)
                _fleetStatus.Text = "连接体检完成" + (checkedCount < targets.Count ? "（已取消）" : "") + "。";
            if (report.Length > 0) ShowTextDialog("连接体检（" + targets.Count + " 台）", report);
        }

        private void ShowTextDialog(string title, string text)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.Font = Ui.F9;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(760, 520);
                f.MinimizeBox = false;
                TextBox box = new TextBox();
                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Both;
                box.WordWrap = false;
                box.Font = Ui.Mono;
                box.BackColor = Ui.RawBg;
                box.ForeColor = Ui.TextColor;
                box.BorderStyle = BorderStyle.FixedSingle;
                box.Dock = DockStyle.Fill;
                box.Text = text;
                f.Controls.Add(box);
                Button close = Ui.B("关闭", true);
                close.Dock = DockStyle.Bottom;
                close.Height = 38;
                close.Click += delegate { f.Close(); };
                f.Controls.Add(close);
                f.CancelButton = close;
                f.ShowDialog(this);
            }
        }

        private void OpenExclusionsDialog()
        {
            Machine m = SelectedMachine();
            if (m == null) { MessageBox.Show(this, "请先选中一台计算机。"); return; }
            // 远程拉取可能要几十秒：放后台，完成后回 UI 线程弹窗
            Cursor = Cursors.WaitCursor;
            if (_fleetStatus != null) _fleetStatus.Text = "正在读取 " + m.Name + " 的排除项…";
            SynchronizationContext ui = SynchronizationContext.Current;
            Machine machine = m;
            Task.Factory.StartNew(delegate
            {
                StringBuilder sb = new StringBuilder();
                try
                {
                    List<string> files = Remote.FetchFileExclusions(TargetFor(machine), machine.State);
                    List<string> regs = Remote.FetchRegExclusions(TargetFor(machine));
                    sb.AppendLine("文件 / 文件夹排除（" + files.Count + "）：");
                    if (files.Count == 0) sb.AppendLine("  （无）");
                    foreach (string s in files) sb.AppendLine("  " + s);
                    sb.AppendLine();
                    sb.AppendLine("注册表排除（" + regs.Count + "）：");
                    if (regs.Count == 0) sb.AppendLine("  （无）");
                    foreach (string s in regs) sb.AppendLine("  " + s);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("读取失败：" + ex.Message);
                }
                string report = sb.ToString();
                if (ui == null) return;
                ui.Post(delegate
                {
                    if (IsDisposed || Disposing) return;
                    Cursor = Cursors.Default;
                    if (_fleetStatus != null) _fleetStatus.Text = "排除项读取完成。";
                    ShowTextDialog(machine.Name + " · 排除项", report);
                }, null);
            });
        }

        private void OpenInstallWizard()
        {
            if (!EnsureMasterChecks()) return;
            List<Machine> targets = CheckedMachines();
            if (targets.Count == 0) targets = VisibleMachines();
            if (targets.Count == 0) { MessageBox.Show(this, "请先添加计算机。"); return; }
            if (!FilterOffline(ref targets, "远程安装")) return;
            using (RemoteInstallForm f = new RemoteInstallForm(targets, _settings, TargetFor))
            {
                f.ShowDialog(this);
                foreach (Machine m in targets) UpdateFleetRow(m);
                UpdateFleetStats();
            }
        }

        // ------------------------------------------------------------------ remote install page

        private Panel BuildRemoteInstallPage()
        {
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Ui.Bg;

            FlowLayoutPanel stack = Ui.Stack();
            page.Controls.Add(stack);

            Card hero = new Card("远程安装：关闭保护 → 装软件 → 恢复保护，装好的东西重启后还在", 224, Ui.Accent);
            hero.Tint = Ui.TintBlue;
            Label t = Ui.Wrap("向导自动完成：给所选机器关闭 UWF 保护（暂停还原，不是维修模式） → 重启 → 等机器回到在线 → 分发并部署（安装包无人值守安装 / 便携版拷目录+公共桌面快捷方式） → 恢复保护 → 重启生效。", Ui.F9, Ui.TextColor);
            t.Location = new Point(0, 0);
            t.Size = new Size(960, 40);
            hero.Body.Controls.Add(t);

            Button start = Ui.B("开始远程安装向导", true);
            start.Location = new Point(0, 52);
            start.Width = 180;
            start.Height = 34;
            start.Click += delegate { OpenInstallWizard(); };
            hero.Body.Controls.Add(start);

            Button quick = Ui.B("先给所选机器暂停还原");
            quick.Location = new Point(190, 52);
            quick.Width = 180;
            quick.Height = 34;
            quick.Click += delegate { StartAction("disable", "暂停还原（关闭 UWF 保护）"); };
            hero.Body.Controls.Add(quick);

            Button sel = Ui.B("去勾选计算机");
            sel.Location = new Point(380, 52);
            sel.Width = 120;
            sel.Height = 34;
            sel.Click += delegate { SelectPage(0); };
            hero.Body.Controls.Add(sel);

            Label hint = Ui.Wrap("提示：向导里的“分发安装包”会先把文件复制到每台机器的 C:\\Windows\\Temp\\UWFTeacher\\deploy，再在本机运行，适合安装包比较大、共享路径不稳定的情况。", Ui.F85, Ui.Muted);
            hint.Location = new Point(0, 100);
            hint.Size = new Size(960, 44);
            hero.Body.Controls.Add(hint);
            stack.Controls.Add(hero);

            Card pre = new Card("开始前请确认", 158);
            Label p = Ui.Wrap(
                "① 教师机能访问每台学生机的 \\\\计算机名\\C$（管理共享）和 WMI；\r\n" +
                "② 使用的账号是学生机的本地管理员（工作组环境还需要目标机执行过一次 tools\\启用远程管理.cmd）；\r\n" +
                "③ 学生机都装了 UWF（企业版 / 教育版 / IoT 版），且当前处于“保护已开启”；\r\n" +
                "④ 安装包支持静默安装参数（如 /S、/quiet /norestart），否则会弹在学生机屏幕上等人点。", Ui.F9, Ui.TextColor);
            p.Location = new Point(0, 0);
            p.Size = new Size(960, 150);
            pre.Body.Controls.Add(p);
            stack.Controls.Add(pre);

            Card faq = new Card("常见情况", 128);
            Label q = Ui.Wrap(
                "• 有的机器失败：点“连接体检”看是 ping、共享、WMI 还是权限问题。\r\n" +
                "• 重启后连不上：等 1–2 分钟再刷新，或确认机器没有停在登录界面等待输入密码。\r\n" +
                "• 装完忘记恢复还原：总览页顶部“暂停还原”数字会大于 0，勾选这些机器再点“恢复还原”。", Ui.F9, Ui.TextColor);
            q.Location = new Point(0, 0);
            q.Size = new Size(960, 120);
            faq.Body.Controls.Add(q);
            stack.Controls.Add(faq);

            return page;
        }
    }
}
