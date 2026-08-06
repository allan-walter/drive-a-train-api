using System.Collections.Concurrent;
using System.Threading.Channels;
using OpenCvSharp;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using DriveATrain;
using DriveATrain.Audio;
using DriveATrain.OpenCv;
using DriveATrain.Services;
using NAudio.Wave;
using NLayer.NAudioSupport;

public class BroadcastService : IHostedService, IDisposable
{
    private readonly Process _ffmpeg;
    private readonly CaptureService _captureService;
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _pumpTask; // capture -> ffmpeg -> broadcast, all in one loop
    private NamedPipeServerStream audioPipe;
    public EngineAudioSource engineAudio;

    public BroadcastService(CaptureService captureService)
    {
        _captureService = captureService;
        var size = $"{CaptureService.streamWidth}x{CaptureService.streamHeight}";
        // Determine audio pipe path per-OS, and set it up if needed
        string audioPipePath;

        if (OperatingSystem.IsWindows())
        {
            audioPipePath = @"\\.\pipe\engine_audio";
            // Windows named pipes are created elsewhere (e.g. NamedPipeServerStream) -
            // nothing to do here, ffmpeg just connects to it.
        }
        else
        {
            audioPipePath = "/tmp/engine_audio";

            // Create the FIFO if it doesn't already exist
            if (!File.Exists(audioPipePath))
            {
                var mkfifo = Process.Start(new ProcessStartInfo
                {
                    FileName = "mkfifo",
                    Arguments = audioPipePath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                mkfifo.WaitForExit();
            }
        }

        _ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-fflags nobuffer -flags low_delay -probesize 32 -analyzeduration 0 " +
                    $"-f rawvideo -pix_fmt bgr24 -s {size} -r {CaptureService.streamFps} -i pipe:0 " +
                    $"-c:v mpeg1video -qscale:v 3 -bf 0 -g 15 -f mpegts -muxdelay 0 -muxpreload 0 -flush_packets 1 -",
                // Arguments =
                //     $"-fflags nobuffer -flags low_delay -probesize 32 -analyzeduration 0 " +
                //     $"-f rawvideo -pix_fmt bgr24 -s {size} -r {CaptureService.streamFps} -i pipe:0 " +
                //     $"-f s16le -ar 44100 -ac 1 -i {audioPipePath} " +
                //     $"-map 0:v -map 1:a " +
                //     $"-c:v mpeg1video -qscale:v 3 -bf 0 -g 15 " +
                //     $"-c:a mp2 -b:a 128k -ar 44100 -ac 1 " +
                //     $"-f mpegts -muxdelay 0 -muxpreload 0 -flush_packets 1 -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        engineAudio = LoadEngineSound("Audio/engine.mp3");
    }

    private EngineAudioSource LoadEngineSound(string path)
    {
        var reader = new Mp3FileReaderBase(path, wf => new Mp3FrameDecompressor(wf)).ToSampleProvider();

        // Force to mono if the file is stereo — average channels
        int channels = reader.WaveFormat.Channels;
        int sourceRate = reader.WaveFormat.SampleRate;

        var raw = new List<float>();
        var buf = new float[reader.WaveFormat.SampleRate * channels];
        int read;
        while ((read = reader.Read(buf, 0, buf.Length)) > 0)
        {
            if (channels == 1)
            {
                raw.AddRange(buf.Take(read));
            }
            else
            {
                for (int i = 0; i < read; i += channels)
                {
                    float sum = 0;
                    for (int c = 0; c < channels; c++) sum += buf[i + c];
                    raw.Add(sum / channels);
                }
            }
        }

        return new EngineAudioSource(raw.ToArray(), sourceRate);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        audioPipe = new NamedPipeServerStream("engine_audio", PipeDirection.Out, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        _ffmpeg.Start();
        // One background task drives both the capture->stdin write and stdout->clients broadcast,
        // via two inner loops on the same Task so a single Stop/Dispose path covers everything.
        _pumpTask = Task.Run(() => RunPump(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        TryCloseStdin();

        if (_pumpTask != null)
            await Task.WhenAny(_pumpTask, Task.Delay(2000, CancellationToken.None));

        KillFfmpegIfRunning();
    }

    public void Dispose()
    {
        _cts.Cancel();
        TryCloseStdin();
        KillFfmpegIfRunning();
        _ffmpeg.Dispose();
        _cts.Dispose();
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

    // --- internals ---

    private async Task RunPump(CancellationToken token)
    {
        var captureTask = Task.Run(() => EncodeLoop(token), token);
        var audioTask = Task.Run(() => AudioLoop(token), token);
        var broadcastTask = BroadcastLoop(token);
        await Task.WhenAll(captureTask, broadcastTask, audioTask);
        // await Task.WhenAll(captureTask, broadcastTask);
    }

    void AudioLoop(CancellationToken token)
    {
        audioPipe.WaitForConnection();
        const int outputRate = 44100;
        const int chunkSamples = 882; // 20ms
        var outBuffer = new short[chunkSamples];
        var byteBuffer = new byte[chunkSamples * 2];
        var sw = Stopwatch.StartNew();
        var nextChunkTime = sw.Elapsed;
        var chunkInterval = TimeSpan.FromSeconds((double)chunkSamples / outputRate);

        while (!token.IsCancellationRequested)
        {
            var now = sw.Elapsed;
            if (now < nextChunkTime)
            {
                Thread.Sleep(1);
                continue;
            }

            nextChunkTime += chunkInterval;
            if (nextChunkTime < now) nextChunkTime = now + chunkInterval;

            engineAudio.Render(outBuffer, chunkSamples, outputRate);
            Buffer.BlockCopy(outBuffer, 0, byteBuffer, 0, byteBuffer.Length);

            try
            {
                audioPipe.Write(byteBuffer, 0, byteBuffer.Length);
            }
            catch
            {
                break;
            } // ffmpeg pipe closed/dead
        }
    }

    private void EncodeLoop(CancellationToken token)
    {
        using var frame = new Mat();
        var stdin = _ffmpeg.StandardInput.BaseStream;
        var frameBytes = CaptureService.streamWidth * CaptureService.streamHeight * 3;
        var buffer = new byte[frameBytes]; // local, not a field — no need to keep it alive on the instance

        while (!token.IsCancellationRequested)
        {
            // Waits (sleeping, no CPU spin) until CaptureService signals a new frame,
            // or the token is cancelled.
            try
            {
                _captureService.FrameReadySignal.Wait(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!_captureService.TryGetLatestFrame(frame) || frame.Empty())
            {
                continue;
            }

            lock (_captureService.debugOverlayLock)
            {
                using var expanded = new Mat();
                Cv2.Resize(_captureService.debugOverlayFrame, expanded,
                    new Size(CaptureService.CAMERA_WIDTH, CaptureService.CAMERA_HEIGHT));
                Blend.BlendOverlay(expanded, frame, 1);
            }

            Marshal.Copy(frame.Data, buffer, 0, frameBytes);
            try
            {
                stdin.Write(buffer, 0, frameBytes);
            }
            catch
            {
                break; // ffmpeg pipe closed/dead
            }
        }
    }

    private async Task BroadcastLoop(CancellationToken token)
    {
        var stdout = _ffmpeg.StandardOutput.BaseStream;
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

            if (read <= 0) break; // ffmpeg exited

            var chunk = buffer.AsMemory(0, read);
            var sends = _clients
                .Where(kvp => kvp.Value.State == WebSocketState.Open)
                .Select(kvp => SendToClientAsync(kvp.Key, kvp.Value, chunk, token));

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

    private void TryCloseStdin()
    {
        try
        {
            _ffmpeg.StandardInput.Close();
        }
        catch
        {
            /* already closed/exited */
        }
    }

    private void KillFfmpegIfRunning()
    {
        try
        {
            if (!_ffmpeg.HasExited)
                _ffmpeg.Kill();
        }
        catch
        {
            /* already gone */
        }
    }
}