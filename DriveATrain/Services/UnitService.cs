using DriveATrain.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace DriveATrain.Services;

public class UnitService(IHubContext<UnitHub> unitHub) : IHostedService
{
    private CancellationTokenSource? _cts;
    private Task? _task;

    private object liveDataLock = new();
    private LiveData? LiveData { get; set; }

    public void SetLiveData(LiveData liveData)
    {
        lock (liveDataLock)
            LiveData = liveData;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _task = DoWorkAsync(_cts.Token);

        return Task.CompletedTask;
    }

    private async Task DoWorkAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                LiveData liveDataSnapshot;
                lock (liveDataLock)
                {
                    liveDataSnapshot = LiveData;
                }

                await unitHub.Clients.All.SendAsync("units", liveDataSnapshot);
                // await unitHub.Clients.All.SendAsync("connections", connections);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown on stop
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_task == null)
        {
            return;
        }

        _cts?.Cancel();

        // Wait for the background task to finish before returning
        await Task.WhenAny(_task, Task.Delay(Timeout.Infinite, cancellationToken));
    }
}