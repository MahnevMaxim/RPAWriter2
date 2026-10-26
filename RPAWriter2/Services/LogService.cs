#nullable disable
using System;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;
using RPAWriter.Core.Services;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Central application logging via NLog. Writes to a rolling file. The file path
    /// is configurable (see Settings); when empty it defaults to the extension
    /// installation folder (with a %AppData%\RPAWriter2\&lt;version&gt;\Logs fallback).
    /// </summary>
    public static class LogService
    {
        private const string LogFileName = "RPAWriter2.log";
        private const string ChatLogFileName = "RPAWriter2.Chat.log";
        private static readonly Logger Log = LogManager.GetLogger("RPAWriter2");
        private static readonly Logger ChatLog = LogManager.GetLogger("RPAWriter2.Chat");
        private static string _currentLogFile;
        private static string _currentChatLogFile;

        /// <summary>Full path of the log file NLog is currently writing to.</summary>
        public static string CurrentLogFilePath =>
            string.IsNullOrWhiteSpace(_currentLogFile) ? ResolveLogFile(null) : _currentLogFile;

        /// <summary>Full path of the chat log file NLog is currently writing to.</summary>
        public static string CurrentChatLogFilePath =>
            string.IsNullOrWhiteSpace(_currentChatLogFile) ? ResolveChatLogFile(null) : _currentChatLogFile;

        static LogService()
        {
            Configure();
        }

        /// <summary>
        /// (Re)configures the file targets. Pass an explicit full path (or a directory),
        /// or null/empty to use the default location next to the extension.
        /// </summary>
        public static void Configure(string logFilePath = null, string chatLogFilePath = null)
        {
            try
            {
                LogManager.ThrowExceptions = false;
                var file = ResolveLogFile(logFilePath);
                var chatFile = ResolveChatLogFile(chatLogFilePath);
                _currentLogFile = file;
                _currentChatLogFile = chatFile;

                var directory = Path.GetDirectoryName(file);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                var chatDirectory = Path.GetDirectoryName(chatFile);
                if (!string.IsNullOrWhiteSpace(chatDirectory))
                    Directory.CreateDirectory(chatDirectory);

                var configuration = new LoggingConfiguration();
                var fileTarget = new FileTarget("file")
                {
                    FileName = file,
                    Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${onexception:inner=${newline}${exception:format=toString}}",
                    ArchiveEvery = FileArchivePeriod.Day,
                    ArchiveNumbering = ArchiveNumberingMode.Date,
                    ArchiveDateFormat = "yyyyMMdd",
                    MaxArchiveFiles = 14,
                    Encoding = System.Text.Encoding.UTF8,
                    ConcurrentWrites = true,
                    KeepFileOpen = true
                };
                configuration.AddTarget(fileTarget);

                var chatFileTarget = new FileTarget("chatFile")
                {
                    FileName = chatFile,
                    Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${onexception:inner=${newline}${exception:format=toString}}",
                    ArchiveEvery = FileArchivePeriod.Day,
                    ArchiveNumbering = ArchiveNumberingMode.Date,
                    ArchiveDateFormat = "yyyyMMdd",
                    MaxArchiveFiles = 14,
                    Encoding = System.Text.Encoding.UTF8,
                    ConcurrentWrites = true,
                    KeepFileOpen = true
                };
                configuration.AddTarget(chatFileTarget);

                // Chat traffic goes to its own file; nothing else should match this rule.
                var chatRule = new LoggingRule("RPAWriter2.Chat*", LogLevel.Debug, LogLevel.Fatal, chatFileTarget);
                chatRule.Final = true;
                configuration.LoggingRules.Add(chatRule);

                // Everything else keeps the existing behavior (Info and above).
                configuration.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);

                LogManager.Configuration = configuration;
            }
            catch
            {
                // Logging must never break the extension.
            }
        }

        private static string ResolveLogFile(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var trimmed = configuredPath.Trim();
                if (Directory.Exists(trimmed) || trimmed.EndsWith("\\") || trimmed.EndsWith("/"))
                    return Path.Combine(trimmed, LogFileName);
                return trimmed;
            }

            return Path.Combine(ResolveDefaultDirectory(), LogFileName);
        }

        private static string ResolveChatLogFile(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var trimmed = configuredPath.Trim();
                if (Directory.Exists(trimmed) || trimmed.EndsWith("\\") || trimmed.EndsWith("/"))
                    return Path.Combine(trimmed, ChatLogFileName);
                return trimmed;
            }

            return Path.Combine(ResolveDefaultDirectory(), ChatLogFileName);
        }

        private static string ResolveDefaultDirectory()
        {
            try
            {
                var installDir = Path.GetDirectoryName(typeof(ToolWindow1Control).Assembly.Location);
                if (!string.IsNullOrWhiteSpace(installDir))
                    return Path.Combine(installDir, "Logs");
            }
            catch
            {
                // Fall through to the safe default.
            }

            return AppDataPaths.Resolve("Logs");
        }

        public static void Debug(string message) => Log.Debug(message);

        public static void Info(string message) => Log.Info(message);

        public static void Warn(string message) => Log.Warn(message);

        public static void Error(string message) => Log.Error(message);

        public static void Error(Exception exception, string message = null)
        {
            if (exception == null)
            {
                Log.Error(message ?? "");
                return;
            }
            Log.Error(exception, message ?? exception.Message);
        }

        public static void ChatDebug(string message) => ChatLog.Debug(message);

        public static void ChatInfo(string message) => ChatLog.Info(message);

        public static void ChatWarn(string message) => ChatLog.Warn(message);

        public static void ChatError(string message) => ChatLog.Error(message);

        public static void ChatError(Exception exception, string message = null)
        {
            if (exception == null)
            {
                ChatLog.Error(message ?? "");
                return;
            }
            ChatLog.Error(exception, message ?? exception.Message);
        }
    }
}
