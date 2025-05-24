using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Models;

public class RestrictionToProgram
{
    public int Id { get; set; }
    public int RestrictionId { get; set; }
    public int ProgramId { get; set; }

    public Restriction Restriction { get; set; }
    public TrackedProgram Program { get; set; }
}
