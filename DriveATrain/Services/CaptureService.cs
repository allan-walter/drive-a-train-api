using System.Diagnostics;
using OpenCvSharp;

namespace DriveATrain.Services;

public class CaptureService : IHostedService
{
    public const int width = 1920;
    public const int height = 1080;
    public const int fps = 30;

    public const int streamWidth = 1920;
    public const int streamHeight = 1080;
    public const int streamFps = 30;

    private Process? _process;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;

    private Mat latestFrame = new Mat();
    public Mat latestFrameLock = new();

    public Mat debugOverlayFrame = new Mat(new Size(width, height), MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
    public object debugOverlayLock = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _captureTask = Task.Run(() => Capture(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private void Capture(CancellationToken token)
    {
        int frameSize = width * height * 3;

        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-f dshow -vcodec mjpeg -video_size {width}x{height} -framerate {fps} -i video=\"Brio 100\" " +
                    $"-pix_fmt bgr24 -f rawvideo -an -sn -",
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
                Arguments = $"-f v4l2 -vcodec mjpeg -video_size {width}x{height} -framerate {fps} -i /dev/video0 " +
                            $"-pix_fmt bgr24 -f rawvideo -an -sn -",
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

            using var frame = Mat.FromPixelData(height, width, MatType.CV_8UC3, buffer);
            Cv2.Flip(frame, frame, FlipMode.Y);

            lock (latestFrameLock)
            {
                frame.CopyTo(latestFrame);


                // Capture framerate is way heigher than detection, so its fine to just do the overlay here on new cameara frame rather than after actual detection
                lock (debugOverlayLock)
                {
                    // Split debug layer into BGR + alpha
                    Mat[] channels = Cv2.Split(debugOverlayFrame);
                    Mat bgr = new Mat();
                    Cv2.Merge(new Mat[] { channels[0], channels[1], channels[2] }, bgr);
                    Mat alphaMask = channels[3]; // single channel, 0 or 255

                    // Copy only where alpha > 0
                    bgr.CopyTo(latestFrame, alphaMask);
                }
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