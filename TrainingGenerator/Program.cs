using System.Runtime.InteropServices;
using DriveATrain.Services;

namespace TrainingGenerator;

using OpenCvSharp;
using System;
using System.Diagnostics;
using System.IO;

class TrainingFrameCapture
{
    const int Fps = 30; // match whatever fps your live capture uses

    static void Main(string[] args)
    {
        var outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DriveATrain",
            "Training Images");

        // Delete existing files to start fresh
        var dir = new DirectoryInfo(outputDir);
        if (dir.Exists)
        {
            foreach (var file in dir.GetFiles())
                file.Delete();
        }
        else
        {
            dir.Create();
        }

        var width = CaptureService.CAMERA_WIDTH;
        var height = CaptureService.CAMERA_HEIGHT;
        var frameSize = width * height * 3; // bgr24 = 3 bytes/pixel
        var flip = false;
        var flipFilter = flip ? "-vf hflip " : "";

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments =
                $"-f v4l2 -vcodec mjpeg -video_size {width}x{height} -framerate {Fps} -i /dev/video0 " +
                $"-pix_fmt bgr24 {flipFilter}-f rawvideo -an -sn -",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            Console.WriteLine("Failed to start ffmpeg process.");
            return;
        }

        // Drain stderr so ffmpeg doesn't block on a full pipe buffer
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();

        var stdout = process.StandardOutput.BaseStream;
        var buffer = new byte[frameSize];

        bool ReadFrame()
        {
            var offset = 0;
            while (offset < frameSize)
            {
                var read = stdout.Read(buffer, offset, frameSize - offset);
                if (read <= 0)
                    return false; // stream ended / ffmpeg died
                offset += read;
            }

            return true;
        }

        // 1. Skip the first 50 frames for lighting/auto-exposure to settle
        Console.WriteLine("Waiting for camera auto-exposure to settle...");
        int skippedFrames = 0;
        while (skippedFrames < 50 && ReadFrame())
        {
            skippedFrames++;
        }

        // 2. Capture and save the next 50 stable frames
        Console.WriteLine("Capturing 50 training frames...");
        int savedFrames = 0;
        const int maxFramesToSave = 50;
        while (savedFrames < maxFramesToSave && ReadFrame())
        {
            using var frame = new Mat(height, width, MatType.CV_8UC3);
            Marshal.Copy(buffer, 0, frame.Data, frameSize);

            var fileName = Path.Combine(outputDir, $"frame_{savedFrames:D2}.jpg");
            Cv2.ImWrite(fileName, frame);
            savedFrames++;
        }

        // Clean up
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch
        {
            // ignore
        }

        process.WaitForExit();

        Console.WriteLine(
            $"Done! Discarded {skippedFrames} warmup frames and saved {savedFrames} frames to {outputDir}");
    }
}