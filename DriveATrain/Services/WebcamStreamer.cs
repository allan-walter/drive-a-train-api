using System.Collections.Concurrent;
using OpenCvSharp;
using System.Diagnostics;
using System.Net.WebSockets;
using DriveATrain.Hubs;
using Microsoft.AspNetCore.SignalR;

public class WebcamStreamer
{
    private readonly VideoCapture _capture;
    private readonly Process _ffmpeg;
    private CancellationTokenSource _cts = new();

    // private const int Width = 640;
    // private const int Height = 360;
    private const int Width = 1920;
    private const int Height = 1080;
    private const int Fps = 25;
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();

    public WebcamStreamer()
    {
        _capture = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
        _capture.Set(VideoCaptureProperties.FrameWidth, Width);
        _capture.Set(VideoCaptureProperties.FrameHeight, Height);
        _capture.Set(VideoCaptureProperties.Fps, Fps);

        _ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-f rawvideo -pix_fmt bgr24 -s {Width}x{Height} -r {Fps} -i pipe:0 " +
                    "-f mpegts -codec:v mpeg1video -b:v 1000k -bf 0 -",
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

        // Keep the connection open; JSMpeg only reads, doesn't send anything meaningful
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

        // Thread 1: push webcam frames into ffmpeg stdin
        Task.Run(() => CaptureLoop(_cts.Token));

        // Thread 2: read encoded output and broadcast over SignalR
        Task.Run(() => BroadcastLoop(_cts.Token));
    }

    private void CaptureLoop(CancellationToken token)
    {
        using var frame = new Mat();
        var stdin = _ffmpeg.StandardInput.BaseStream;

        while (!token.IsCancellationRequested)
        {
            if (!_capture.Read(frame) || frame.Empty())
                continue;

            // Mat data is contiguous BGR24 for a standard camera read
            var bytes = new byte[frame.Total() * frame.ElemSize()];
            System.Runtime.InteropServices.Marshal.Copy(frame.Data, bytes, 0, bytes.Length);

            stdin.Write(bytes, 0, bytes.Length);
            stdin.Flush();
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

            var segment = new ArraySegment<byte>(buffer, 0, read);

            foreach (var kvp in _clients)
            {
                var socket = kvp.Value;
                if (socket.State != WebSocketState.Open) continue;

                try
                {
                    await socket.SendAsync(segment, WebSocketMessageType.Binary, true, token);
                }
                catch
                {
                    _clients.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    public void Stop()
    {
        _cts.Cancel();
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
}