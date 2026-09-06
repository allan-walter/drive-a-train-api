using DriveATrain;
using DriveATrain.OpenCv;
using DriveATrain.Services;
using OpenCvSharp;

namespace ColorTest;

class Program
{
    private static readonly string StaticImages = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DriveATrain", "Static Images");

    static void Main(string[] args)
    {
        string image = args.Length > 0 ? args[0] : Path.Combine(StaticImages, "live4.jpg");


        // A video or a folder of frames runs the batch report instead
        if (Directory.Exists(image) || Path.GetExtension(image) is ".mp4" or ".avi" or ".mkv" or ".mov" or ".webm")
        {
            BatchTest.Run(image, Path.Combine(Path.GetTempPath(), "aruco misses"));
            Console.WriteLine("enter to close");
            Console.ReadLine();
            return;
        }

        using var frame = Cv2.ImRead(image);
        if (frame.Empty())
            throw new Exception($"Couldn't read {image}");

        using var trackMask = UnitFinder.BuildTrackMask(frame.Size(), new Config().Layout);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var hits = UnitFinder.Find(frame, trackMask);
        Console.WriteLine($"{hits.Count} hits in {sw.ElapsedMilliseconds}ms");

        foreach (var h in hits)
            Console.WriteLine($"{h.Colour,-7} {h.Rect.Size.Width:0}x{h.Rect.Size.Height:0} fill={h.Fill:0.00} at ({h.Rect.Center.X:0},{h.Rect.Center.Y:0})");

        using var aruco = new ArucoService([22]);

        foreach (var h in hits)
        {
            // Pad the unit's box so a tag near the edge keeps its quiet zone
            var box = h.Rect.BoundingRect();
            var roi = new Rect(box.X - 30, box.Y - 30, box.Width + 60, box.Height + 60);

            foreach (var (position, id) in aruco.Find(frame, roi))
                Console.WriteLine($"{h.Colour} unit: aruco id={id} at ({position.X:0},{position.Y:0})");
        }

        Console.WriteLine("enter to close");
        Console.ReadLine();
    }

    // The old magenta direction marker experiment
    static void MagentaTest()
    {
        using Mat frame = Cv2.ImRead(Path.Combine(StaticImages, "live2.jpg"));

        using Mat hsv = new Mat();
        Cv2.CvtColor(frame, hsv, ColorConversionCodes.BGR2HSV);

        // Range 1: Strict Magenta/Pink (165 to 178)
        // Raised Saturation floor (100) to reject pale/warm background noise
        using Mat mask1 = new Mat();
        Cv2.InRange(hsv,
            new Scalar(165, 100, 50),
            new Scalar(178, 255, 255),
            mask1);

        // Range 2: Very tight lower wraparound boundary (0 to 3)
        // Only catches pixels that cross 180 -> 0 right at the edge
        using Mat mask2 = new Mat();
        Cv2.InRange(hsv,
            new Scalar(0, 100, 50),
            new Scalar(3, 255, 255),
            mask2);

        using Mat finalMask = new Mat();
        Cv2.BitwiseOr(mask1, mask2, finalMask);

        using Mat cut = new Mat();
        frame.CopyTo(cut, finalMask);

        DebugWindow.ShowAlways("in range", finalMask);
        DebugWindow.ShowAlways("cut", cut);

        Console.ReadLine();
    }
}
