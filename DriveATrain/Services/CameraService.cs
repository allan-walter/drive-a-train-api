using System.Net.Sockets;

namespace DriveATrain.Services;

public class CameraService : IHostedService
{
    public static double CAMERA_WIDTH = 640.0;
    public static double CAMERA_HEIGHT = 360.0;
    public static int STREAM_WIDTH = 1920;
    public static int STREAM_HEIGHT = 1080;
    public static int STREAM_FPS = 30;


    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask; // returns immediately, loop runs in background
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
    }
}