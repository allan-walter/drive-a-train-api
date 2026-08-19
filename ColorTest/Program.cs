using DriveATrain.OpenCv;
using DriveATrain.Services;
using OpenCvSharp;
using OpenCvSharp.XImgProc;

namespace ColorTest;

class Program
{
    static void Main(string[] args)
    {
        using Mat frame = Cv2.ImRead(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "DriveATrain",
            "Static Images/live2.jpg"));


        using Mat hsv = new Mat();
        Cv2.CvtColor(frame, hsv, ColorConversionCodes.BGR2HSV);

// Range 1: Strict Magenta/Pink (165 to 178)
// Raised Saturation floor (100) to reject pale/warm background noise
        using Mat mask1 = new Mat();
        Cv2.InRange(hsv,
            new Scalar(165, 100, 50), // Lower H, S, V
            new Scalar(178, 255, 255), // Upper H, S, V
            mask1);

// Range 2: Very tight lower wraparound boundary (0 to 3)
// Only catches pixels that cross 180 -> 0 right at the edge
        using Mat mask2 = new Mat();
        Cv2.InRange(hsv,
            new Scalar(0, 100, 50),
            new Scalar(3, 255, 255),
            mask2);

// Combine masks
        using Mat finalMask = new Mat();
        Cv2.BitwiseOr(mask1, mask2, finalMask);
        
        using Mat cut = new Mat();
        frame.CopyTo(cut, finalMask);

        DebugWindow.ShowAlways("in range", finalMask);
        DebugWindow.ShowAlways("cut", cut);

        // Cv2.Resize(frame, frame,
        //     new Size(CaptureService.DETECTION_WIDTH, CaptureService.DETECTION_HEIGHT));

        // using Mat goZone = Cv2.ImRead(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        //     "DriveATrain",
        //     "Static Images/go zone.jpg"), ImreadModes.Grayscale);
        //
        // Cv2.Resize(goZone, goZone,
        //     new Size(CaptureService.DETECTION_WIDTH, CaptureService.DETECTION_HEIGHT));

        // using var cut = new Mat();
        // frame.CopyTo(cut, goZone);

        // int size = 31;
        // var Blur = new Size(size, size);
        // Cv2.GaussianBlur(cut, cut, Blur, 0);
        //
        // Mat guided = new Mat();
        // CvXImgProc.GuidedFilter(cut, cut, guided, radius: 8, eps: 100);
        //
        // DebugWindow.ShowAlways("frame", cut);

        Console.ReadKey();
    }
}