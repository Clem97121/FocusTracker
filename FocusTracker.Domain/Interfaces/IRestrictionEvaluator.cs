using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Interfaces
{
    public interface IRestrictionEvaluator
    {
        bool TryGetViolatedRestrictions(string appName, TimeSpan activeTime, out List<string> violatedNotes);

    }

}
