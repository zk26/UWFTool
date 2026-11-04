using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UwfTeacher
{
    /// <summary>扫描局域网，自动发现开了 UWF 的学生机，选中的直接加入机器清单。</summary>
    public class DiscoveryForm : Form
    {
        private class Host
        {
            public string Ip = "";
            public string Name = "";
            public Remote.ProbeResult Probe;
            public bool Selected = true;
        }

        private readonly Func<string, RemoteTarget> _targetFor;
        private readonly List<Host> _hosts = new List<Host>();
        private readonly Dictionary<string, DataGridViewRow> _rows = new Dictionary<string, DataGridViewRow>(StringComparer.OrdinalIgnoreCase);

        private TextBox _prefixes;
        private DataGridView _grid;
        private Label _status;
        private Label _preview;
        private ProgressBar _bar;
        private Button _btnStart;
        private Button _btnAdd;
        private Button _btnStop;
        private CheckBox _onlyUwf;
        private CheckBox _onlyFailed;

        private CancellationTokenSource _cts;
        private bool _running;

        public List<Machine> Result = new List<Machine>();

        public DiscoveryForm(Func<string, RemoteTarget> targetFor)
        {
            _targetFor = targetFor;
            Text = "搜索学生机 · 自动发现局域网里的 UWF 机器";
            Font = Ui.F9;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(800, 560);
            BackColor = Ui.Bg;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.BackColor = Ui.Bg;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 172));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            Controls.Add(root);

            // 顶部
            Panel top = new Panel();
            top.Dock = DockStyle.Fill;
            top.Margin = new Padding(14, 12, 14, 4);
            top.BackColor = Ui.CardBg;
            top.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Ui.Border)) e.Graphics.DrawRectangle(p, 0, 0, top.Width - 1, top.Height - 1);
            };
            root.Controls.Add(top, 0, 0);

            Label l = Ui.L("要扫描的范围（每行一个；支持网段 / 区间 / CIDR）", Ui.F9B, Ui.TextColor);
            l.Location = new Point(14, 10);
            top.Controls.Add(l);

            Label hint = Ui.L("例：192.168.1.    192.168.1.10-60    192.168.1.0/24", Ui.F85, Ui.Muted);
            hint.Location = new Point(250, 12);
            top.Controls.Add(hint);

            _prefixes = new TextBox();
            Ui.StyleInput(_prefixes);
            _prefixes.Multiline = true;
            _prefixes.ScrollBars = ScrollBars.Vertical;
            _prefixes.Height = 92;
            _prefixes.Location = new Point(14, 34);
            _prefixes.Width = 600;
            _prefixes.Text = string.Join("\r\n", LocalPrefixes().ToArray());
            _prefixes.TextChanged += delegate { UpdatePreview(); };
            top.Controls.Add(_prefixes);

            _btnStart = Ui.B("开始搜索", true);
            _btnStart.Location = new Point(628, 34);
            _btnStart.Width = 128;
            _btnStart.Height = 34;
            _btnStart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnStart.Click += delegate { StartScan(); };
            top.Controls.Add(_btnStart);

            _btnStop = Ui.B("停止");
            _btnStop.Location = new Point(764, 34);
            _btnStop.Width = 100;
            _btnStop.Height = 34;
            _btnStop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnStop.Enabled = false;
            _btnStop.Click += delegate { if (_cts != null) _cts.Cancel(); };
            top.Controls.Add(_btnStop);

            _preview = Ui.Wrap("", Ui.F85, Ui.Muted);
            _preview.Location = new Point(628, 76);
            _preview.Size = new Size(236, 40);
            _preview.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            top.Controls.Add(_preview);

            top.Resize += delegate
            {
                int right = top.Width - 14;
                _btnStop.Location = new Point(right - _btnStop.Width, 34);
                _btnStart.Location = new Point(_btnStop.Left - 8 - _btnStart.Width, 34);
                _preview.Location = new Point(_btnStart.Left, 76);
                _preview.Width = right - _btnStart.Left;
                _prefixes.Width = Math.Max(220, _btnStart.Left - 14 - 14);
            };

            Label foot = Ui.Wrap("扫描分两步：先探测在线（ping / TCP 端口 / ARP），再用 WMI 确认是否装了 UWF。禁 ping 的机器也能被发现。",
                                 Ui.F85, Ui.MutedLight);
            foot.Location = new Point(14, 130);
            foot.Size = new Size(820, 20);
            top.Controls.Add(foot);

            // 表格
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.Margin = new Padding(14, 4, 14, 4);
            Ui.StyleGrid(_grid);
            _grid.MultiSelect = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.ShowCellToolTips = true;

            DataGridViewCheckBoxColumn sel = new DataGridViewCheckBoxColumn();
            sel.HeaderText = "";
            sel.Name = "sel";
            sel.Width = 36;
            sel.FillWeight = 4;
            sel.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            sel.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(sel);
            _grid.Columns.Add(Col("ip", "IP 地址", 80));
            _grid.Columns.Add(Col("name", "计算机名", 100));
            _grid.Columns.Add(Col("uwf", "UWF", 50));
            _grid.Columns.Add(Col("state", "状态", 105));
            _grid.Columns.Add(Col("note", "说明", 150));
            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                bool on = Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells["sel"].Value ?? false);
                Host h = _grid.Rows[e.RowIndex].Tag as Host;
                if (h != null) h.Selected = on;
                UpdateCounts();
            };
            _grid.CellToolTipTextNeeded += delegate(object s, DataGridViewCellToolTipTextNeededEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                object v = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
                e.ToolTipText = v == null ? "" : Convert.ToString(v);
            };
            root.Controls.Add(_grid, 0, 1);

            // 进度
            Panel mid = new Panel();
            mid.Dock = DockStyle.Fill;
            mid.Margin = new Padding(14, 0, 14, 0);
            mid.BackColor = Ui.Bg;
            _bar = new ProgressBar();
            _bar.Location = new Point(0, 12);
            _bar.Size = new Size(220, 14);
            _bar.Visible = false;
            mid.Controls.Add(_bar);
            _status = Ui.L("点「开始搜索」，会先探测整个范围，再逐台确认是不是 UWF 机器。", Ui.F85, Ui.Muted);
            _status.Location = new Point(232, 10);
            _status.AutoSize = false;
            _status.AutoEllipsis = true;
            _status.Size = new Size(600, 20);
            mid.Controls.Add(_status);
            mid.Resize += delegate
            {
                _status.Width = Math.Max(200, mid.Width - 232);
            };
            root.Controls.Add(mid, 0, 2);

            // 底部
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Fill;
            bottom.BackColor = Ui.Bg;
            bottom.Margin = new Padding(14, 0, 14, 8);

            FlowLayoutPanel left = new FlowLayoutPanel();
            left.FlowDirection = FlowDirection.LeftToRight;
            left.WrapContents = false;
            left.Location = new Point(0, 12);
            left.Size = new Size(560, 34);
            left.BackColor = Ui.Bg;

            Button all = Ui.B("全选");
            all.Width = 68;
            all.Click += delegate { SetAll(true); };
            Button none = Ui.B("全不选");
            none.Width = 76;
            none.Click += delegate { SetAll(false); };
            Button uwfOnly = Ui.B("只选 UWF");
            uwfOnly.Width = 92;
            uwfOnly.Click += delegate { SelectUwfOnly(); };
            _onlyUwf = new CheckBox();
            _onlyUwf.Text = "只看 UWF";
            _onlyUwf.Font = Ui.F9;
            _onlyUwf.AutoSize = true;
            _onlyUwf.Margin = new Padding(8, 9, 0, 0);
            _onlyUwf.CheckedChanged += delegate { ApplyFilter(); };
            _onlyFailed = new CheckBox();
            _onlyFailed.Text = "只看异常";
            _onlyFailed.Font = Ui.F9;
            _onlyFailed.AutoSize = true;
            _onlyFailed.Margin = new Padding(8, 9, 0, 0);
            _onlyFailed.CheckedChanged += delegate { ApplyFilter(); };

            left.Controls.Add(all);
            left.Controls.Add(none);
            left.Controls.Add(uwfOnly);
            left.Controls.Add(_onlyUwf);
            left.Controls.Add(_onlyFailed);
            bottom.Controls.Add(left);

            _btnAdd = Ui.B("加入机器清单", true);
            _btnAdd.Width = 150;
            _btnAdd.Height = 34;
            _btnAdd.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnAdd.Location = new Point(560, 12);
            _btnAdd.Enabled = false;
            _btnAdd.Click += delegate { Accept(); };
            bottom.Controls.Add(_btnAdd);

            Button close = Ui.B("取消");
            close.Width = 88;
            close.Height = 34;
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Location = new Point(720, 12);
            close.Click += delegate { Close(); };
            bottom.Controls.Add(close);
            bottom.Resize += delegate
            {
                close.Location = new Point(bottom.Width - 96, 12);
                _btnAdd.Location = new Point(bottom.Width - 96 - 158, 12);
                left.Width = Math.Max(240, bottom.Width - 96 - 158 - 12);
            };

            root.Controls.Add(bottom, 0, 3);
            CancelButton = close;

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_preview == null) return;
            string err;
            List<string> ips = SubnetScanner.ExpandTargets(_prefixes.Text, out err);
            if (ips.Count == 0)
            {
                _preview.Text = err.Length > 0 ? err : "请输入要扫描的网段";
                _preview.ForeColor = err.Length > 0 ? Ui.Amber : Ui.Muted;
            }
            else
            {
                _preview.Text = "将扫描 " + ips.Count + " 个地址\r\n" + ips[0] + " … " + ips[ips.Count - 1];
                _preview.ForeColor = err.Length > 0 ? Ui.Amber : Ui.Muted;
            }
        }

#if DEMO
        public void LoadDemoHosts()
        {
            string[,] data =
            {
                { "192.168.1.11", "PC-01", "1", "1" },
                { "192.168.1.12", "PC-02", "1", "1" },
                { "192.168.1.13", "PC-03", "1", "1" },
                { "192.168.1.14", "PC-04", "1", "0" },
                { "192.168.1.15", "PC-05", "1", "1" },
                { "192.168.1.16", "", "0", "0" },
                { "192.168.1.17", "PC-07", "1", "1" },
                { "192.168.1.18", "", "0", "0" }
            };
            for (int i = 0; i < data.GetLength(0); i++)
            {
                Host h = new Host();
                h.Ip = data[i, 0];
                h.Name = data[i, 1];
                h.Probe = new Remote.ProbeResult();
                h.Probe.Host = h.Ip;
                h.Probe.Name = h.Name;
                h.Probe.WmiOk = true;
                h.Probe.UwfOk = data[i, 2] == "1";
                h.Probe.NoUwf = !h.Probe.UwfOk;
                h.Probe.FilterCurrent = data[i, 3] == "1";
                h.Probe.Protected = h.Probe.FilterCurrent;
                if (h.Name.Length == 0) h.Probe.Error = "未安装 UWF 组件";

                _hosts.Add(h);
                int idx = _grid.Rows.Add();
                DataGridViewRow row = _grid.Rows[idx];
                row.Tag = h;
                row.Cells["sel"].Value = true;
                row.Cells["ip"].Value = h.Ip;
                row.Cells["name"].Value = "…";
                row.Cells["uwf"].Value = "…";
                row.Cells["state"].Value = "探测中";
                _rows[h.Ip] = row;
                ApplyProbe(h.Ip, h.Probe);
            }
            _status.Text = "搜索完成：在线 8 台，其中 UWF 机器 6 台。勾选后点「加入机器清单」。";
            _btnAdd.Enabled = true;
            UpdateCounts();
        }
#endif
        private DataGridViewTextBoxColumn Col(string name, string header, int weight)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = header;
            c.FillWeight = weight;
            c.ReadOnly = true;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            return c;
        }

        // ---------------------------------------------------------------- 本机网段

        public static List<string> LocalPrefixes()
        {
            List<string> list = new List<string>();
            List<string> fallback = new List<string>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    if (IsVirtualAdapter(ni)) continue;
                    IPInterfaceProperties props = ni.GetIPProperties();
                    bool hasGateway = props.GatewayAddresses != null && props.GatewayAddresses.Count > 0;
                    foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ua.Address)) continue;
                        byte[] b = ua.Address.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;
                        string prefix = b[0] + "." + b[1] + "." + b[2] + ".";
                        if (hasGateway && !list.Contains(prefix)) list.Add(prefix);
                        else if (!fallback.Contains(prefix)) fallback.Add(prefix);
                    }
                }
            }
            catch
            {
            }
            if (list.Count == 0) list = fallback;
            if (list.Count == 0) list.Add("192.168.1.");
            return list;
        }

        private static bool IsVirtualAdapter(NetworkInterface ni)
        {
            string name = (ni.Name + " " + ni.Description).ToLowerInvariant();
            string[] words = { "vmware", "virtualbox", "hyper-v", "vethernet", "tap-", "tun", "vpn",
                               "bluetooth", "wsl", "tailscale", "zerotier", "npcap", "loopback",
                               "docker", "parallels", "qemu", "radmin" };
            foreach (string w in words) if (name.IndexOf(w, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // ---------------------------------------------------------------- 扫描

        private void StartScan()
        {
            if (_running) return;

            string err;
            List<string> ips = SubnetScanner.ExpandTargets(_prefixes.Text, out err);
            if (ips.Count == 0)
            {
                MessageBox.Show(this, err.Length > 0 ? err : "请先填写要扫描的网段，例如 192.168.1.", "没有扫描目标");
                return;
            }
            if (err.Length > 0)
            {
                if (MessageBox.Show(this, err + "\r\n\r\n继续扫描？", "提示",
                                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            }

            _hosts.Clear();
            _rows.Clear();
            _grid.Rows.Clear();
            _btnAdd.Enabled = false;
            _btnAdd.Text = "加入机器清单";
            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
            _bar.Visible = true;
            _bar.Minimum = 0;
            _bar.Maximum = ips.Count;
            _bar.Value = 0;
            _running = true;
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            SynchronizationContext ui = SynchronizationContext.Current;

            _status.Text = "正在探测 " + ips.Count + " 个地址（ping / 445 / 135 / ARP）…";
            Log.Info("开始搜索学生机：" + ips.Count + " 个地址（" + ips[0] + " 起）。");

            Task.Factory.StartNew(delegate
            {
                List<string> alive = new List<string>();
                object aliveLock = new object();
                int pinged = 0;
                try
                {
                    Parallel.ForEach(ips, new ParallelOptions { MaxDegreeOfParallelism = 64, CancellationToken = token }, delegate(string ip)
                    {
                        if (Remote.IsAlive(ip)) { lock (aliveLock) alive.Add(ip); }
                        int n = Interlocked.Increment(ref pinged);
                        if (n % 6 == 0 && ui != null)
                        {
                            int val = n;
                            string cur = ip;
                            ui.Post(delegate
                            {
                                if (IsDisposed || Disposing) return;
                                _bar.Value = Math.Min(_bar.Maximum, val);
                                _status.Text = "正在探测 " + cur + "…（" + val + " / " + ips.Count + "）";
                            }, null);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                }

                alive.Sort(delegate(string a, string b) { return CompareIp(a, b); });

                if (ui != null)
                {
                    try
                    {
                        ui.Send(delegate
                        {
                            if (IsDisposed || Disposing) return;
                            _bar.Value = 0;
                            _bar.Maximum = Math.Max(1, alive.Count);
                            _status.Text = "在线 " + alive.Count + " 台，正在确认哪些装了 UWF…";
                            foreach (string ip in alive)
                            {
                                Host h = new Host();
                                h.Ip = ip;
                                h.Selected = false;
                                _hosts.Add(h);
                                int idx = _grid.Rows.Add();
                                DataGridViewRow row = _grid.Rows[idx];
                                row.Tag = h;
                                row.Cells["sel"].Value = false;
                                row.Cells["ip"].Value = ip;
                                row.Cells["name"].Value = "…";
                                row.Cells["uwf"].Value = "…";
                                row.Cells["state"].Value = "探测中";
                                row.Cells["state"].Style.ForeColor = Ui.Accent;
                                _rows[ip] = row;
                            }
                        }, null);
                    }
                    catch
                    {
                    }
                }

                int probed = 0;
                int uwfCount = 0;
                int noUwfCount = 0;
                int failedCount = 0;
                try
                {
                    Parallel.ForEach(alive, new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = token }, delegate(string ip)
                    {
                        Remote.ProbeResult pr = Remote.Probe(_targetFor(ip), 10);
                        if (pr.Name.Length == 0 && pr.WmiOk)
                        {
                            try
                            {
                                string host = Dns.GetHostEntry(ip).HostName;
                                int dot = host.IndexOf('.');
                                pr.Name = dot > 0 ? host.Substring(0, dot) : host;
                            }
                            catch
                            {
                            }
                        }
                        if (pr.UwfOk) Interlocked.Increment(ref uwfCount);
                        else if (pr.NoUwf) Interlocked.Increment(ref noUwfCount);
                        else Interlocked.Increment(ref failedCount);

                        int n = Interlocked.Increment(ref probed);
                        if (ui != null)
                        {
                            ui.Post(delegate
                            {
                                if (IsDisposed || Disposing) return;
                                ApplyProbe(ip, pr);
                                _bar.Value = Math.Min(_bar.Maximum, n);
                            }, null);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                }

                bool cancelled = token.IsCancellationRequested;
                if (ui != null)
                {
                    ui.Post(delegate
                    {
                        if (IsDisposed || Disposing) return;
                        _running = false;
                        _btnStart.Enabled = true;
                        _btnStop.Enabled = false;
                        _bar.Visible = false;
                        _btnAdd.Enabled = _hosts.Count > 0;
                        _status.Text = (cancelled ? "已停止：" : "搜索完成：") +
                                       "在线 " + _hosts.Count + " 台 · UWF " + uwfCount + " 台" +
                                       (noUwfCount > 0 ? " · 无 UWF " + noUwfCount + " 台" : "") +
                                       (failedCount > 0 ? " · 读不到 " + failedCount + " 台" : "") + "。";
                        Log.Info("搜索完成：在线 " + _hosts.Count + " 台，UWF " + uwfCount + " 台，无 UWF " + noUwfCount + " 台，读取失败 " + failedCount + " 台。");
                        UpdateCounts();
                    }, null);
                }
            }, token);
        }

        private static int CompareIp(string a, string b)
        {
            long x = IpValue(a), y = IpValue(b);
            if (x == y) return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return x < y ? -1 : 1;
        }

        private static long IpValue(string ip)
        {
            try
            {
                string[] p = ip.Split('.');
                long v = 0;
                for (int i = 0; i < 4 && i < p.Length; i++)
                {
                    int o;
                    if (!int.TryParse(p[i], out o)) return 0;
                    v = (v << 8) | (long)(byte)o;
                }
                return v;
            }
            catch
            {
                return 0;
            }
        }

        private void ApplyProbe(string ip, Remote.ProbeResult pr)
        {
            DataGridViewRow row;
            if (!_rows.TryGetValue(ip, out row)) return;
            Host h = row.Tag as Host;
            if (h == null) return;
            h.Probe = pr;
            h.Name = pr.Name;

            row.Cells["name"].Value = pr.Name.Length > 0 ? pr.Name : "—";
            if (pr.UwfOk)
            {
                row.Cells["uwf"].Value = "有";
                row.Cells["uwf"].Style.ForeColor = Ui.Green;
                if (pr.Servicing)
                {
                    row.Cells["state"].Value = "维修模式";
                    row.Cells["state"].Style.ForeColor = Ui.Amber;
                    row.Cells["note"].Value = "维修模式运行中（Windows 更新），改动不会还原";
                }
                else if (pr.Protected)
                {
                    row.Cells["state"].Value = "保护已开启";
                    row.Cells["state"].Style.ForeColor = Ui.Green;
                    row.Cells["note"].Value = "";
                }
                else
                {
                    row.Cells["state"].Value = "未开启保护";
                    row.Cells["state"].Style.ForeColor = Ui.Amber;
                    row.Cells["note"].Value = "过滤器当前是关闭的";
                }
                if (!h.Selected)
                {
                    h.Selected = true;
                    row.Cells["sel"].Value = true;
                }
            }
            else if (pr.NoUwf)
            {
                row.Cells["uwf"].Value = "无";
                row.Cells["uwf"].Style.ForeColor = Ui.Muted;
                row.Cells["state"].Value = "不是 UWF 机器";
                row.Cells["state"].Style.ForeColor = Ui.Muted;
                row.Cells["note"].Value = "未安装 UWF 组件（专业版 / 家庭版不支持）";
                h.Selected = false;
                row.Cells["sel"].Value = false;
            }
            else if (pr.WmiOk)
            {
                row.Cells["uwf"].Value = "?";
                row.Cells["uwf"].Style.ForeColor = Ui.Amber;
                row.Cells["state"].Value = "读取失败";
                row.Cells["state"].Style.ForeColor = Ui.Red;
                row.Cells["note"].Value = pr.Error.Length > 0 ? pr.Error : "WMI 已连上，但读不到 UWF";
                h.Selected = false;
                row.Cells["sel"].Value = false;
            }
            else
            {
                row.Cells["uwf"].Value = "?";
                row.Cells["uwf"].Style.ForeColor = Ui.Muted;
                row.Cells["state"].Value = "无法连接";
                row.Cells["state"].Style.ForeColor = Ui.Red;
                row.Cells["note"].Value = pr.Error.Length > 0 ? pr.Error : "WMI 连接失败（账号 / 防火墙 / 目标机未准备）";
                h.Selected = false;
                row.Cells["sel"].Value = false;
            }
            ApplyFilterRow(row, h);
            UpdateCounts();
        }

        private void ApplyFilter()
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                ApplyFilterRow(row, row.Tag as Host);
            }
        }

        private void ApplyFilterRow(DataGridViewRow row, Host h)
        {
            if (h == null) return;
            bool visible = true;
            if (_onlyUwf != null && _onlyUwf.Checked)
                visible = h.Probe != null && h.Probe.UwfOk;
            if (_onlyFailed != null && _onlyFailed.Checked)
                visible = visible && h.Probe != null && (h.Probe.ReadFailed || !h.Probe.WmiOk);
            row.Visible = visible;
        }

        private void SetAll(bool on)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                Host h = row.Tag as Host;
                if (h == null) continue;
                h.Selected = on;
                row.Cells["sel"].Value = on;
            }
            UpdateCounts();
        }

        private void SelectUwfOnly()
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                Host h = row.Tag as Host;
                if (h == null) continue;
                bool on = h.Probe != null && h.Probe.UwfOk;
                h.Selected = on;
                row.Cells["sel"].Value = on;
            }
            UpdateCounts();
        }

        private void UpdateCounts()
        {
            int n = 0;
            foreach (Host h in _hosts) if (h.Selected) n++;
            if (_btnAdd != null)
            {
                _btnAdd.Text = "加入机器清单 (" + n + ")";
                _btnAdd.Enabled = n > 0;
            }
        }

        private void Accept()
        {
            Result.Clear();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Host h in _hosts)
            {
                if (!h.Selected) continue;
                if (h.Probe == null) continue;   // 还没探测完的机器不要加进清单：会显示“无法连接”且说明列一片空白
                string name = h.Name.Length > 0 ? h.Name : h.Ip;
                if (!seen.Add(name)) continue;
                Machine m = new Machine();
                m.Name = name;
                if (h.Name.Length > 0) m.Note = h.Ip;
                m.Group = "扫描 " + PrefixOf(h.Ip);
                m.State = new RemoteState();
                m.State.Ok = h.Probe != null;
                m.State.Transport = RemoteTransport.Wmi;
                m.State.UwfPresent = h.Probe != null && h.Probe.UwfOk;
                m.State.FilterCurrent = h.Probe != null && h.Probe.FilterCurrent;
                m.State.FilterNext = m.State.FilterCurrent;
                m.State.ServicingCurrent = h.Probe != null && h.Probe.Servicing;
                m.State.ServicingNext = m.State.ServicingCurrent;
                m.State.Error = h.Probe != null && !h.Probe.WmiOk ? h.Probe.Error : "";
                m.StatusText = "正常";
                m.LastSeen = DateTime.Now;
                if (m.State.UwfPresent)
                {
                    VolumeState v = new VolumeState();
                    v.Letter = "C:";
                    v.ProtectedCurrent = m.State.FilterCurrent;
                    v.ProtectedNext = m.State.FilterCurrent;
                    m.State.Volumes.Add(v);
                }
                Result.Add(m);
            }
            if (Result.Count == 0)
            {
                MessageBox.Show(this, "没有勾选任何机器。");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string PrefixOf(string ip)
        {
            int idx = ip.LastIndexOf('.');
            return idx > 0 ? ip.Substring(0, idx + 1) + "x" : ip;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_cts != null)
            {
                try { _cts.Cancel(); } catch { }
            }
            base.OnFormClosing(e);
        }
    }
}
