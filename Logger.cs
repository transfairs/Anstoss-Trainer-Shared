using System;
using System.IO;

namespace AnstossTrainer.Shared
{
    /// <summary>Severity of a log entry.</summary>
    internal enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }

    /// <summary>
    /// File-based logger, writing to %LocalAppData%\&lt;trainer name&gt;\trainer.log with a single
    /// backup file for rotation. Each trainer supplies its folder name and debug switch through the
    /// partial methods below (e.g. Controller/Logger.A2.cs).
    /// </summary>
    internal static partial class Logger
    {
        private const long MaxLogFileBytes = 2 * 1024 * 1024;

        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName());
        private static readonly string LogFilePath = Path.Combine(LogDirectory, "trainer.log");

        /// <summary>Lets the trainer name its log folder under %LocalAppData%.</summary>
        static partial void GetAppFolderName(ref string name);

        /// <summary>Lets the trainer switch Debug/Info logging on (off unless it says otherwise).</summary>
        static partial void GetDebugEnabled(ref bool enabled);

        private static string AppFolderName()
        {
            string name = "AnstossTrainer";
            GetAppFolderName(ref name);
            return name;
        }

        private static bool IsDebugEnabled()
        {
            bool enabled = false;
            GetDebugEnabled(ref enabled);
            return enabled;
        }

        // Debug/Info are gated behind the trainer's debug switch so the log stays empty by default
        // and never bloats for a user who hasn't opted into debugging; Warn/Error always write,
        // since they flag an actual problem and are what a bug report would need.
        /// <summary>Writes a debug entry, if the trainer has debug logging on.</summary>
        internal static void Debug(string message)
        {
            if (IsDebugEnabled())
                Write(LogLevel.Debug, message, null);
        }

        /// <summary>Writes an info entry, if the trainer has debug logging on.</summary>
        internal static void Info(string message)
        {
            if (IsDebugEnabled())
                Write(LogLevel.Info, message, null);
        }

        /// <summary>Writes a warning.</summary>
        internal static void Warn(string message) => Write(LogLevel.Warn, message, null);
        /// <summary>Writes a warning with the exception's details.</summary>
        internal static void Warn(string message, Exception ex) => Write(LogLevel.Warn, message, ex);

        /// <summary>Writes an error.</summary>
        internal static void Error(string message) => Write(LogLevel.Error, message, null);
        /// <summary>Writes an error with the exception's details.</summary>
        internal static void Error(string message, Exception ex) => Write(LogLevel.Error, message, ex);

        private static void Write(LogLevel level, string message, Exception ex)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                // ex.ToString() (not the "+ ex" concat form): the compiler emits its own redundant
                // null-check for a concatenated object operand (concat treats null as ""), which is
                // unreachable dead code here since ex is already known non-null - an explicit
                // ToString() call needs no such guard and avoids that untestable branch.
                if (ex != null)
                    line += Environment.NewLine + ex.ToString();

                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogDirectory);
                    RotateIfTooLarge();
                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never be the reason the trainer crashes (e.g. an unwritable/locked
                // log directory) - drop the entry rather than throw.
            }
        }

        // Keeps disk usage bounded (current file + one backup) without needing full rolling
        // multi-file rotation for what is a low-volume, single-user log.
        private static void RotateIfTooLarge()
        {
            FileInfo info = new FileInfo(LogFilePath);
            if (!info.Exists || info.Length < MaxLogFileBytes)
                return;

            string backupPath = LogFilePath + ".old";
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            File.Move(LogFilePath, backupPath);
        }
    }
}
