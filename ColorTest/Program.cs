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
            "Static Images/live.jpg"));

        Cv2.Resize(frame, frame,
            new Size(CaptureService.DETECTION_WIDTH, CaptureService.DETECTION_HEIGHT));

        using Mat goZone = Cv2.ImRead(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "DriveATrain",
            "Static Images/go zone.jpg"), ImreadModes.Grayscale);

        Cv2.Resize(goZone, goZone,
            new Size(CaptureService.DETECTION_WIDTH, CaptureService.DETECTION_HEIGHT));

        using var cut = new Mat();
        frame.CopyTo(cut, goZone);

        int size = 31;
        var Blur = new Size(size, size);
        Cv2.GaussianBlur(cut, cut, Blur, 0);

        Mat guided = new Mat();
        CvXImgProc.GuidedFilter(cut, cut, guided, radius: 8, eps: 100);

        DebugWindow.ShowAlways("frame", cut);

        Console.ReadKey();
    }
}