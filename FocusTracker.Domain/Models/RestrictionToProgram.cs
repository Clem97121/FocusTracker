namespace FocusTracker.Domain.Models;

public class RestrictionToProgram
{
    public int Id { get; set; }

    public int RestrictionId { get; set; }
    public int ProgramId { get; set; }

    public Restriction Restriction { get; set; } = null!;
    public TrackedProgram Program { get; set; } = null!;
}
