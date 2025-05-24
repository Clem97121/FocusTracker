using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using FocusTracker.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace FocusTracker.Core.Services
{
    public class ProgramSyncService
    {
        private readonly IProgramService _programService;
        private readonly IAppUsageStatService _statService;
        private readonly ProgramNameResolverService _resolver;

        public ProgramSyncService(
            IProgramService programService,
            IAppUsageStatService statService,
            ProgramNameResolverService resolver)
        {
            _programService = programService;
            _statService = statService;
            _resolver = resolver;
        }

        public async Task SyncAsync()
        {
            var tracked = (await _programService.GetAllAsync())
                .Select(p => p.Identifier)
                .ToHashSet();

            var statIds = (await _statService.GetStatsForTodayAsync())
                .Select(s => s.AppName)
                .Distinct();

            foreach (var id in statIds)
            {
                if (!tracked.Contains(id))
                {
                    string? exePath = TryGetProcessPath(id);
                    string displayName = _resolver.Resolve(id, exePath);
                    byte[]? icon = exePath != null ? IconExtractor.GetIconBytes(exePath) : null;

                    var program = new TrackedProgram
                    {
                        Identifier = id,
                        Name = id,
                        DisplayName = displayName,
                        IconBytes = icon,
                        Category = "Суміжні",
                        IsTracked = true,
                        IsHidden = false
                    };

                    _programService.Add(program);
                }
            }
        }

        private string? TryGetProcessPath(string processName)
        {
            try
            {
                var process = Process.GetProcessesByName(processName).FirstOrDefault();
                return process?.MainModule?.FileName;
            }
            catch { return null; }
        }
    }
}
