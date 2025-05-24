using System.Windows.Controls;
using FocusTracker.App.ViewModels;
using LiveChartsCore.SkiaSharpView.WPF;
using FocusTracker.App.Controls;
using LiveChartsCore;

namespace FocusTracker.App.Views
{
    public partial class StatsPage : UserControl
    {
        public StatsPage()
        {
            InitializeComponent();
            DataContext = new PeriodStatsViewModel();
            //chart.Tooltip = new CustomTooltip(); // это теперь работает
        }

        public CartesianChart Chart => chart;
    }


}
