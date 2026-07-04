namespace TrainingGenerator;

using OpenCvSharp;
using System;
using System.IO;

class TrainingFrameCapture
{
    private const int CameraWidth = 1280; // set to your actual CAMERA_WIDTH
    private const int CameraHeight = 720; // set to your actual CAMERA_HEIGHT

    static void Main(string[] args)
    {
        var outputDir = @"Images\Training";

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

        VideoCapture camera;

        if (OperatingSystem.IsWindows())
        {
            camera = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
        }
        else
        {
            camera = new VideoCapture("/dev/ttyACM0", VideoCaptureAPIs.V4L2);
        }

        camera.Set(VideoCaptureProperties.FrameWidth, CameraWidth);
        camera.Set(VideoCaptureProperties.FrameHeight, CameraHeight);

        using var frame = new Mat();

        // 1. Skip the first 50 frames for lighting/auto-exposure to settle
        Console.WriteLine("Waiting for camera auto-exposure to settle...");
        int skippedFrames = 0;
        while (skippedFrames < 50 && camera.Read(frame) && !frame.Empty())
        {
            skippedFrames++;
        }

        // 2. Capture and save the next 50 stable frames
        Console.WriteLine("Capturing 50 training frames...");
        int savedFrames = 0;
        const int maxFramesToSave = 50;

        while (savedFrames < maxFramesToSave && camera.Read(frame) && !frame.Empty())
        {
            var flip = true;
            if (flip)
            {
                Cv2.Flip(frame, frame, FlipMode.Y); // 1 in OpenCV = horizontal flip = FlipMode.Y
            }

            var fileName = Path.Combine(outputDir, $"frame_{savedFrames:D2}.jpg");
            Cv2.ImWrite(fileName, frame);
            savedFrames++;
        }

        // Clean up (using statements handle release automatically, but explicit is fine too)
        camera.Release();

        Console.WriteLine(
            $"Done! Discarded {skippedFrames} warmup frames and saved {savedFrames} frames to {outputDir}");
    }
}