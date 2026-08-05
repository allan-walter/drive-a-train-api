using System.Collections.Concurrent;
using System.Threading.Channels;
using OpenCvSharp;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using DriveATrain;
using DriveATrain.Audio;
using DriveATrain.OpenCv;
using DriveATrain.Services;
using NAudio.Wave;
using NLayer.NAudioSupport;

// unlike the other feed which has capture and encode in seperate step so we can do stuff to the raw frame this reads and encodes in one step
// That might have to change in the future
public class PovBroadcastService : IHostedService, IDisposable
{
    private readonly PovCaptureService _captureService;
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _pumpTask; // capture -> ffmpeg -> broadcast, all in one loop

    public PovBroadcastService(PovCaptureService captureService)
    {
        _captureService = captureService;
    }


    public Task StartAsync(CancellationToken cancellationToken)
    {
        // One background task drives both the capture->stdin write and stdout->clients broadcast,
        // via two inner loops on the same Task so a single Stop/Dispose path covers everything.
        _pumpTask = Task.Run(() => RunPump(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();

        if (_pumpTask != null)
            await Task.WhenAny(_pumpTask, Task.Delay(2000, CancellationToken.None));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    public async Task RegisterClientAsync(WebSocket socket, CancellationToken token)
    {
        var id = Guid.NewGuid();
        _clients[id] = socket;
        var buffer = new byte[1024];

        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch
        {
            /* client disconnected */
        }
        finally
        {
            _clients.TryRemove(id, out _);
        }
    }

    // --- internals ---

    private async Task RunPump(CancellationToken token)
    {
        try
        {
            await BroadcastLoop(token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[POV broadcast] pump failed: {ex}");
        }
    }

    private async Task BroadcastLoop(CancellationToken token)
    {
        while (_captureService.process == null && !token.IsCancellationRequested)
            await Task.Delay(50, token);

        var process = _captureService.process;
        if (process == null)
            return;

        var stdout = process.StandardOutput.BaseStream;
        var buffer = new byte[64 * 1024];

        while (!token.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await stdout.ReadAsync(buffer, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (read <= 0) break; // ffmpeg exited

            var chunk = buffer.AsMemory(0, read);
            var sends = _clients
                .Where(kvp => kvp.Value.State == WebSocketState.Open)
                .Select(kvp => SendToClientAsync(kvp.Key, kvp.Value, chunk, token));

            await Task.WhenAll(sends);
        }
    }

    private async Task SendToClientAsync(Guid id, WebSocket socket, ReadOnlyMemory<byte> chunk, CancellationToken token)
    {
        try
        {
            await socket.SendAsync(chunk, WebSocketMessageType.Binary, true, token);
        }
        catch
        {
            _clients.TryRemove(id, out _);
        }
    }
}