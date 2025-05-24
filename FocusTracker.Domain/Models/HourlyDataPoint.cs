using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Models
{
    public class HourlyDataPoint
    {
        public string Hour { get; set; }     // "08:00"
        public double Minutes { get; set; }  // например, 15
    }

}
