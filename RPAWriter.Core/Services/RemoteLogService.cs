using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Remote logs: contract-based download (transport wired up later),
    /// analysis delegated to LogParser.
    /// </summary>
    public class RemoteLogService : IRemoteLogService
    {
        private readonly LogParser _analyzer = new LogParser();

        public Task<IReadOnlyList<string>> DownloadAsync(
            RobotDeploymentConfig config, CancellationToken cancellationToken = default)
        {
            // Transport (SSH/HTTP/SMB) is not wired up yet — contract + stub.
            throw new NotSupportedException(
                "Remote log download transport is not wired up yet (contract + stub).");
        }

        public Task<LogAnalysis> AnalyzeAsync(
            IEnumerable<string> lines, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_analyzer.Analyze(lines));
        }
    }
}
