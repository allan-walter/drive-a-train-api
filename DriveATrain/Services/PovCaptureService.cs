using System.Diagnostics;
using DriveATrain.OpenCv;

namespace DriveATrain.Services;

public class PovCaptureService : IHostedService
{
    public const int CAMERA_WIDTH = 320;
    public const int CAMERA_HEIGHT = 240;
    public const int fps = 30;
    public const int streamWidth = 320;
    public const int streamHeight = 240;
    public const int streamFps = 30;

    public Process? process;
    private readonly CameraConfig _config;
    private CancellationTokenSource? _cts;

    public PovCaptureService(Config config)
    {
        _config = config.Camera;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        StartFfmpeg(_cts.Token);
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

    private static async Task DrainStderrAsync(Process process, CancellationToken token)
    {
        try
        {
            using var reader = process.StandardError;
            while (!token.IsCancellationRequested && !reader.EndOfStream)
                Debug.WriteLine($"[POV FFMPEG] {await reader.ReadLineAsync(token)}");
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            /* process exiting */
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts != null)
            await _cts.CancelAsync();

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