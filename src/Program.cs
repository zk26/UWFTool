using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UwfTeacher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && args[0].StartsWith("--"))
            {
                return Cli.Run(args);
            }

            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Ui.Fatal(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null) Ui.Fatal(ex);
            };
            TaskScheduler.UnobservedTaskException += delegate(object s, UnobservedTaskExceptionEventArgs e)
            {
                // 后台任务里没被 await 的异常：记下来并标记已观察，别让它把进程带走
                try
                {
                    if (e.Exception != null) Log.Error("后台任务异常：" + e.Exception.GetBaseException().Message);
                    e.SetObserved();
                }
                catch { }
            };

            try
            {
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                Ui.Fatal(ex);
                return 1;
            }
        }
    }

    internal static class Cli
    {
        public static int Run(string[] args)
        {
            string mode = args[0].ToLowerInvariant();
            string outFile = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "uwf_cli.txt");
            StringBuilder sb = new StringBuilder();
            int rc = 0;
            try
            {
                if (mode == "--shot")
                {
                    string demo = args.Length > 2 ? args[2].ToLowerInvariant() : "";
                    rc = Shots.Run(outFile, demo, sb);
                }
                else if (mode == "--status" || mode == "--selftest")
                {
                    rc = DumpStatus(sb);
                }
                else if (mode == "--uwf")
                {
                    string cmd = args.Length > 2 ? args[2] : "get-config";
                    UwfResult r = Uwf.Run(cmd);
                    sb.AppendLine("COMMAND: " + r.Command);
                    sb.AppendLine("EXIT   : " + r.ExitCode);
                    sb.AppendLine("-------- output --------");
                    sb.AppendLine(r.All);
                    rc = r.Ok ? 0 : 2;
                }

                else if (mode == "--master")
                {
                    System.Collections.Generic.List<AuditResult> results = MasterAudit.RunAll();
                    foreach (AuditResult r in results)
                    {
                        sb.AppendLine((r.Ok ? "[OK]  " : (r.Required ? "[FAIL]" : "[WARN]")) + " " +
                                      r.Category + " / " + r.Name + "：" + (r.Ok ? "已放行" : r.Message));
                    }
                    AuditVerdict v = MasterAudit.Verdict(results);
                    sb.AppendLine();
                    sb.AppendLine(v.Headline);
                    sb.AppendLine(v.Detail);
                    rc = v.MasterReady ? 0 : 2;
                }
                else if (mode == "--audit")
                {
                    using (MainForm f = new MainForm())
                    {
                        f.SuppressStartupRefresh = true;
                        f.ShowInTaskbar = false;
                        f.StartPosition = FormStartPosition.Manual;
                        f.Location = new System.Drawing.Point(-2400, 40);
                        f.Show();
#if DEMO
                        f.LoadDemoState("demo");
#endif
                        Application.DoEvents();
                        int[][] sizes = new int[][] { new int[] { 1120, 740 }, new int[] { 1440, 900 } };
                        foreach (int[] size in sizes)
                        {
                            f.Size = new System.Drawing.Size(size[0], size[1]);
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(80);
                            Application.DoEvents();
                            for (int i = 0; i < f.PageCount; i++)
                            {
                                f.SelectPage(i);
                                Application.DoEvents();
                                System.Collections.Generic.List<string> issues = Ui.AuditText(f);
                                sb.AppendLine("===== " + size[0] + "px page " + i + " (" + issues.Count + ") =====");
                                foreach (string s in issues) sb.AppendLine(s);
                            }
                        }
                        f.Hide();
                    }
#if DEMO
                    using (DiscoveryForm d = new DiscoveryForm(delegate(string host)
                    {
                        RemoteTarget t = new RemoteTarget();
                        t.Host = host;
                        return t;
                    }))
                    {
                        d.StartPosition = FormStartPosition.Manual;
                        d.Location = new System.Drawing.Point(-2400, 40);
                        d.ShowInTaskbar = false;
                        d.Show();
                        Application.DoEvents();
                        int[][] dSizes = new int[][] { new int[] { 800, 560 }, new int[] { 900, 640 }, new int[] { 1200, 760 } };
                        foreach (int[] size in dSizes)
                        {
                            d.Size = new System.Drawing.Size(size[0], size[1]);
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(60);
                            Application.DoEvents();
                            System.Collections.Generic.List<string> issues = Ui.AuditText(d);
                            sb.AppendLine("===== 搜索学生机 " + size[0] + "px (" + issues.Count + ") =====");
                            foreach (string s in issues) sb.AppendLine(s);
                        }
                        d.Hide();
                    }
#endif
                }
                else if (mode == "--dumpui")
                {
                    using (MainForm f = new MainForm())
                    {
                        f.CreateControl();
                        f.ReloadState(false);
                        for (int i = 0; i < f.PageCount; i++)
                        {
                            f.SelectPage(i);
                            sb.AppendLine("===== page " + i + " =====");
                            Dump(f, sb, 0);
                        }
                    }
                }
                else
                {
                    sb.AppendLine("unknown mode: " + mode);
                    rc = 64;
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("EXCEPTION: " + ex);
                rc = 99;
            }

            try
            {
                // --shot 的第一个参数是目录：日志写进目录里，别指望能当文件名用
                string target = Directory.Exists(outFile) ? Path.Combine(outFile, "shots.log") : outFile;
                File.WriteAllText(target, sb.ToString(), new UTF8Encoding(true));
            }
            catch
            {
            }
            return rc;
        }

        private static void Dump(Control c, StringBuilder sb, int depth)
        {
            string pad = new string(' ', depth * 2);
            string text = "";
            try { text = c.Text; } catch { }
            sb.AppendLine(pad + c.GetType().Name + " [" + Ui.Short(text, 28) + "] " +
                          "x=" + c.Left + " y=" + c.Top + " w=" + c.Width + " h=" + c.Height +
                          " vis=" + c.Visible + " en=" + c.Enabled);
            foreach (Control child in c.Controls) Dump(child, sb, depth + 1);
        }

        private static int DumpStatus(StringBuilder sb)
        {
            sb.AppendLine("== machine ==");
            sb.AppendLine("Computer : " + Environment.MachineName);
            sb.AppendLine("User     : " + Environment.UserDomainName + "\\" + Environment.UserName);
            sb.AppendLine("OS       : " + Environment.OSVersion.VersionString + " (" + Environment.OSVersion.Version.Build + ")");
            sb.AppendLine("64bit    : " + Environment.Is64BitOperatingSystem + " / process " + Environment.Is64BitProcess);
            sb.AppendLine("Admin    : " + Uwf.IsAdmin());
            sb.AppendLine("uwfmgr   : " + Uwf.ExePath + " exists=" + File.Exists(Uwf.ExePath));
            sb.AppendLine();

            UwfState st = Uwf.Read();
            sb.AppendLine("== uwf state ==");
            sb.AppendLine("Installed      : " + st.Installed);
            sb.AppendLine("WMI present    : " + st.WmiPresent);
            sb.AppendLine("uwfmgr present : " + st.UwfmgrPresent);
            sb.AppendLine("Filter cur/next: " + st.FilterCurrent + " / " + st.FilterNext);
            sb.AppendLine("Servicing      : " + st.ServicingCurrent + " / " + st.ServicingNext);
            sb.AppendLine("ProtectedNow   : " + st.ProtectedNow);
            sb.AppendLine("PendingReboot  : " + st.PendingReboot);
            sb.AppendLine("Overlay MB     : used=" + st.OverlayUsedMb + " free=" + st.OverlayFreeMb + " max=" + st.OverlayMaxMb + " warn=" + st.OverlayWarnMb + " crit=" + st.OverlayCritMb + " type=" + st.OverlayType);
            sb.AppendLine("Error          : " + st.Error);
            sb.AppendLine("Volumes:");
            foreach (VolumeState v in st.Volumes)
            {
                sb.AppendLine("  " + v.Letter + "  label=" + v.Label + "  cur=" + v.ProtectedCurrent + " next=" + v.ProtectedNext + "  sizeGB=" + Math.Round(v.CapacityGb, 1));
            }
            sb.AppendLine("File exclusions (" + st.FileExclusions.Count + "):");
            foreach (string p in st.FileExclusions) sb.AppendLine("  " + p);
            sb.AppendLine("Registry exclusions (" + st.RegExclusions.Count + "):");
            foreach (string p in st.RegExclusions) sb.AppendLine("  " + p);
            sb.AppendLine();
            sb.AppendLine("== raw get-config ==");
            sb.AppendLine(st.RawConfig);
            sb.AppendLine();
            sb.AppendLine("== raw overlay get-config ==");
            sb.AppendLine(st.RawOverlay);
            return 0;
        }
    }

    internal static class Shots
    {
        public static int Run(string dir, string demo, StringBuilder log)
        {
            Directory.CreateDirectory(dir);
            using (MainForm f = new MainForm())
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new System.Drawing.Point(-2400, 40);
                f.Size = new System.Drawing.Size(1280, 820);
                f.ShowInTaskbar = false;
                f.SuppressStartupRefresh = true;
                f.Show();
                Application.DoEvents();
#if DEMO
                if (demo.Length > 0) f.LoadDemoState(demo); else f.ReloadState(true);
#else
                f.ReloadState(true);
#endif
                Application.DoEvents();

                for (int i = 0; i < f.PageCount; i++)
                {
                    f.SelectPage(i);
                    Application.DoEvents();
                    Thread.Sleep(120);
                    Application.DoEvents();
                    using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                        string path = Path.Combine(dir, "page" + i + ".png");
                        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                        log.AppendLine("saved " + path);
                    }
                }
                f.Hide();
#if DEMO
                if (demo.Length > 0) { f.DemoCaptureInstallWizard(dir, log); f.DemoCaptureDialogs(dir, log); }
#endif
            }
            return 0;
        }
    }
}
