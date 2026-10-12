namespace FocusTracker.Domain.Interfaces
{
    public interface ITaskCompletionService
    {
        // Завершує задачу, нараховує XP навичці й повертає нараховане XP (0, якщо нового часу не було)
        int Complete(int taskId);

        // XP за секунди роботи: 1 XP за хвилину × складність
        static int CalculateXp(int seconds, int difficulty) =>
            seconds <= 0 ? 0 : seconds * Math.Max(difficulty, 1) / 60;
    }
}
