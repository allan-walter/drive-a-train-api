using System.Diagnostics;
using DriveATrain.OpenCv;
using OpenCvSharp;

namespace DriveATrain.Services;

public class CaptureService : IHostedService
{
    // public const int CAMERA_WIDTH = 640;
    //
    // public const int CAMERA_HEIGHT = 480;
    public const int CAMERA_WIDTH = 1920;
    public const int CAMERA_HEIGHT = 1080;

    public const int fps = 30;

    public const int DETECTION_WIDTH = CAMERA_WIDTH / 4;

    public const int DETECTION_HEIGHT = CAMERA_HEIGHT / 4;
    // public const int detectionWidth = (int)CAMERA_WIDTH;
    // public const int detectionHeight = (int)CAMERA_HEIGHT;

    public const int streamWidth = 1920;
    public const int streamHeight = 1080;
    public const int streamFps = 30;

    private Process? _process;
    private CameraConfig config;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;

    private Mat latestFrame = new Mat();
    public Mat latestFrameLock = new();

    public Mat debugOverlayFrame =
        new Mat(new Size(DETECTION_WIDTH, DETECTION_HEIGHT), MatType.CV_8UC4, new Scalar(0, 0, 0, 0));

    public object debugOverlayLock = new();

    public CaptureService(Config config)
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
        int frameSize = (int)CAMERA_WIDTH * (int)CAMERA_HEIGHT * 3;

        ProcessStartInfo psi;
        string flipFilter = config.Flip ? "-vf hflip " : "";

        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-f dshow -vcodec mjpeg -video_size {CAMERA_WIDTH}x{CAMERA_HEIGHT} -framerate {fps} -i video=\"Brio 100\" " +
                    $"-pix_fmt bgr24 {flipFilter}-f rawvideo -an -sn -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-f v4l2 -vcodec mjpeg -video_size {CAMERA_WIDTH}x{CAMERA_HEIGHT} -framerate {fps} -i /dev/video0 " +
                    $"-pix_fmt bgr24 {flipFilter}-f rawvideo -an -sn -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        _process = Process.Start(psi);
        if (_process == null) return;

        // Drain stderr continuously so ffmpeg never blocks writing logs.
        _ = Task.Run(async () =>
        {
            try
            {
                using var reader = _process.StandardError;
                while (!reader.EndOfStream)
                    await reader.ReadLineAsync();
            }
            catch
            {
                /* process exiting, ignore */
            }
        }, token);

        var stdout = _process.StandardOutput.BaseStream;
        var buffer = new byte[frameSize];

        while (!token.IsCancellationRequested)
        {
            int totalRead = 0;
            while (totalRead < frameSize)
            {
                int bytesRead = stdout.Read(buffer, totalRead, frameSize - totalRead);
                if (bytesRead <= 0) return; // pipe closed / ffmpeg exited
                totalRead += bytesRead;
            }

            using var frame = Mat.FromPixelData((int)CAMERA_HEIGHT, (int)CAMERA_WIDTH, MatType.CV_8UC3, buffer);
            Cv2.Flip(frame, frame, FlipMode.Y);

            lock (latestFrameLock)
            {
                frame.CopyTo(latestFrame);
            }
        }
    }

    public bool TryGetLatestFrame(Mat dest)
    {
        lock (latestFrameLock)
        {
            if (latestFrame.Empty()) return false;
            latestFrame.CopyTo(dest);
        }

        return true;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (latestFrameLock)
            latestFrame.Dispose();

        _cts?.Cancel();
        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.Kill();
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