using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Models
{
    public class HourlyCategoryStat
    {
        public int Hour { get; set; }

        public double ProductiveMinutes { get; set; }
        public double NeutralMinutes { get; set; }
        public double UnproductiveMinutes { get; set; }
    }
}
