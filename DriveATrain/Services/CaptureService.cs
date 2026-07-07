using OpenCvSharp;

namespace DriveATrain.Services;

public class CaptureService : IHostedService, IDisposable
{
    private readonly CancellationTokenSource tokenSource = new();
    private readonly VideoCapture capture;
    private Task? captureTask;
    private readonly Mat latestFrame = new();

    public CaptureService(Config config)
    {
        if (OperatingSystem.IsWindows())
        {
            int index = int.Parse(config.Vision.Camera);
            capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
        }
        else
        {
            capture = new VideoCapture(config.Vision.Camera, VideoCaptureAPIs.V4L2);
            capture.Set(VideoCaptureProperties.BufferSize, 1);
        }

        capture.Set(VideoCaptureProperties.FrameWidth, DetectorService.CAMERA_WIDTH);
        capture.Set(VideoCaptureProperties.FrameHeight, DetectorService.CAMERA_HEIGHT);
    }

    private void CaptureLoop(CancellationToken token)
    {
        using var frame = new Mat();
        while (!token.IsCancellationRequested)
        {
            if (!capture.Read(frame) || frame.Empty())
            {
                Thread.Sleep(10); // avoid busy-spin on read failure
                continue;
            }

            Cv2.Flip(frame, frame, FlipMode.Y);
            lock (latestFrame)
            {
                frame.CopyTo(latestFrame);
            }
        }
    }

    public bool TryGetLatestFrame(Mat dest)
    {
        lock (latestFrame)
        {
            if (latestFrame.Empty()) return false;
            latestFrame.CopyTo(dest);
            return true;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        captureTask = Task.Run(() => CaptureLoop(tokenSource.Token), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await tokenSource.CancelAsync();

        if (captureTask != null)
        {
            // Wait for the loop to actually exit, but don't hang forever
            await Task.WhenAny(captureTask, Task.Delay(Timeout.Infinite, cancellationToken))
                .ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        tokenSource.Cancel();
        tokenSource.Dispose();
        capture.Dispose();
    }
}