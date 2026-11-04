using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UwfTeacher
{
    public static class Log
    {
        private static readonly object Sync = new object();
        private static readonly List<string> Buffer = new List<string>();
        private static string _dir;
        public static event Action<string> Added;

        /// <summary>日志保留天数，0 = 永久保留。启动时按此清理旧文件。</summary>
        public static int RetentionDays = 30;

        /// <summary>日志目录：优先放在程序所在目录的 logs 下，不可写时退回 ProgramData。</summary>
        public static string Dir
        {
            get
            {
                if (_dir != null) return _dir;
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string preferred = Path.Combine(exeDir, "logs");
                if (CanWrite(preferred))
                {
                    _dir = preferred;
                    return _dir;
                }
                string fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "UWF教师控制台", "logs");
                try { Directory.CreateDirectory(fallback); } catch { }
                _dir = fallback;
                return _dir;
            }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-test");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsPortable
        {
            get
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                return string.Equals(Dir, Path.Combine(exeDir, "logs"), StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(Dir, "uwf-" + DateTime.Now.ToString("yyyyMMdd") + ".log"); }
        }

        public static List<string> All
        {
            get { lock (Sync) { return new List<string>(Buffer); } }
        }

        /// <summary>最近一次写文件是否失败（磁盘只读、被杀软占用、两个实例抢文件等）。</summary>
        public static bool FileWriteFailed { get; private set; }

        public static void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  [" + level + "]  " + message;
            lock (Sync)
            {
                Buffer.Add(line);
                if (Buffer.Count > 2000) Buffer.RemoveRange(0, 500);
                try
                {
                    // FileShare.ReadWrite：教师开两个实例、或杀毒软件正在读日志时也能写进去，
                    // 否则 AppendAllText（默认独占）会直接抛 IOException，日志静默缺行。
                    using (FileStream fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (StreamWriter sw = new StreamWriter(fs, new UTF8Encoding(false)))
                    {
                        sw.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + level + "\t" + message + "\r\n");
                    }
                    FileWriteFailed = false;
                }
                catch
                {
                    FileWriteFailed = true;   // 不静默：日志页会显示“文件日志不可用”
                }
            }
            Action<string> h = Added;
            if (h != null)
            {
                try { h(line); } catch { }
            }
        }

        public static void Info(string message) { Write("信息", message); }
        public static void Warn(string message) { Write("警告", message); }
        public static void Error(string message) { Write("错误", message); }

        /// <summary>按保留天数清理旧日志，返回删除的文件数。</summary>
        public static int Cleanup()
        {
            int removed = 0;
            if (RetentionDays <= 0) return 0;
            try
            {
                DateTime limit = DateTime.Now.Date.AddDays(-RetentionDays);
                foreach (string file in Directory.GetFiles(Dir, "uwf-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < limit)
                        {
                            File.Delete(file);
                            removed++;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            if (removed > 0) Write("信息", "已清理 " + removed + " 个超过 " + RetentionDays + " 天的旧日志。");
            return removed;
        }

        /// <summary>列出日志目录里的文件，按时间倒序。</summary>
        public static string[] Files()
        {
            try
            {
                string[] files = Directory.GetFiles(Dir, "uwf-*.log");
                Array.Sort(files, delegate(string a, string b)
                {
                    return File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a));
                });
                return files;
            }
            catch
            {
                return new string[0];
            }
        }
    }
}
