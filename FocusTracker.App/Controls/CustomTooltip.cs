using System;
using System.Collections.Generic;
using System.Linq;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView.SKCharts;

namespace FocusTracker.App.Controls
{
    public class CustomTooltip : SKDefaultTooltip
    {
        public override void Show(IEnumerable<ChartPoint> foundPoints, Chart chart)
        {
            // ❗ Не показывать тултип, если все значения ≈ 0
            if (!foundPoints.Any(p => Math.Abs(p.Coordinate.PrimaryValue) > 0.01))
            {
                Hide(chart);
                return;
            }

            base.Show(foundPoints, chart);
        }
    }
}
