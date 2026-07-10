using System.Collections.Concurrent;
using System.Threading.Channels;
using OpenCvSharp;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using DriveATrain;
using DriveATrain.Services;

public class BroadcastService : IHostedService, IDisposable
{
    private readonly Process _ffmpeg;
    private readonly CaptureService _captureService;
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _pumpTask; // capture -> ffmpeg -> broadcast, all in one loop

    public BroadcastService(CaptureService captureService)
    {
        _captureService = captureService;
        var size = $"{CaptureService.streamWidth}x{CaptureService.streamHeight}";

        _ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-fflags nobuffer -flags low_delay -probesize 32 -analyzeduration 0 " +
                    $"-f rawvideo -pix_fmt bgr24 -s {size} -r {CaptureService.streamFps} -i pipe:0 " +
                    $"-c:v mpeg1video -qscale:v 3 -bf 0 -g 15 -f mpegts -muxdelay 0 -muxpreload 0 -flush_packets 1 -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ffmpeg.Start();
        // One background task drives both the capture->stdin write and stdout->clients broadcast,
        // via two inner loops on the same Task so a single Stop/Dispose path covers everything.
        _pumpTask = Task.Run(() => RunPump(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        TryCloseStdin();

        if (_pumpTask != null)
            await Task.WhenAny(_pumpTask, Task.Delay(2000, CancellationToken.None));

        KillFfmpegIfRunning();
    }

    public void Dispose()
    {
        _cts.Cancel();
        TryCloseStdin();
        KillFfmpegIfRunning();
        _ffmpeg.Dispose();
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
        var captureTask = Task.Run(() => EncodeLoop(token), token);
        var broadcastTask = BroadcastLoop(token);
        await Task.WhenAll(captureTask, broadcastTask);
    }

    private void EncodeLoop(CancellationToken token)
    {
        using var frame = new Mat();
        var stdin = _ffmpeg.StandardInput.BaseStream;
        var frameBytes = CaptureService.streamWidth * CaptureService.streamHeight * 3;
        var buffer = new byte[frameBytes]; // local, not a field — no need to keep it alive on the instance


        var frameInterval = TimeSpan.FromSeconds(1.0 / CaptureService.streamFps);
        var sw = Stopwatch.StartNew();
        var nextFrameTime = sw.Elapsed;
        int frameCount = 0;
        var lastLog = sw.Elapsed;
        while (!token.IsCancellationRequested)
        {
            if (!_captureService.TryGetLatestFrame(frame) || frame.Empty())
            {
                Task.Delay(5); // avoid a hot spin when no frame is ready yet
                continue;
            }

            // IT won't error if we throw more frames at the decoder but it does slow it down for no benefit
            // This is done with a timer instead a simple frame change since there might be different frame rates that we capture at vs broadcasting
            var now = sw.Elapsed;
            if (now < nextFrameTime)
            {
                var wait = nextFrameTime - now;
                if (wait > TimeSpan.FromMilliseconds(1))
                    Task.Delay(wait);
                continue;
            }

            nextFrameTime += frameInterval;
            // if we fell badly behind, don't try to "catch up" by bursting frames
            if (nextFrameTime < now)
                nextFrameTime = now + frameInterval;

            // Cv2.ImShow("frame", frame);
            // Cv2.WaitKey(1);

            Marshal.Copy(frame.Data, buffer, 0, frameBytes);

            try
            {
                stdin.Write(buffer, 0, frameBytes);
                frameCount++;
            }
            catch
            {
                break; // ffmpeg pipe closed/dead
            }

            if (sw.Elapsed - lastLog > TimeSpan.FromSeconds(1))
            {
                // Console.WriteLine($"[encode] fps written: {frameCount}");
                frameCount = 0;
                lastLog = sw.Elapsed;
            }
        }
    }

    private async Task BroadcastLoop(CancellationToken token)
    {
        var stdout = _ffmpeg.StandardOutput.BaseStream;
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

    private void TryCloseStdin()
    {
        try
        {
            _ffmpeg.StandardInput.Close();
        }
        catch
        {
            /* already closed/exited */
        }
    }

    private void KillFfmpegIfRunning()
    {
        try
        {
            if (!_ffmpeg.HasExited)
                _ffmpeg.Kill();
        }
        catch
        {
            /* already gone */
        }
    }
}