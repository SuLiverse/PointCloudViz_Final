using System;
using System.IO;

namespace PointCloudViz_Final.Utils
{
    /// <summary>日志系统，用于记录程序运行状态和错误</summary>
    public static class Logger
    {
        private static readonly string LogPath = "PointCloudApp.log";
        private static readonly object LockObject = new object();

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warning(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message, Exception? exception = null)
        {
            var errorMsg = exception != null ? $"{message}: {exception.Message}\n{exception.StackTrace}" : message;
            Write("ERROR", errorMsg);
        }

        public static void Debug(string message)
        {
#if DEBUG
            Write("DEBUG", message);
#endif
        }

        private static void Write(string level, string message)
        {
            lock (LockObject)
            {
                try
                {
                    var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                    
                    // 写入文件
                    File.AppendAllText(LogPath, logEntry + "\n");
                    
                    // 同时输出到 Visual Studio 调试窗口
                    System.Diagnostics.Debug.WriteLine(logEntry);
                }
                catch
                {
                    // 如果日志写入失败，静默处理（避免影响主程序）
                }
            }
        }

        /// <summary>清除日志文件</summary>
        public static void Clear()
        {
            lock (LockObject)
            {
                try
                {
                    if (File.Exists(LogPath))
                        File.Delete(LogPath);
                }
                catch { }
            }
        }
    }
}

