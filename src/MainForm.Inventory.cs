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
        private ListView _invList;
        private TextBox _invUser;
        private TextBox _invPass;
        private NumericUpDown _invParallel;
        private Label _invCount;

        private Panel BuildInventoryPage()
        {
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Ui.Bg;

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 2;
            grid.RowCount = 1;
            grid.BackColor = Ui.Bg;
            grid.Padding = new Padding(Ui.PagePad, 16, Ui.PagePad, 16);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
            page.Controls.Add(grid);

            // 左：机器列表
            RoundedPanel left = new RoundedPanel();
            left.Dock = DockStyle.Fill;
            left.Margin = new Padding(0, 0, 14, 0);
            grid.Controls.Add(left, 0, 0);

            Label lt = Ui.L("机器清单", Ui.F10B, Ui.TextColor);
            lt.Location = new Point(20, 14);
            left.Controls.Add(lt);

            _invCount = Ui.L("", Ui.F85, Ui.Muted);
            _invCount.Location = new Point(104, 19);
            left.Controls.Add(_invCount);

            _invList = new ListView();
            Ui.StyleList(_invList);
            _invList.View = View.Details;
            _invList.MultiSelect = true;
            _invList.GridLines = false;
            _invList.Location = new Point(20, 46);
            _invList.Size = new Size(left.Width - 40, left.Height - 166);
            _invList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _invList.Columns.Add("计算机名 / IP", 180);
            _invList.Columns.Add("分组", 100);
            _invList.Columns.Add("备注", 200);
            _invList.ClientSizeChanged += delegate { FitMachineColumns(); };
            _invList.DoubleClick += delegate { EditSelectedMachine(); };
            left.Controls.Add(_invList);

            // 固定 2 行 × 4 列：窗口变窄时只缩按钮、不换行，避免第三行被底部裁掉
            TableLayoutPanel ops = new TableLayoutPanel();
            ops.ColumnCount = 4;
            ops.RowCount = 2;
            ops.Location = new Point(20, left.Height - 108);
            ops.Size = new Size(left.Width - 40, 92);
            ops.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            ops.BackColor = Ui.CardBg;
            for (int c = 0; c < 4; c++) ops.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            ops.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            ops.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            Button add = Ui.B("手动添加…", true);
            add.Click += delegate { AddMachines(); };
            Button addRange = Ui.B("按编号添加…");
            addRange.Click += delegate { AddMachineRange(); };
            Button import = Ui.B("从文件导入");
            import.Click += delegate { ImportMachines(); };
            Button scan = Ui.B("扫描网段");
            scan.Click += delegate { ScanSubnet(); };
            Button del = Ui.Danger("删除所选");
            del.Click += delegate { DeleteSelectedMachines(); };
            Button clear = Ui.Danger("清空清单");
            clear.Click += delegate
            {
                if (_fleet.Count == 0) return;
                if (!Confirm("清空机器清单", "将从清单中移除全部 " + _fleet.Count + " 台计算机（不会影响机器本身）。\r\n\r\n确定继续？")) return;
                _fleet.Clear();
                AfterFleetChanged();
            };
            Button exportList = Ui.B("导出清单");
            exportList.Click += delegate { ExportMachines(); };
            Button check = Ui.B("连接体检");
            check.Click += delegate { DiagnoseChecked(); };

            Button[] opButtons = { add, addRange, import, scan, del, clear, exportList, check };
            for (int i = 0; i < opButtons.Length; i++)
            {
                opButtons[i].Dock = DockStyle.Fill;
                opButtons[i].Margin = new Padding(0, 0, 8, 8);
                ops.Controls.Add(opButtons[i], i % 4, i / 4);
            }
            left.Controls.Add(ops);

            // 右：账号与准备（紧凑双卡，不需要滚动）
            FlowLayoutPanel right = Ui.Stack();
            right.Padding = new Padding(0, 0, 0, 0);
            right.AutoScroll = false;
            grid.Controls.Add(right, 1, 0);

            Card cred = new Card("连接账号", 226);
            Label cl = Ui.Wrap("所有学生机用同一个管理员账号连接。工作组填「计算机名\\管理员」，域环境填「域\\账号」。", Ui.F85, Ui.Muted);
            cl.Location = new Point(0, 0);
            cl.Size = new Size(380, 34);
            cred.Body.Controls.Add(cl);

            Label ul = Ui.L("用户名", Ui.F85, Ui.Muted);
            ul.Location = new Point(0, 40);
            cred.Body.Controls.Add(ul);
            _invUser = new TextBox();
            Ui.StyleInput(_invUser);
            _invUser.Location = new Point(0, 60);
            _invUser.Width = 196;
            cred.Body.Controls.Add(_invUser);

            Label pl = Ui.L("密码", Ui.F85, Ui.Muted);
            pl.Location = new Point(212, 40);
            cred.Body.Controls.Add(pl);
            _invPass = new TextBox();
            Ui.StyleInput(_invPass);
            _invPass.UseSystemPasswordChar = true;
            _invPass.Location = new Point(212, 60);
            _invPass.Width = 148;
            cred.Body.Controls.Add(_invPass);

            Label nl = Ui.L("同时连接的机器数", Ui.F85, Ui.Muted);
            nl.Location = new Point(0, 98);
            cred.Body.Controls.Add(nl);
            _invParallel = new NumericUpDown();
            _invParallel.Minimum = 1;
            _invParallel.Maximum = 32;
            _invParallel.Value = 12;
            _invParallel.Font = Ui.F9;
            _invParallel.Location = new Point(0, 118);
            _invParallel.Width = 80;
            cred.Body.Controls.Add(_invParallel);

            Button save = Ui.B("保存账号设置", true);
            save.Location = new Point(100, 114);
            save.Width = 124;
            save.Height = 30;
            save.Click += delegate
            {
                _settings.User = (_invUser.Text ?? "").Trim();
                _settings.Password = _invPass.Text ?? "";
                _settings.Parallel = (int)_invParallel.Value;
                if (!SettingsStore.Save(_settings))
                {
                    Log.Error("连接账号没能保存到磁盘。");
                    MessageBox.Show(this, "保存失败：账号设置没能写入磁盘（详见操作日志）。\r\n\r\n密码没能加密保存，请重试。",
                                    "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Log.Info("已保存连接账号（密码用当前用户密钥加密保存在本机）。");
                MessageBox.Show(this, "已保存。\r\n\r\n密码使用 Windows 的 DPAPI 加密，只有当前 Windows 账号能解密。", "完成",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            cred.Body.Controls.Add(save);
            right.Controls.Add(cred);

            Card prep = new Card("学生机连接说明", 196);
            Label prepText = Ui.Wrap(
                "每台学生机可以单独填写 IP、管理员账号和密码。\r\n未单独设置时，远程连接使用上方的默认账号。",
                Ui.F9, Ui.Muted);
            prepText.Location = new Point(0, 0);
            prepText.Size = new Size(380, 70);
            prep.Body.Controls.Add(prepText);

            Button diagAll = Ui.B("全部连接体检");
            diagAll.Location = new Point(0, 78);
            diagAll.Width = 142;
            diagAll.Height = 32;
            diagAll.Click += delegate { DiagnoseMachines(new List<Machine>(_fleet)); };
            prep.Body.Controls.Add(diagAll);
            right.Controls.Add(prep);

            Label saveAt = Ui.Wrap("清单与账号保存在：\r\n" + Paths.DataDir + "（换教师机复制该文件夹）", Ui.F85, Ui.MutedLight);
            saveAt.Location = new Point(0, 0);
            saveAt.Size = new Size(500, 34);
            right.Controls.Add(saveAt);

            return page;
        }

        /// <summary>开始远程安装前的母盘检查闸口：以本机实测结果为准，不再用"用户说他确认过了"放行。</summary>
        private bool EnsureMasterChecks()
        {
            List<AuditResult> results = MasterAudit.HasFresh ? MasterAudit.Last : null;
            if (results == null)
            {
                // 母盘检查十几项、每项最坏几秒：放后台 + 遮罩。原来同步跑在 UI 线程，
                // 最坏能假死几十秒，弹窗也卡在后面出不来。
                BusyForm busy = new BusyForm("正在检查母盘条件", "正在逐项检查 WMI / 共享 / 防火墙 / UWF…", 0, null);
                List<AuditResult>[] slot = new List<AuditResult>[1];
                System.Threading.Tasks.Task.Factory.StartNew(delegate
                {
                    try { slot[0] = MasterAudit.RunAll(); }
                    catch (Exception ex) { Log.Error("母盘检查异常：" + ex.Message); }
                }).ContinueWith(delegate
                {
                    System.Threading.SynchronizationContext ui = System.Threading.SynchronizationContext.Current;
                    Action done = delegate { busy.CloseFromOwner(); };
                    if (ui != null) ui.Post(delegate { try { done(); } catch { } }, null);
                    else try { done(); } catch { }
                });
                busy.ShowDialog(this);
                try { busy.Dispose(); } catch { }
                results = slot[0];
                if (results == null) results = new List<AuditResult>();
            }

            AuditVerdict v = MasterAudit.Verdict(results);
            if (v.RemoteReady) return true;

            StringBuilder tips = new StringBuilder();
            foreach (AuditResult r in results)
            {
                if (!r.Ok && r.Required && !string.Equals(r.Category, MasterAudit.CatMaster, StringComparison.Ordinal))
                    tips.AppendLine("• " + r.Name + "：" + Ui.Short(r.Message, 70));
            }

            DialogResult r2 = MessageBox.Show(this,
                "母盘放行检查没有通过：\r\n\r\n" + v.Headline + "\r\n\r\n" + tips +
                "\r\n没放行就做母盘，对拷后会出现「全部连不上、装不进软件」。\r\n\r\n" +
                "【是】去「母盘放行检查」处理（推荐）\r\n【否】先不管，继续远程安装",
                "母盘放行检查", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (r2 == DialogResult.Yes)
            {
                SelectPage(3);
                return false;
            }
            Log.Warn("母盘放行检查未通过，用户选择继续：" + Ui.Short(v.Detail, 200));
            return true;
        }

        /// <summary>允许 IP，或只含字母/数字/-/_/. 的主机名；挡住带空格、分号之类的脏数据。</summary>
        private static bool IsValidHost(string host)
        {
            System.Net.IPAddress addr;
            if (System.Net.IPAddress.TryParse(host, out addr)) return true;
            if (host.Length == 0 || host.Length > 63) return false;
            foreach (char c in host)
            {
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')) return false;
            }
            return true;
        }

        private void RefreshInventory()
        {
            if (_invList == null) return;
            _invList.BeginUpdate();
            _invList.Items.Clear();
            foreach (Machine m in _fleet)
            {
                ListViewItem it = new ListViewItem(m.Name);
                it.SubItems.Add(m.Group);
                it.SubItems.Add(m.Note);
                it.Tag = m;
                _invList.Items.Add(it);
            }
            _invList.EndUpdate();
            FitMachineColumns();
            _invCount.Text = "共 " + _fleet.Count + " 台";
            // 只在输入框为空时回填：切页刷新不能把老师刚输入、还没点“保存账号设置”的账号冲掉
            if ((_invUser.Text ?? "").Trim().Length == 0) _invUser.Text = _settings.User;
            if ((_invPass.Text ?? "").Length == 0) _invPass.Text = _settings.Password;
            _invParallel.Value = Math.Max(1, Math.Min(32, _settings.Parallel));
        }

        private void FitMachineColumns()
        {
            if (_invList == null || _invList.Columns.Count < 3) return;
            int rest = _invList.ClientSize.Width - 4 - _invList.Columns[0].Width - _invList.Columns[1].Width;
            if (rest > 120) _invList.Columns[2].Width = rest;
        }
        private void AfterFleetChanged()
        {
            _byName.Clear();
            foreach (Machine m in _fleet) _byName[m.Name] = m;
            if (!FleetStore.Save(_fleet))
                Log.Warn("机器清单没能保存到磁盘：本次改动只在内存里，重启后会丢。");
            RebuildGrid();
            RefreshInventory();
            UpdateFleetStats();
        }

        private void AddMachines()
        {
            using (Form f = new Form())
            {
                f.Text = "手动添加学生机";
                f.Font = Ui.F9;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ClientSize = new Size(430, 340);
                f.BackColor = Ui.CardBg;

                Label ipLabel = Ui.L("学生机 IP / 计算机名", Ui.F9, Ui.TextColor);
                ipLabel.Location = new Point(18, 16);
                f.Controls.Add(ipLabel);
                TextBox ip = new TextBox();
                Ui.StyleInput(ip);
                ip.Location = new Point(18, 40);
                ip.Width = 390;
                f.Controls.Add(ip);

                Label userLabel = Ui.L("此学生机管理员账号（留空则使用默认账号）", Ui.F9, Ui.TextColor);
                userLabel.Location = new Point(18, 82);
                f.Controls.Add(userLabel);
                TextBox user = new TextBox();
                Ui.StyleInput(user);
                user.Location = new Point(18, 106);
                user.Width = 390;
                f.Controls.Add(user);

                Label passLabel = Ui.L("密码", Ui.F9, Ui.TextColor);
                passLabel.Location = new Point(18, 148);
                f.Controls.Add(passLabel);
                TextBox pass = new TextBox();
                Ui.StyleInput(pass);
                pass.UseSystemPasswordChar = true;
                pass.Location = new Point(18, 172);
                pass.Width = 390;
                f.Controls.Add(pass);

                Label groupLabel = Ui.L("分组（可选）", Ui.F9, Ui.TextColor);
                groupLabel.Location = new Point(18, 214);
                f.Controls.Add(groupLabel);
                TextBox group = new TextBox();
                Ui.StyleInput(group);
                group.Location = new Point(18, 238);
                group.Width = 180;
                f.Controls.Add(group);

                Button ok = Ui.B("添加学生机", true);
                ok.Location = new Point(220, 286);
                ok.Width = 112;
                ok.DialogResult = DialogResult.OK;
                f.Controls.Add(ok);
                Button cancel = Ui.B("取消");
                cancel.Location = new Point(340, 286);
                cancel.Width = 68;
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;

                if (f.ShowDialog(this) != DialogResult.OK) return;
                string host = ip.Text.Trim();
                if (host.Length == 0) { MessageBox.Show(this, "请输入学生机 IP 或计算机名。"); return; }
                if (!IsValidHost(host))
                {
                    MessageBox.Show(this, "计算机名只能包含字母、数字、- 和 _，最长 63 个字符；也可以直接填 IP 地址。\r\n\r\n" +
                                    "带空格、分号等字符的名字会被当成非法主机名，连接必然失败。");
                    return;
                }
                Machine existing;
                if (_byName.TryGetValue(host, out existing))
                {
                    existing.User = user.Text.Trim();
                    existing.Password = pass.Text;
                    if (group.Text.Trim().Length > 0) existing.Group = group.Text.Trim();
                    Log.Info("更新学生机独立连接账号：" + host);
                }
                else
                {
                    Machine m = new Machine();
                    m.Name = host;
                    m.User = user.Text.Trim();
                    m.Password = pass.Text;
                    m.Group = group.Text.Trim();
                    _fleet.Add(m);
                    _byName[host] = m;
                    Log.Info("手动添加学生机：" + host);
                }
                AfterFleetChanged();
                SelectPage(0);
            }
        }

        private int MergeNames(string text, string group, string note)
        {
            int added = 0;
            foreach (string raw in SplitNames(text))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                if (_byName.ContainsKey(name)) continue;
                Machine m = new Machine();
                m.Name = name;
                m.Group = group ?? "";
                m.Note = note ?? "";
                _fleet.Add(m);
                _byName[name] = m;
                added++;
            }
            if (added > 0) Log.Info("添加了 " + added + " 台计算机。");
            return added;
        }

        private IEnumerable<string> SplitNames(string text)
        {
            if (text == null) yield break;
            foreach (string line in text.Replace("\r", "\n").Split('\n'))
            {
                foreach (string part in line.Split(new char[] { ',', '，', ';', '；', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    yield return part;
                }
            }
        }

        private void AddMachineRange()
        {
            using (Form f = new Form())
            {
                f.Text = "按编号批量添加";
                f.Font = Ui.F9;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ClientSize = new Size(480, 250);
                f.BackColor = Ui.CardBg;

                Label l1 = Ui.L("名称前缀", Ui.F9, Ui.TextColor);
                l1.Location = new Point(16, 14);
                f.Controls.Add(l1);
                TextBox prefix = new TextBox();
                Ui.StyleInput(prefix);
                prefix.Text = "PC-";
                prefix.Location = new Point(16, 36);
                prefix.Width = 150;
                f.Controls.Add(prefix);

                Label l2 = Ui.L("起始编号", Ui.F9, Ui.TextColor);
                l2.Location = new Point(186, 14);
                f.Controls.Add(l2);
                NumericUpDown from = new NumericUpDown();
                from.Minimum = 1;
                from.Maximum = 9999;
                from.Value = 1;
                from.Location = new Point(186, 36);
                from.Width = 90;
                f.Controls.Add(from);

                Label l3 = Ui.L("结束编号", Ui.F9, Ui.TextColor);
                l3.Location = new Point(296, 14);
                f.Controls.Add(l3);
                NumericUpDown to = new NumericUpDown();
                to.Minimum = 1;
                to.Maximum = 9999;
                to.Value = 50;
                to.Location = new Point(296, 36);
                to.Width = 90;
                f.Controls.Add(to);

                Label l4 = Ui.L("编号位数（2 → PC-01）", Ui.F9, Ui.TextColor);
                l4.Location = new Point(16, 74);
                f.Controls.Add(l4);
                NumericUpDown digits = new NumericUpDown();
                digits.Minimum = 1;
                digits.Maximum = 4;
                digits.Value = 2;
                digits.Location = new Point(16, 96);
                digits.Width = 90;
                f.Controls.Add(digits);

                Label l5 = Ui.L("分组", Ui.F9, Ui.TextColor);
                l5.Location = new Point(140, 74);
                f.Controls.Add(l5);
                TextBox group = new TextBox();
                Ui.StyleInput(group);
                group.Text = "机房1";
                group.Location = new Point(140, 96);
                group.Width = 150;
                f.Controls.Add(group);

                Label preview = Ui.L("", Ui.F85, Ui.Muted);
                preview.Location = new Point(16, 132);
                preview.AutoSize = true;
                f.Controls.Add(preview);

                EventHandler update = delegate
                {
                    preview.Text = "将添加：" + prefix.Text + ((int)from.Value).ToString("D" + (int)digits.Value) +
                                   " … " + prefix.Text + ((int)to.Value).ToString("D" + (int)digits.Value);
                };
                prefix.TextChanged += update;
                from.ValueChanged += update;
                to.ValueChanged += update;
                digits.ValueChanged += update;
                update(null, null);

                Button ok = Ui.B("添加", true);
                ok.Location = new Point(282, 180);
                ok.Width = 88;
                ok.DialogResult = DialogResult.OK;
                f.Controls.Add(ok);
                Button cancel = Ui.B("取消");
                cancel.Location = new Point(376, 180);
                cancel.Width = 88;
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;

                if (f.ShowDialog(this) != DialogResult.OK) return;

                int a = (int)from.Value, b = (int)to.Value;
                if (b < a) { MessageBox.Show(this, "结束编号不能小于起始编号。"); return; }
                StringBuilder sb = new StringBuilder();
                for (int i = a; i <= b; i++) sb.AppendLine(prefix.Text + i.ToString("D" + (int)digits.Value));
                int added = MergeNames(sb.ToString(), group.Text.Trim(), "");
                Log.Info("按编号添加 " + added + " 台计算机。");
                AfterFleetChanged();
            }
        }

        private void ImportMachines()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "导入机器清单";
            dlg.Filter = "文本 / CSV|*.txt;*.csv|所有文件|*.*";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            int added = 0;
            string[] lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#")) continue;
                string[] parts = line.Split(new char[] { ',', '\t', ';' });
                string name = parts.Length > 0 ? parts[0].Trim() : "";
                if (name.Length == 0) continue;
                if (name == "计算机" || string.Equals(name, "name", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "计算机名", StringComparison.OrdinalIgnoreCase)) continue;
                if (_byName.ContainsKey(name)) continue;
                Machine m = new Machine();
                m.Name = name;
                m.Group = parts.Length > 1 ? parts[1].Trim() : "";
                m.Note = parts.Length > 2 ? parts[2].Trim() : "";
                _fleet.Add(m);
                _byName[name] = m;
                added++;
            }
            Log.Info("从文件导入 " + added + " 台计算机：" + dlg.FileName);
            MessageBox.Show(this, "导入了 " + added + " 台计算机。", "完成");
            AfterFleetChanged();
        }

        private void ExportMachines()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.FileName = "机器清单.csv";
            dlg.Filter = "CSV 文件|*.csv";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("计算机,分组,备注");
            foreach (Machine m in _fleet) sb.AppendLine(Csv(m.Name) + "," + Csv(m.Group) + "," + Csv(m.Note));
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            Log.Info("已导出机器清单：" + dlg.FileName);
        }

        private void DeleteSelectedMachines()
        {
            if (_invList.SelectedItems.Count == 0) { MessageBox.Show(this, "请先在列表中选择要删除的计算机。"); return; }
            if (!Confirm("删除计算机", "将从清单中移除 " + _invList.SelectedItems.Count + " 台计算机（不影响机器本身）。")) return;
            foreach (ListViewItem it in _invList.SelectedItems)
            {
                Machine m = it.Tag as Machine;
                if (m != null) _fleet.Remove(m);
            }
            AfterFleetChanged();
        }

        private void EditSelectedMachine()
        {
            if (_invList.SelectedItems.Count != 1) { MessageBox.Show(this, "请选择一台计算机。"); return; }
            Machine m = _invList.SelectedItems[0].Tag as Machine;
            if (m == null) return;

            string group = Ui.Prompt("修改分组", "计算机：" + m.Name, m.Group);
            if (group == null) return;
            string note = Ui.Prompt("修改备注", "计算机：" + m.Name, m.Note);
            if (note == null) return;
            m.Group = group;
            m.Note = note;
            AfterFleetChanged();
        }

        private void ScanSubnet()
        {
            using (Form f = new Form())
            {
                f.Text = "扫描网段";
                f.Font = Ui.F9;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ClientSize = new Size(460, 300);
                f.BackColor = Ui.CardBg;

                Label l1 = Ui.L("网段前缀（写到第三段，例如 192.168.1.）", Ui.F9, Ui.TextColor);
                l1.Location = new Point(16, 14);
                f.Controls.Add(l1);
                TextBox prefix = new TextBox();
                Ui.StyleInput(prefix);
                List<string> local = DiscoveryForm.LocalPrefixes();
                prefix.Text = local.Count > 0 ? local[0] : "192.168.1.";
                prefix.Location = new Point(16, 36);
                prefix.Width = 200;
                f.Controls.Add(prefix);

                Label l2 = Ui.L("起始", Ui.F9, Ui.TextColor);
                l2.Location = new Point(236, 14);
                f.Controls.Add(l2);
                NumericUpDown from = new NumericUpDown();
                from.Minimum = 1; from.Maximum = 254; from.Value = 1;
                from.Location = new Point(236, 36); from.Width = 80;
                f.Controls.Add(from);

                Label l3 = Ui.L("结束", Ui.F9, Ui.TextColor);
                l3.Location = new Point(336, 14);
                f.Controls.Add(l3);
                NumericUpDown to = new NumericUpDown();
                to.Minimum = 1; to.Maximum = 254; to.Value = 254;
                to.Location = new Point(336, 36); to.Width = 80;
                f.Controls.Add(to);

                CheckBox resolve = new CheckBox();
                resolve.Text = "尝试解析计算机名（慢）";
                resolve.Font = Ui.F9;
                resolve.AutoSize = true;
                resolve.Location = new Point(16, 76);
                f.Controls.Add(resolve);

                Label hint = Ui.Wrap("扫描会先 ping，再探 445 / 135 端口，最后用 ARP 兜底：禁 ping 的机器也能被发现。加入清单后点「刷新全部」确认哪些装了 UWF。", Ui.F85, Ui.Muted);
                hint.Location = new Point(16, 104);
                hint.Size = new Size(420, 44);
                f.Controls.Add(hint);

                ProgressBar bar = new ProgressBar();
                bar.Location = new Point(16, 154);
                bar.Size = new Size(420, 14);
                bar.Visible = false;
                f.Controls.Add(bar);

                Label status = Ui.L("", Ui.F85, Ui.Muted);
                status.Location = new Point(16, 174);
                status.AutoSize = true;
                f.Controls.Add(status);

                Button start = Ui.B("开始扫描", true);
                start.Location = new Point(16, 208);
                start.Width = 110;
                start.Click += delegate
                {
                    string p = prefix.Text.Trim();
                    if (!p.EndsWith(".")) p += ".";
                    if (from.Value > to.Value) { MessageBox.Show(f, "起始不能大于结束。"); return; }

                    // 前缀写错（多写一段、乱码）时别白扫 254 个地址
                    System.Net.IPAddress probeAddr;
                    if (!System.Net.IPAddress.TryParse(p + "1", out probeAddr) ||
                        probeAddr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        MessageBox.Show(f, "网段格式不对，只能写到第三段再加点，例如：192.168.1.");
                        return;
                    }
                    int i1 = (int)from.Value;
                    int i2 = (int)to.Value;
                    bool needNames = resolve.Checked;
                    start.Enabled = false;
                    bar.Visible = true;
                    bar.Minimum = 0;
                    bar.Maximum = 100;
                    bar.Value = 0;
                    status.Text = "扫描中…";

                    // 扫描放后台线程：界面保持可响应，进度与结果封送回 UI 线程
                    SynchronizationContext ui = SynchronizationContext.Current;
                    Task.Factory.StartNew(delegate
                    {
                        List<string> found = null;
                        Exception scanErr = null;
                        try
                        {
                            found = SubnetScanner.Scan(p, i1, i2, needNames,
                                delegate(int done, int total, string curIp)
                                {
                                    int pct = Math.Min(100, done * 100 / Math.Max(1, total));
                                    if (ui != null) ui.Post(delegate
                                    {
                                        if (bar.IsDisposed) return;
                                        bar.Value = pct;
                                        // 显示正在扫哪个 IP：两百多个地址扫几十秒，只有一个百分比看着像卡死
                                        if (status != null && !status.IsDisposed)
                                            status.Text = "正在探测 " + curIp + "…（" + done + " / " + total + "）";
                                    }, null);
                                },
                                delegate { return f.IsDisposed; });
                        }
                        catch (Exception ex)
                        {
                            scanErr = ex;
                        }
                        List<string> result = found;
                        Exception err = scanErr;
                        if (ui == null) return;
                        ui.Post(delegate
                        {
                            if (f.IsDisposed || Disposing) return;
                            start.Enabled = true;
                            bar.Visible = false;
                            if (err != null)
                            {
                                status.Text = "扫描失败。";
                                MessageBox.Show(f, "扫描失败：" + err.Message);
                                return;
                            }
                            status.Text = "找到 " + (result == null ? 0 : result.Count) + " 台在线主机。";
                            if (result == null) return;
                            int added = 0;
                            foreach (string item in result)
                            {
                                string[] parts = item.Split('|');
                                string name = parts[0];
                                if (_byName.ContainsKey(name)) continue;
                                Machine m = new Machine();
                                m.Name = name;
                                m.Group = "扫描 " + p + "x";
                                if (parts.Length > 1 && !string.Equals(parts[0], parts[1], StringComparison.OrdinalIgnoreCase)) m.Note = parts[1];
                                _fleet.Add(m);
                                _byName[name] = m;
                                added++;
                            }
                            Log.Info("扫描 " + p + " 段，找到 " + result.Count + " 台，新增 " + added + " 台。");
                            AfterFleetChanged();
                            MessageBox.Show(f, "扫描完成：在线 " + result.Count + " 台，新增 " + added + " 台。", "完成");
                        }, null);
                    });
                };
                f.Controls.Add(start);

                Button close = Ui.B("关闭");
                close.Location = new Point(136, 208);
                close.Width = 88;
                close.DialogResult = DialogResult.Cancel;
                f.Controls.Add(close);
                f.CancelButton = close;
                f.ShowDialog(this);
            }
        }

        private void SavePrepScript()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.FileName = "启用远程管理.cmd";
            dlg.Filter = "批处理脚本|*.cmd";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(dlg.FileName, PrepScriptText(), Encoding.Default);
            Log.Info("已生成目标机准备脚本：" + dlg.FileName);
            MessageBox.Show(this, "已生成：\r\n" + dlg.FileName + "\r\n\r\n在每台学生机上以管理员身份运行一次，然后重启。",
                            "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private string PrepScriptText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 936 >nul 2>&1");
            sb.AppendLine("setlocal");
            sb.AppendLine("net session >nul 2>&1");
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("  echo 请右键选择“以管理员身份运行”本脚本。");
            sb.AppendLine("  pause");
            sb.AppendLine("  exit /b 1");
            sb.AppendLine(")");
            sb.AppendLine("echo 正在配置远程管理：%COMPUTERNAME%");
            sb.AppendLine("echo.");
            sb.AppendLine("echo [1/5] 开启文件和打印机共享...");
            sb.AppendLine("netsh advfirewall firewall set rule group=\"文件和打印机共享\" new enable=Yes >nul 2>&1");
            sb.AppendLine("netsh advfirewall firewall set rule group=\"File and Printer Sharing\" new enable=Yes >nul 2>&1");
            sb.AppendLine("sc config LanmanServer start= auto >nul 2>&1");
            sb.AppendLine("net start LanmanServer >nul 2>&1");
            sb.AppendLine("echo [2/5] 开启 WMI...");
            sb.AppendLine("netsh advfirewall firewall set rule group=\"Windows Management Instrumentation (WMI)\" new enable=Yes >nul 2>&1");
            sb.AppendLine("netsh advfirewall firewall set rule group=\"Windows Management Instrumentation (WMI-In)\" new enable=Yes >nul 2>&1");
            sb.AppendLine("sc config Winmgmt start= auto >nul 2>&1");
            sb.AppendLine("echo [3/5] 允许远程管理员访问（工作组环境必需）...");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f >nul");
            sb.AppendLine("echo [4/5] 允许 ping...");
            sb.AppendLine("netsh advfirewall firewall add rule name=\"UWF Console ICMP\" protocol=icmpv4:8,any dir=in action=allow >nul 2>&1");
            sb.AppendLine("echo [5/5] 完成检查");
            sb.AppendLine("echo.");
            sb.AppendLine("echo 本机信息：");
            sb.AppendLine("ipconfig | findstr /i \"IPv4\"");
            sb.AppendLine("echo.");
            sb.AppendLine("echo 配置完成，请重启这台计算机，然后在教师机上点“刷新全部状态”。");
            sb.AppendLine("pause");
            return sb.ToString();
        }
    }
}
