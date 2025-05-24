using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Models
{
    public class HourlyStat
    {
        public string Hour { get; set; }
        public double Productive { get; set; }
        public double Neutral { get; set; }
        public double Unproductive { get; set; }
        public int HourInt => int.Parse(Hour[..2]);
    }

}
