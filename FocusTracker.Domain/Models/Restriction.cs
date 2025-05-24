using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Models;

public class Restriction
{
    public int Id { get; set; }
    public string Note { get; set; }

    public ICollection<RestrictionRule> Rules { get; set; }
    public ICollection<RestrictionToProgram> Targets { get; set; }
}
