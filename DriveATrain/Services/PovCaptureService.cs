using System.Diagnostics;
using DriveATrain.OpenCv;
using OpenCvSharp;

namespace DriveATrain.Services;

public class PovCaptureService : IHostedService
{
    // public const int CAMERA_WIDTH = 640;
    //
    // public const int CAMERA_HEIGHT = 480;
    public const int CAMERA_WIDTH = 320;
    public const int CAMERA_HEIGHT = 240;

    public const int fps = 30;

    public const int streamWidth = 320;
    public const int streamHeight = 240;
    public const int streamFps = 30;

    public Process? process;
    private CameraConfig config;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;


    public PovCaptureService(Config config)
    {
        this.config = config.Camera;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _captureTask = Task.Run(() => Capture(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private void Capture(CancellationToken token)
    {
        int frameSize = CAMERA_WIDTH * CAMERA_HEIGHT * 3;

        ProcessStartInfo psi;

        string url = "http://192.168.20.100:81/stream";
        psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments =
                $"-f mjpeg " + // Explicit input format
                $"-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 2 " +
                $"-i {url} " +
                $"-c:v mpeg1video -b:v 1000k -pix_fmt yuv420p -bf 0 " +
                $"-f mpegts -",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // psi = new ProcessStartInfo
        // {
        //     FileName = "ffmpeg",
        //     Arguments =
        //         $"-fflags nobuffer -flags low_delay -probesize 32 -analyzeduration 0 " +
        //         $"-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 2 " +
        //         $"-f mjpeg -i {url} " + // Added -f mjpeg here
        //         $"-c:v mpeg1video -qscale:v 3 -bf 0 -g 15 -f mpegts -muxdelay 0 -muxpreload 0 -flush_packets 1 -",
        //     RedirectStandardOutput = true,
        //     RedirectStandardError = true,
        //     UseShellExecute = false,
        //     CreateNoWindow = true
        // };

        process = Process.Start(psi);
        if (process == null) return;

        // Drain stderr continuously so ffmpeg never blocks writing logs.
        _ = Task.Run(async () =>
        {
            try
            {
                using var reader = process.StandardError;
                while (!reader.EndOfStream)
                    Debug.WriteLine($"[FFMPEG] {await reader.ReadLineAsync()}");
            }
            catch
            {
                /* process exiting, ignore */
            }
        }, token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
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

        if (_captureTask != null)
            await Task.WhenAny(_captureTask, Task.Delay(2000, cancellationToken));
    }
}