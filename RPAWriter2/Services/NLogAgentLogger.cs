#nullable disable
using System;
using Agent.Core.Contracts;

namespace RPAWriter2.Services
{
    /// <summary>
    /// <see cref="IAgentLogger"/> that writes through NLog's chat-log target, so model
    /// warnings (e.g. a prompt-cache loss) land in the chat-log file.
    /// </summary>
    public sealed class NLogAgentLogger : IAgentLogger
    {
        public void Info(string message) => LogService.ChatInfo(message);
        public void Warn(string message) => LogService.ChatWarn(message);
        public void Error(string message) => LogService.ChatError(message);
        public void Error(Exception exception, string message = null) => LogService.ChatError(exception, message);
    }
}
