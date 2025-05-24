using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Models;

public class ProgramUsageLog
{
    public int Id { get; set; }
    public int ProgramId { get; set; }
    public string Date { get; set; }
    public int ActiveDuration { get; set; }
    public int TotalDuration { get; set; }

    public TrackedProgram Program { get; set; }
}
