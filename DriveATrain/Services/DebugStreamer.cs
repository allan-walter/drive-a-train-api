using System.Collections.Concurrent;
using OpenCvSharp;
using System.Diagnostics;
using System.Net.WebSockets;
using DriveATrain;
using DriveATrain.Services;

public class DebugStreamer : IHostedService
{
    private readonly Process _ffmpeg;
    private CancellationTokenSource _cts = new();

    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly DetectorService _detectorService;

    private byte[] _streamBuffer = [];

    public DebugStreamer(Config config, DetectorService detectorService)
    {
        _detectorService = detectorService;

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
        Task.Run(() => BroadcastLoop(_cts.Token));
    }

    private void CaptureLoop(CancellationToken token)
    {
        using var frame = new Mat();
        var stdin = _ffmpeg.StandardInput.BaseStream;
        var frameBytes = DetectorService.STREAM_WIDTH * DetectorService.STREAM_HEIGHT * 3;
        _streamBuffer = new byte[frameBytes];

        while (!token.IsCancellationRequested)
        {
            if (_detectorService.currentDebugFrame.Empty())
                continue;
            
            _detectorService.currentDebugFrame.CopyTo(frame);

            System.Runtime.InteropServices.Marshal.Copy(frame.Data, _streamBuffer, 0, frameBytes);
            stdin.Write(_streamBuffer, 0, frameBytes);
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
