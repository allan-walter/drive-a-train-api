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
    private readonly ILogger<PovVideoService> _logger;
    private Task? _pumpTask; // capture -> ffmpeg -> broadcast, all in one loop
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();

    public PovVideoService(Config config, ILogger<PovVideoService> logger)
    {
        _config = config.Camera;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        StartFfmpeg(_cts.Token);
        // One background task drives both the capture->stdin write and stdout->clients broadcast,
        // via two inner loops on the same Task so a single Stop/Dispose path covers everything.
        _pumpTask = Task.Run(() => BroadcastLoop(_cts.Token));

        return Task.CompletedTask;
    }

    private void StartFfmpeg(CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            // FileName = "ffmpeg", // Ensure ffmpeg is in system PATH or use full path like @"C:\ffmpeg\bin\ffmpeg.exe"
            // Otherwise it can't access streams on the network somehow
            FileName = OperatingSystem.IsLinux() ? "/usr/bin/ffmpeg" : "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // TODO on first run of the day the camera is still turning on at thi point But since it doesn't aupo turn off  it works next time
        string[] args =
        [
            "-hide_banner",
            "-nostats", // otherwise the per-frame progress lines bury any real error in stderr
            "-fflags", "nobuffer",
            "-probesize", "32k",
            "-reconnect", "1",
            "-reconnect_streamed", "1",
            "-reconnect_delay_max", "2",
            "-f", "mjpeg",
            "-i", _config.PovCameraUrl,
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

        _logger.LogInformation("Starting POV ffmpeg: {FileName} {Args}", psi.FileName, string.Join(' ', args));

        try
        {
            process = Process.Start(psi);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to start POV ffmpeg process ({FileName})", psi.FileName);
            throw;
        }

        if (process == null)
        {
            _logger.LogError("Process.Start returned null for POV ffmpeg ({FileName})", psi.FileName);
            throw new InvalidOperationException("Failed to start POV ffmpeg process.");
        }

        _logger.LogInformation("POV ffmpeg started, pid {Pid}, camera {Url}", process.Id, _config.PovCameraUrl);

        _ = Task.Run(() => DrainStderrAsync(process, token), token);
    }

    public async Task RegisterClientAsync(WebSocket socket, CancellationToken token)
    {
        var id = Guid.NewGuid();
        _clients[id] = socket;
        var buffer = new byte[1024];

        var exited = process == null || process.HasExited;
        _logger.LogInformation(
            "POV client {ClientId} connected ({ClientCount} total), ffmpeg {FfmpegState}",
            id, _clients.Count, exited ? "is NOT running" : "running");

        if (exited)
        {
            // The stream died before this client showed up, so it will never receive a frame.
            _logger.LogError(
                "POV client {ClientId} connected but ffmpeg is not running (exit code {ExitCode}); no video will be sent",
                id, process?.ExitCode);
        }

        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("POV client {ClientId} cancelled", id);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "POV client {ClientId} receive loop failed", id);
        }
        finally
        {
            _clients.TryRemove(id, out _);
            _logger.LogInformation("POV client {ClientId} disconnected ({ClientCount} remaining)", id, _clients.Count);
        }
    }

    private async Task DrainStderrAsync(Process process, CancellationToken token)
    {
        try
        {
            using var reader = process.StandardError;
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line == null) break;

                _logger.LogInformation("[POV FFMPEG] {Line}", line);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "POV ffmpeg stderr reader stopped");
        }
    }

    private async Task BroadcastLoop(CancellationToken token)
    {
        try
        {
            var stdout = process!.StandardOutput.BaseStream;
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

                if (read <= 0)
                {
                    // ffmpeg exited. Give it a moment so ExitCode and the last stderr lines are available.
                    try
                    {
                        await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
                    }
                    catch (TimeoutException)
                    {
                    }

                    _logger.LogError(
                        "POV ffmpeg stdout closed, exit code {ExitCode}. POV video is now dead until restart ({ClientCount} clients connected)",
                        process.HasExited ? process.ExitCode : (int?)null,
                        _clients.Count);
                    break;
                }

                var chunk = buffer.AsMemory(0, read);
                var sends = _clients
                    .Where(kvp => kvp.Value.State == WebSocketState.Open)
                    .Select(kvp => SendToClientAsync(kvp.Key, kvp.Value, chunk, token));

                await Task.WhenAll(sends);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _logger.LogError(e, "POV broadcast loop crashed");
        }
    }

    private async Task SendToClientAsync(Guid id, WebSocket socket, ReadOnlyMemory<byte> chunk, CancellationToken token)
    {
        try
        {
            await socket.SendAsync(chunk, WebSocketMessageType.Binary, true, token);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "POV send to client {ClientId} failed, dropping client", id);
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
            catch (Exception e)
            {
                _logger.LogDebug(e, "POV ffmpeg already exited before Kill");
            }
        }
    }
}
