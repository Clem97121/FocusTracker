using Android.Content;
using Android.Runtime;
using AndroidX.Work;
using FocusTracker.Mobile.Services;

namespace FocusTracker.Mobile.Platforms.Android;

// Фонова синхронізація раз на 15 хвилин (мінімум WorkManager), навіть коли застосунок закрито
[Register("com.clem97121.focustracker.SyncWorker")]
public class SyncWorker : Worker
{
    private const string UniqueName = "focustracker-sync";

    public SyncWorker(Context context, WorkerParameters parameters) : base(context, parameters)
    {
    }

    public override Result DoWork()
    {
        try
        {
            Task.Run(() => new PhoneSync().SyncAsync()).GetAwaiter().GetResult();
            return Result.InvokeSuccess();
        }
        catch
        {
            // Без мережі чи входу просто чекаємо наступного разу
            return Result.InvokeRetry();
        }
    }

    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)
            .Build();
        var request = new PeriodicWorkRequest.Builder(typeof(SyncWorker), TimeSpan.FromMinutes(15))
            .SetConstraints(constraints)
            .Build();
        WorkManager.GetInstance(context).EnqueueUniquePeriodicWork(UniqueName, ExistingPeriodicWorkPolicy.Keep!, request);
    }
}
