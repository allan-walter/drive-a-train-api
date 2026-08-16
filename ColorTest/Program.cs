using DriveATrain.OpenCv;
using DriveATrain.Services;
using OpenCvSharp;

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


        // A bit of blur so there is more of an average color to find
        int blurSize = (int)ResolutionScaler.ScaleKernel(21);
        using var blurredFrame = new Mat();
        Cv2.GaussianBlur(frame, blurredFrame, new Size(blurSize, blurSize), 0);

        Mat whiteImage = new Mat(CaptureService.DETECTION_HEIGHT, CaptureService.DETECTION_WIDTH, MatType.CV_8UC1,
            new Scalar(255));
        var items = DetectorService.SplitMaskByNearestColorRegion(frame, whiteImage,
            UnitColor.Colors);

        Console.ReadKey();
    }
}