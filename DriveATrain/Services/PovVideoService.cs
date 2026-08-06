using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using DriveATrain.OpenCv;

namespace DriveATrain.Services;

public class PovVideoService : IHostedService
{
    public const int CAMERA_WIDTH = 320;
    public const int CAMERA_HEIGHT = 240;
    public const int fps = 30;
    public const int streamWidth = 320;
    public const int streamHeight = 240;
    public const int streamFps = 30;

    public Process? process;
    private readonly CameraConfig _config;
    private Task? _pumpTask; // capture -> ffmpeg -> broadcast, all in one loop
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();

    public PovVideoService(Config config)
    {
        _config = config.Camera;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        StartFfmpeg(_cts.Token);
        // One background task drives both the capture->stdin write and stdout->clients broadcast,
        // via two inner loops on the same Task so a single Stop/Dispose path covers everything.
        _pumpTask = Task.Run(() => RunPump(_cts.Token));

        return Task.CompletedTask;
    }

    private void StartFfmpeg(CancellationToken token)
    {
        const string url = "http://192.168.20.100:81/stream";

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg", // Ensure ffmpeg is in system PATH or use full path like @"C:\ffmpeg\bin\ffmpeg.exe"
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        string[] args =
        [
            "-fflags", "nobuffer",
            "-probesize", "32k",
            "-reconnect", "1",
            "-reconnect_streamed", "1",
            "-reconnect_delay_max", "2",
            "-f", "mjpeg",
            "-i", url, // Passed cleanly with zero escaping logic required
            "-c:v", "mpeg1video",
            "-b:v", "1000k",
            "-pix_fmt", "yuv420p",
            "-bf", "0",
            "-g", "15",
            "-f", "mpegts",
            "-muxdelay", "0",
            "-muxpreload", "0",
            "-flush_packets", "1",
            "-"
        ];

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start POV ffmpeg process.");

        _ = Task.Run(() => DrainStderrAsync(process, token), token);
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

    private static async Task DrainStderrAsync(Process process, CancellationToken token)
    {
        try
        {
            using var reader = process.StandardError;
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line == null) break;
                
                Debug.WriteLine($"[POV FFMPEG] {line}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            /* process exiting */
        }
    }

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

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts != null)
            await _cts.CancelAsync();


        if (_pumpTask != null)
            await Task.WhenAny(_pumpTask, Task.Delay(2000, CancellationToken.None));

        if (process != null && !process.HasExited)
        {
            try
            {
                process.Kill();
            }
            catch
            {
                /* already exited */
            }
        }
    }
}