using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;


namespace FocusTracker.App.ViewModels
{
    public class PeriodStatsViewModel : INotifyPropertyChanged
    {
        private bool _isRangeMode;
        public bool IsRangeMode
        {
            get => _isRangeMode;
            set
            {
                if (_isRangeMode != value)
                {
                    _isRangeMode = value;
                    OnPropertyChanged();
                    if (_isRangeMode)
                        LoadDataForRange();
                    else
                        LoadDataForDate(SelectedDate);
                }
            }
        }

        public ObservableCollection<ProgramDailyStat> DailyPrograms { get; set; } = new();

        private DateTime _selectedDate = DateTime.Today;
        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                if (_selectedDate != value)
                {
                    _selectedDate = value;
                    OnPropertyChanged();
                    LoadDataForDate(_selectedDate);
                }
            }
        }

        private DateTime _startDate = DateTime.Today;
        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (_startDate != value)
                {
                    _startDate = value;
                    OnPropertyChanged();
                    LoadDataForRange();
                }
            }
        }

        private DateTime _endDate = DateTime.Today;
        public DateTime EndDate
        {
            get => _endDate;
            set
            {
                if (_endDate != value)
                {
                    _endDate = value;
                    OnPropertyChanged();
                    LoadDataForRange();
                }
            }
        }

        public ISeries[] Series { get; set; }
        public Axis[] XAxes { get; set; }
        public Axis[] YAxes { get; set; }

        public PeriodStatsViewModel()
        {
            LoadMinDate(); // 👈 сначала определим минимум
            LoadDataForDate(_selectedDate);
        }
        private DateTime _minDate = DateTime.Today;
        public DateTime MinDate
        {
            get => _minDate;
            set
            {
                _minDate = value;
                OnPropertyChanged();
            }
        }


        public DateTime MinAvailableDate { get; private set; } = DateTime.Today;

        private async void LoadMinDate()
        {
            var usageService = App.Services.GetRequiredService<IAppUsageStatService>();
            MinDate = await usageService.GetMinTrackedDateAsync();
        }


        private async void LoadDataForDate(DateTime date)
        {
            DailyPrograms.Clear();

            var usageService = App.Services.GetRequiredService<IAppUsageStatService>();
            var programService = App.Services.GetRequiredService<IProgramService>();

            var hourlyStats = (await usageService.GetHourlyStatsAsync(date)).ToList();
            var statsForDay = (await usageService.GetStatsForDayAsync(date)).ToList();
            var programs = (await programService.GetAllAsync())
                .Where(p => p.IsTracked && !p.IsHidden)
                .ToList();

            var labels = new List<string>();
            var prodValues = new List<double>();
            var neuValues = new List<double>();
            var unprodValues = new List<double>();

            // Находим диапазон активных часов
            int? firstActiveHour = null;
            int? lastActiveHour = null;

            for (int hour = 0; hour < 24; hour++)
            {
                var hourGroup = hourlyStats.Where(u => u.Hour == hour);
                if (hourGroup.Any(u => u.TotalTime.TotalMinutes > 0))
                {
                    if (firstActiveHour == null) firstActiveHour = hour;
                    lastActiveHour = hour;
                }
            }

            if (firstActiveHour == null || lastActiveHour == null)
            {
                firstActiveHour = 0;
                lastActiveHour = 23;
            }

            int startHour = Math.Max(0, firstActiveHour.Value - 4);
            int endHour = Math.Min(23, lastActiveHour.Value + 4);

            for (int hour = startHour; hour <= endHour; hour++)
            {
                var hourGroup = hourlyStats.Where(u => u.Hour == hour);
                double prod = 0, neu = 0, unprod = 0;

                foreach (var record in hourGroup)
                {
                    var prog = programs.FirstOrDefault(p => p.Identifier == record.AppName);
                    if (prog == null) continue;

                    var minutes = record.TotalTime.TotalMinutes;
                    switch (prog.Category)
                    {
                        case "Продуктивні": prod += minutes; break;
                        case "Суміжні": neu += minutes; break;
                        case "Непродуктивні": unprod += minutes; break;
                    }
                }

                labels.Add(hour.ToString("00") + ":00");
                prodValues.Add(prod);
                neuValues.Add(neu);
                unprodValues.Add(unprod);
            }

            Series = new ISeries[]
            {
                new StackedColumnSeries<double>
                {
                    Values = unprodValues,
                    Name = "Непродуктивні",
                    Fill = new SolidColorPaint(SKColors.IndianRed),
                    DataLabelsFormatter = point => double.IsNaN(point.Model) ? string.Empty : $"{Math.Round(point.Model)} хв"

                },
                new StackedColumnSeries<double>
                {
                    Values = neuValues,
                    Name = "Суміжні",
                    Fill = new SolidColorPaint(SKColors.Gold),
                    DataLabelsFormatter = point => double.IsNaN(point.Model) ? string.Empty : $"{Math.Round(point.Model)} хв"

                },
                new StackedColumnSeries<double>
                {
                    Values = prodValues,
                    Name = "Продуктивні",
                    Fill = new SolidColorPaint(SKColors.LimeGreen),
                    DataLabelsFormatter = point => double.IsNaN(point.Model) ? string.Empty : $"{Math.Round(point.Model)} хв"

                }
            };

            XAxes = new Axis[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsRotation = 0,
                    Name = "Година",
                    TextSize = 12,
                    LabelsPaint = new SolidColorPaint(SKColors.White),
                    NamePaint = new SolidColorPaint(SKColors.White)
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Хвилини",
                    TextSize = 12,
                    LabelsPaint = new SolidColorPaint(SKColors.White),
                    NamePaint = new SolidColorPaint(SKColors.White),
                    Labeler = value => $"{Math.Round(value, 1)} хв"
                }
            };

            OnPropertyChanged(nameof(Series));
            OnPropertyChanged(nameof(XAxes));
            OnPropertyChanged(nameof(YAxes));

            var totalMinutes = statsForDay.Sum(s => s.TotalTime.TotalMinutes);
            foreach (var stat in statsForDay)
            {
                var prog = programs.FirstOrDefault(p => p.Identifier == stat.AppName);
                if (prog == null) continue;

                var minutes = stat.TotalTime.TotalMinutes;
                var activeMinutes = stat.ActiveTime.TotalMinutes;
                var percent = totalMinutes > 0 ? (minutes / totalMinutes) * 100 : 0;

                DailyPrograms.Add(new ProgramDailyStat
                {
                    Name = prog.Name,
                    DisplayName = prog.DisplayName ?? prog.Name,
                    IconBytes = prog.IconBytes,
                    Minutes = minutes,
                    ActiveMinutes = activeMinutes,
                    Percentage = percent,
                    Category = prog.Category
                });
            }

        }

        private async void LoadDataForRange()
        {
            DailyPrograms.Clear();

            var usageService = App.Services.GetRequiredService<IAppUsageStatService>();
            var programService = App.Services.GetRequiredService<IProgramService>();

            var programs = (await programService.GetAllAsync())
                .Where(p => p.IsTracked && !p.IsHidden)
                .ToList();

            var allHourlyStats = new List<HourlyAppUsageLog>();
            var allStats = new List<AppUsageStat>();

            foreach (var date in EachDay(StartDate, EndDate))
            {
                allHourlyStats.AddRange(await usageService.GetHourlyStatsAsync(date));
                allStats.AddRange(await usageService.GetStatsForDayAsync(date));
            }

            // 1. Находим диапазон активных часов
            int? firstActiveHour = null;
            int? lastActiveHour = null;

            for (int hour = 0; hour < 24; hour++)
            {
                if (allHourlyStats.Any(u => u.Hour == hour && u.TotalTime.TotalMinutes > 0))
                {
                    if (firstActiveHour == null) firstActiveHour = hour;
                    lastActiveHour = hour;
                }
            }

            if (firstActiveHour == null || lastActiveHour == null)
            {
                firstActiveHour = 0;
                lastActiveHour = 23;
            }

            int startHour = Math.Max(0, firstActiveHour.Value - 4);
            int endHour = Math.Min(23, lastActiveHour.Value + 4);


            var labels = new List<string>();
            var prodValues = new List<double>();
            var neuValues = new List<double>();
            var unprodValues = new List<double>();

            for (int hour = startHour; hour <= endHour; hour++)
            {
                var hourGroup = allHourlyStats.Where(h => h.Hour == hour);
                double prod = 0, neu = 0, unprod = 0;

                foreach (var record in hourGroup)
                {
                    var prog = programs.FirstOrDefault(p => p.Identifier == record.AppName);
                    if (prog == null) continue;

                    var minutes = record.TotalTime.TotalMinutes;

                    switch (prog.Category)
                    {
                        case "Продуктивні": prod += minutes; break;
                        case "Суміжні": neu += minutes; break;
                        case "Непродуктивні": unprod += minutes; break;
                    }
                }

                labels.Add(hour.ToString("00") + ":00");
                prodValues.Add(prod);
                neuValues.Add(neu);
                unprodValues.Add(unprod);
            }


            Series = new ISeries[]
            {
                new StackedColumnSeries<double>
                {
                    Values = unprodValues,
                    Name = "Непродуктивні",
                    Fill = new SolidColorPaint(SKColors.IndianRed)
                },
                new StackedColumnSeries<double>
                {
                    Values = neuValues,
                    Name = "Суміжні",
                    Fill = new SolidColorPaint(SKColors.Gold)
                },
                new StackedColumnSeries<double>
                {
                    Values = prodValues,
                    Name = "Продуктивні",
                    Fill = new SolidColorPaint(SKColors.LimeGreen)
                }
            };

            XAxes = new Axis[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsRotation = 0,
                    Name = "Година",
                    TextSize = 12,
                    LabelsPaint = new SolidColorPaint(SKColors.White),
                    NamePaint = new SolidColorPaint(SKColors.White)
                }
            };

            OnPropertyChanged(nameof(Series));
            OnPropertyChanged(nameof(XAxes));

            var totalMinutes = allStats.Sum(s => s.TotalTime.TotalMinutes);

            foreach (var group in allStats.GroupBy(s => s.AppName))
            {
                var prog = programs.FirstOrDefault(p => p.Identifier == group.Key);
                if (prog == null) continue;

                var minutes = group.Sum(s => s.TotalTime.TotalMinutes);
                var activeMinutes = group.Sum(s => s.ActiveTime.TotalMinutes);
                var percent = totalMinutes > 0 ? (minutes / totalMinutes) * 100 : 0;

                DailyPrograms.Add(new ProgramDailyStat
                {
                    Name = prog.Name,
                    DisplayName = prog.DisplayName ?? prog.Name,
                    IconBytes = prog.IconBytes,
                    Minutes = minutes,
                    ActiveMinutes = activeMinutes,
                    Percentage = percent,
                    Category = prog.Category
                });
            }
        }

        private IEnumerable<DateTime> EachDay(DateTime from, DateTime to)
        {
            for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
                yield return day;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }


    public class ProgramDailyStat
    {
        public Brush PassiveBrush => Category switch
        {
            "Продуктивні" => (Brush)Application.Current.Resources["SuccessBrush"],
            "Суміжні" => (Brush)Application.Current.Resources["NeutralBrush"],
            "Непродуктивні" => (Brush)Application.Current.Resources["DangerBrush"],
            _ => Brushes.Gray
        };

        public Brush ActiveBrush
        {
            get
            {
                var cat = Category;
                var brush = cat switch
                {
                    "Продуктивні" => (Brush)Application.Current.Resources["SuccessAccentBrush"],
                    "Суміжні" => (Brush)Application.Current.Resources["NeutralAccentBrush"],
                    "Непродуктивні" => (Brush)Application.Current.Resources["DangerAccentBrush"],
                    _ => Brushes.DarkGray
                };
                Console.WriteLine($"{DisplayName} -> {cat} -> {((SolidColorBrush)brush).Color}");
                return brush;
            }
        }


        public string Name { get; set; }
        public string DisplayName { get; set; }

        public double Minutes { get; set; }
        public double ActiveMinutes { get; set; }
        public double Percentage { get; set; } // Minutes / Total day

        public string Category { get; set; }
        public byte[] IconBytes { get; set; }

        public Brush CategoryColor => Category switch
        {
            "Продуктивні" => (Brush)Application.Current.Resources["SuccessBrush"],
            "Суміжні" => (Brush)Application.Current.Resources["NeutralBrush"],
            "Непродуктивні" => (Brush)Application.Current.Resources["DangerBrush"],
            _ => Brushes.Gray
        };


        public string DisplayLabel
        {
            get
            {
                string Format(double m)
                {
                    var ts = TimeSpan.FromMinutes(m);
                    return ts.TotalHours >= 1
                        ? $"{(int)ts.TotalHours} год {(int)ts.Minutes} хв"
                        : $"{(int)ts.TotalMinutes} хв";
                }

                return $"{DisplayName} — {Format(Minutes)} (активно: {Format(ActiveMinutes)})";
            }
        }

        // 👇 Используется для Grid колонок в XAML
        public double ActiveFillPercent => Percentage > 0 && Minutes > 0
            ? (ActiveMinutes / Minutes) * Percentage
            : 0;

        public double PassiveFillPercent => Math.Max(0, Percentage - ActiveFillPercent);
    }


}
