using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace FocusTracker.App.ViewModels
{
    public class AppStatsViewModel
    {
        public ObservableCollection<AppUsageStat> Stats { get; } = new();

        public AppStatsViewModel()
        {
            _ = RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            var usageService = App.Services.GetRequiredService<IAppUsageStatService>();
            var programService = App.Services.GetRequiredService<IProgramService>();

            // Асинхронно получаем статистику для сегодняшнего дня
            var todayStats = await usageService.GetStatsForTodayAsync();  // Должно быть с await, чтобы получить результаты
            var trackedPrograms = (await programService.GetAllAsync())  // Получаем все программы
                .Where(p => p.IsTracked && !p.IsHidden)
                .Select(p => p.Identifier)
                .ToHashSet();

            // Фильтруем статистику по программам, которые отслеживаются
            var filteredStats = todayStats
                .Where(stat => trackedPrograms.Contains(stat.AppName));

            // Очищаем текущие данные
            Stats.Clear();
            foreach (var stat in filteredStats)
                Stats.Add(stat);
        }
    }
}
