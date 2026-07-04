using System.Collections.Concurrent;
using System.Threading.Channels;
using OpenCvSharp;
using System.Diagnostics;
using System.Net.WebSockets;
using DriveATrain;
using DriveATrain.Services;

public class WebcamStreamer : IHostedService
{
    private readonly VideoCapture _capture;
    private readonly Process _ffmpeg;
    private CancellationTokenSource _cts = new();

    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly DetectorService _detectorService;
    private readonly Channel<Mat> _detectionFrames = Channel.CreateBounded<Mat>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });

    private byte[] _streamBuffer = [];

    public WebcamStreamer(Config config, DetectorService detectorService)
    {
        _detectorService = detectorService;
        if (OperatingSystem.IsWindows())
        {
            int index = int.Parse(config.Vision.Camera);
            _capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
        }
        else
        {
            _capture = new VideoCapture(config.Vision.Camera, VideoCaptureAPIs.V4L2);
        }

        _capture.Set(VideoCaptureProperties.FrameWidth, DetectorService.CAMERA_WIDTH);
        _capture.Set(VideoCaptureProperties.FrameHeight, DetectorService.CAMERA_HEIGHT);

        var streamSize = $"{DetectorService.STREAM_WIDTH}x{DetectorService.STREAM_HEIGHT}";
        _ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-fflags nobuffer -flags low_delay -probesize 32 -analyzeduration 0 " +
                    $"-f rawvideo -pix_fmt bgr24 -s {streamSize} -r {DetectorService.STREAM_FPS} -i pipe:0 " +
                    $"-c:v mpeg1video -b:v 1000k -bf 0 -g 1 -f mpegts -muxdelay 0 -muxpreload 0 -flush_packets 1 -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
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

    public void Start()
    {
        _ffmpeg.Start();

        Task.Run(() => CaptureLoop(_cts.Token));
        Task.Run(() => DetectionLoop(_cts.Token));
        Task.Run(() => BroadcastLoop(_cts.Token));

        DebugWindow.Start();
    }

    private void CaptureLoop(CancellationToken token)
    {
        using var frame = new Mat();
        var stdin = _ffmpeg.StandardInput.BaseStream;
        var frameBytes = DetectorService.STREAM_WIDTH * DetectorService.STREAM_HEIGHT * 3;
        _streamBuffer = new byte[frameBytes];

        while (!token.IsCancellationRequested)
        {
            if (!_capture.Read(frame) || frame.Empty())
                continue;

            Cv2.Flip(frame, frame, FlipMode.Y);

            System.Runtime.InteropServices.Marshal.Copy(frame.Data, _streamBuffer, 0, frameBytes);
            stdin.Write(_streamBuffer, 0, frameBytes);

            if (_detectionFrames.Writer.TryWrite(frame.Clone()))
                continue;

            // Channel full — drop the oldest pending frame and enqueue the latest.
            while (_detectionFrames.Reader.TryRead(out var dropped))
                dropped.Dispose();

            _detectionFrames.Writer.TryWrite(frame.Clone());
        }
    }

    private async Task DetectionLoop(CancellationToken token)
    {
        try
        {
            await foreach (var frame in _detectionFrames.Reader.ReadAllAsync(token))
            {
                using (frame)
                    _detectorService.Process(frame);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task BroadcastLoop(CancellationToken token)
    {
        var stdout = _ffmpeg.StandardOutput.BaseStream;
        var buffer = new byte[64 * 1024];

        while (!token.IsCancellationRequested)
        {
            int read = await stdout.ReadAsync(buffer, 0, buffer.Length, token);
            if (read <= 0) continue;

            var chunk = buffer.AsMemory(0, read);
            var sends = new List<Task>(_clients.Count);

            foreach (var kvp in _clients)
            {
                var socket = kvp.Value;
                if (socket.State != WebSocketState.Open) continue;

                sends.Add(SendToClientAsync(kvp.Key, socket, chunk, token));
            }

            if (sends.Count > 0)
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

    public void Stop()
    {
        _cts.Cancel();
        _detectionFrames.Writer.TryComplete();
        _capture.Release();
        try
        {
            _ffmpeg.StandardInput.Close();
        }
        catch
        {
        }

        if (!_ffmpeg.HasExited) _ffmpeg.Kill();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        return Task.CompletedTask;
    }
}
