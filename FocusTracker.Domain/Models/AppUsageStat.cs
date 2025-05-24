public class AppUsageStat
{
    public int Id { get; set; }
    public string AppName { get; set; }
    public DateTime Date { get; set; }
    public TimeSpan TotalTime { get; set; }
    public TimeSpan ActiveTime { get; set; }
}
