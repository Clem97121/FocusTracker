using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Models
{
    public class HourlyAppUsageLog
    {
        public int Id { get; set; }
        public string AppName { get; set; }
        public DateTime Date { get; set; }  // Тільки дата
        public int Hour { get; set; }       // Від 0 до 23
        public TimeSpan TotalTime { get; set; }
        public TimeSpan ActiveTime { get; set; }
    }

}
