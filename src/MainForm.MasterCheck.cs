using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UwfTeacher
{
    /// <summary>母盘放行检查：本机（母盘）WMI / SMB / 共享通道自检，做母盘前确认放行到位。</summary>
    public partial class MainForm
    {
        private class MasterCheckRow
        {
            public string Name;
            public string Category;
            public Label Icon;
            public Label Status;
        }

        private readonly List<MasterCheckRow> _mcRows = new List<MasterCheckRow>();
        private TableLayoutPanel _mcList;
        private Card _mcListCard;
        private Label _mcSummary;
        private Label _mcHint;
        private Label _mcDetail;
        private Button _mcRunBtn;
        private Button _mcFixBtn;
        private ToolTip _mcTip;
        private bool _mcRunning;
        private int _mcToken;

        private Panel BuildMasterCheckPage()
        {
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Ui.Bg;

            _mcTip = new ToolTip();
            _mcTip.AutoPopDelay = 12000;
            _mcTip.InitialDelay = 300;
            _mcTip.ReshowDelay = 100;

            FlowLayoutPanel stack = Ui.Stack();
            page.Controls.Add(stack);

            Card sum = new Card("检测结果", 252);
            _mcSummary = Ui.L("尚未检测", Ui.F15B, Ui.TextColor);
            _mcSummary.Location = new Point(0, 0);
            sum.Body.Controls.Add(_mcSummary);

            _mcHint = Ui.Wrap("点「重新检测」看看本机（母盘）有没有放行 WMI / SMB。", Ui.F9, Ui.Muted);
            _mcHint.Location = new Point(0, 34);
            _mcHint.Size = new Size(900, 20);
            sum.Body.Controls.Add(_mcHint);

            _mcDetail = Ui.Wrap("", Ui.F85, Ui.Muted);
            _mcDetail.Location = new Point(0, 58);
            _mcDetail.Size = new Size(900, 44);
            sum.Body.Controls.Add(_mcDetail);

            _mcRunBtn = Ui.B("重新检测", true);
            _mcRunBtn.Location = new Point(0, 108);
            _mcRunBtn.Width = 110;
            _mcRunBtn.Height = 32;
            _mcRunBtn.Click += delegate { RunMasterCheck(); };
            sum.Body.Controls.Add(_mcRunBtn);

            _mcFixBtn = Ui.B("一键放行修复");
            _mcFixBtn.Location = new Point(122, 108);
            _mcFixBtn.Width = 130;
            _mcFixBtn.Height = 32;
            _mcFixBtn.Click += delegate { FixMasterRelease(); };
            sum.Body.Controls.Add(_mcFixBtn);

            Button gen = Ui.B("生成启用远程管理.cmd");
            gen.Location = new Point(264, 108);
            gen.Width = 190;
            gen.Height = 32;
            gen.Click += delegate { SavePrepScript(); };
            sum.Body.Controls.Add(gen);

            Button toInventory = Ui.B("去机器清单");
            toInventory.Location = new Point(466, 108);
            toInventory.Width = 110;
            toInventory.Height = 32;
            toInventory.Click += delegate { SelectPage(2); };
            sum.Body.Controls.Add(toInventory);

            Label tip = Ui.Wrap("做母盘前先放行、全部 ✓ 再对拷；克隆出去的每台机器都靠这些通道被远程管理。", Ui.F85, Ui.MutedLight);
            tip.Location = new Point(0, 150);
            tip.Size = new Size(980, 20);
            sum.Body.Controls.Add(tip);
            stack.Controls.Add(sum);

            _mcListCard = new Card("放行检查清单", 640);
            _mcList = new TableLayoutPanel();
            _mcList.Dock = DockStyle.Fill;
            _mcList.Margin = new Padding(0);
            _mcList.BackColor = Ui.CardBg;
            _mcList.ColumnCount = 4;
            _mcList.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            _mcList.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            _mcList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _mcList.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            _mcListCard.Body.Controls.Add(_mcList);
            stack.Controls.Add(_mcListCard);

            BuildCheckRows();

            Card how = new Card("怎么用这张表", 132);
            Label ht = Ui.Wrap(
                "远程管理（必需）：SMB / WMI / 防火墙 / 账号权限全绿，教师机才能批量读写这些机器。\r\n" +
                "母盘保护（必需才算母盘就绪）：UWF 已开启、系统盘已在保护列表；否则克隆出去也不会还原。\r\n" +
                "绿色 = 已放行；橙色 ! = 建议项；红色 × = 必须处理。",
                Ui.F9, Ui.TextColor);
            ht.Location = new Point(0, 0);
            ht.Size = new Size(1000, 76);
            how.Body.Controls.Add(ht);
            stack.Controls.Add(how);

            return page;
        }

        // ---------------------------------------------------------------- 行

        private void BuildCheckRows()
        {
            _mcList.SuspendLayout();
            _mcList.Controls.Clear();
            _mcList.RowStyles.Clear();
            _mcList.RowCount = 0;
            _mcRows.Clear();

            int row = 0;
            AddHeaderRow(row++);

            List<AuditItem> items = MasterAudit.BuildItems();
            string lastCategory = null;
            foreach (AuditItem item in items)
            {
                if (!string.Equals(lastCategory, item.Category, StringComparison.Ordinal))
                {
                    lastCategory = item.Category;
                    AddCategoryRow(row++, item.Category);
                }
                AddCheckRow(row++, item);
            }

            _mcList.ResumeLayout();
            int content = 0;
            foreach (RowStyle rs in _mcList.RowStyles) content += (int)rs.Height;
            _mcListCard.Height = 46 + 8 + content + 20;
        }

        private void AddHeaderRow(int row)
        {
            _mcList.RowCount++;
            _mcList.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            Label h1 = Ui.L("", Ui.F85, Ui.Muted);
            Label h2 = Ui.L("检查项", Ui.F85, Ui.Muted);
            Label h3 = Ui.L("说明", Ui.F85, Ui.Muted);
            Label h4 = Ui.L("结果", Ui.F85, Ui.Muted);
            h2.Dock = DockStyle.Fill; h2.TextAlign = ContentAlignment.MiddleLeft;
            h3.Dock = DockStyle.Fill; h3.TextAlign = ContentAlignment.MiddleLeft;
            h4.Dock = DockStyle.Fill; h4.TextAlign = ContentAlignment.MiddleLeft;
            _mcList.Controls.Add(h1, 0, row);
            _mcList.Controls.Add(h2, 1, row);
            _mcList.Controls.Add(h3, 2, row);
            _mcList.Controls.Add(h4, 3, row);
        }

        private void AddCategoryRow(int row, string category)
        {
            _mcList.RowCount++;
            _mcList.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            Label cap = Ui.L(category == MasterAudit.CatMaster ? "母盘保护（母盘就绪必须全绿）" : "远程管理（教师机批量管理必需）",
                             Ui.F85, Ui.Muted);
            cap.Dock = DockStyle.Fill;
            cap.TextAlign = ContentAlignment.BottomLeft;
            _mcList.Controls.Add(cap, 0, row);
            _mcList.SetColumnSpan(cap, 4);
        }

        private void AddCheckRow(int row, AuditItem item)
        {
            _mcList.RowCount++;
            _mcList.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            Label icon = Ui.L("…", Ui.F11B, Ui.MutedLight);
            icon.Dock = DockStyle.Fill;
            icon.TextAlign = ContentAlignment.MiddleCenter;
            _mcList.Controls.Add(icon, 0, row);

            Label nm = Ui.L(item.Name, Ui.F9B, Ui.TextColor);
            nm.Dock = DockStyle.Fill;
            nm.TextAlign = ContentAlignment.MiddleLeft;
            nm.AutoEllipsis = true;
            _mcList.Controls.Add(nm, 1, row);

            Label ds = Ui.L(item.Desc, Ui.F85, Ui.Muted);
            ds.Dock = DockStyle.Fill;
            ds.TextAlign = ContentAlignment.MiddleLeft;
            ds.AutoEllipsis = true;
            _mcList.Controls.Add(ds, 2, row);

            Label st = Ui.L(item.Required ? "待检测" : "建议项 · 待检测", Ui.F85, Ui.MutedLight);
            st.Dock = DockStyle.Fill;
            st.TextAlign = ContentAlignment.MiddleLeft;
            st.AutoEllipsis = true;
            _mcList.Controls.Add(st, 3, row);

            if (_mcTip != null)
            {
                _mcTip.SetToolTip(nm, item.Name + "：" + item.Desc);
                _mcTip.SetToolTip(ds, item.Desc);
                _mcTip.SetToolTip(st, item.Required ? "必需项" : "建议项");
            }

            MasterCheckRow r = new MasterCheckRow();
            r.Name = item.Name;
            r.Category = item.Category;
            r.Icon = icon;
            r.Status = st;
            _mcRows.Add(r);
        }

        private void ResetCheckRows()
        {
            foreach (MasterCheckRow r in _mcRows)
            {
                r.Icon.Text = "…";
                r.Icon.ForeColor = Ui.MutedLight;
                r.Status.Text = "检测中…";
                r.Status.ForeColor = Ui.Muted;
            }
        }

        private void UpdateCheckRow(AuditResult result)
        {
            foreach (MasterCheckRow r in _mcRows)
            {
                if (!string.Equals(r.Name, result.Name, StringComparison.Ordinal)) continue;
                if (result.Ok)
                {
                    r.Icon.Text = "✓";
                    r.Icon.ForeColor = Ui.Green;
                    r.Status.Text = result.Required ? "已放行" : "已就绪（建议项）";
                    r.Status.ForeColor = Ui.Green;
                }
                else if (result.Required)
                {
                    r.Icon.Text = "×";
                    r.Icon.ForeColor = Ui.Red;
                    r.Status.Text = Ui.Short(result.Message, 44);
                    r.Status.ForeColor = Ui.Red;
                }
                else
                {
                    r.Icon.Text = "!";
                    r.Icon.ForeColor = Ui.Amber;
                    r.Status.Text = Ui.Short(result.Message, 44);
                    r.Status.ForeColor = Ui.Amber;
                }
                if (_mcTip != null) _mcTip.SetToolTip(r.Status, result.Message);
                return;
            }
        }

        // ---------------------------------------------------------------- 运行

        /// <summary>把上一次的检测结果显示出来（切换页面回来时不用重复跑一遍）。</summary>
        private void ShowCachedMasterCheck()
        {
            List<AuditResult> results = MasterAudit.Last;
            if (results.Count == 0)
            {
                RunMasterCheck();
                return;
            }
            foreach (AuditResult r in results) UpdateCheckRow(r);
            ApplyVerdict(MasterAudit.Verdict(results), results);
        }

        private void RunMasterCheck()
        {
            if (_mcSummary == null || _mcSummary.IsDisposed) return;
            if (_mcRunning) return;            _mcRunning = true;
            int token = ++_mcToken;

            ResetCheckRows();
            _mcSummary.Text = "正在检测…";
            _mcSummary.ForeColor = Ui.Accent;
            _mcHint.Text = "逐项检查服务、共享、防火墙与 UWF 状态，通常 2–5 秒。";
            _mcDetail.Text = "";
            _mcRunBtn.Enabled = false;
            _mcFixBtn.Enabled = false;

            SynchronizationContext ui = SynchronizationContext.Current;
            Task.Factory.StartNew(delegate
            {
                List<AuditResult> results;
                try
                {
                    results = MasterAudit.RunAll(delegate(AuditResult r)
                    {
                        if (ui == null) return;
                        ui.Post(delegate
                        {
                            if (IsDisposed || token != _mcToken) return;
                            UpdateCheckRow(r);
                        }, null);
                    });
                }
                catch (Exception ex)
                {
                    results = null;
                    Log.Error("母盘放行检查异常：" + ex.Message);
                }
                if (ui == null) return;
                ui.Post(delegate
                {
                    if (IsDisposed || token != _mcToken) return;
                    _mcRunning = false;
                    _mcRunBtn.Enabled = true;
                    _mcFixBtn.Enabled = true;
                    if (results == null)
                    {
                        _mcSummary.Text = "检测失败";
                        _mcSummary.ForeColor = Ui.Red;
                        _mcHint.Text = "检测过程中出现异常，详情见「操作日志」。";
                        return;
                    }
                    ApplyVerdict(MasterAudit.Verdict(results), results);
                }, null);
            });
        }

        private void ApplyVerdict(AuditVerdict v, List<AuditResult> results)
        {
            _mcSummary.Text = v.Headline;
            _mcSummary.ForeColor = v.MasterReady ? Ui.Green : (v.RemoteReady ? Ui.Amber : Ui.Red);
            _mcHint.Text = v.MasterReady
                ? "本机的共享 / WMI / 防火墙 / 账号权限都已到位，UWF 也已开启保护，可以直接做母盘。"
                : (v.RemoteReady
                    ? "远程管理已放行；完成「母盘保护」下的项目后，克隆出去的机器才会还原。"
                    : "先处理红色 × 的项目；修完点「重新检测」，全部通过再对拷。");
            _mcDetail.Text = v.Detail;
            _mcDetail.ForeColor = v.RemoteReady && v.MasterReady ? Ui.Muted : Ui.TextColor;
        }

        private void FixMasterRelease()
        {
            if (!Uwf.IsAdmin())
            {
                MessageBox.Show(this, "一键放行需要管理员权限。\r\n请右键本工具 →「以管理员身份运行」，再来点这个按钮。", "放行修复");
                return;
            }
            if (_mcRunning) return;
            _mcRunning = true;
            int token = ++_mcToken;
            _mcFixBtn.Enabled = false;
            _mcRunBtn.Enabled = false;
            _mcSummary.Text = "正在放行…";
            _mcSummary.ForeColor = Ui.Accent;
            _mcHint.Text = "正在开启服务、放行防火墙端口并补齐共享 / 令牌策略。";
            _mcDetail.Text = "";

            SynchronizationContext ui = SynchronizationContext.Current;
            Task.Factory.StartNew(delegate
            {
                string report;
                try { report = MasterAudit.FixAll(); }
                catch (Exception ex) { report = "放行失败：" + ex.Message; }
                if (ui == null) return;
                ui.Post(delegate
                {
                    if (IsDisposed) return;
                    if (token != _mcToken) return;   // 已经重新检测过，旧的修复结果不要再弹，免得盖在新结论上
                    _mcRunning = false;
                    _mcFixBtn.Enabled = true;
                    _mcRunBtn.Enabled = true;
                    Log.Info("母盘放行修复：" + Ui.Short(report, 240));
                    MessageBox.Show(this, report, "一键放行修复", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RunMasterCheck();
                }, null);
            });
        }
    }
}
