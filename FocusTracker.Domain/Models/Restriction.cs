using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Models;

public class Restriction : ISyncEntity
{
    public int Id { get; set; }
    public Guid SyncId { get; set; }
    public DateTime? SyncUpdatedAt { get; set; }
    public string Note { get; set; }

    public ICollection<RestrictionRule> Rules { get; set; }
    public ICollection<RestrictionToProgram> Targets { get; set; }
}
