using DriveATrain;
using DriveATrain.OpenCv;
using DriveATrain.Services;
using OpenCvSharp;

namespace ColorTest;

/// <summary>
/// Runs ArucoService over a video or a folder of frames and reports the hit rate.
/// Frames where a unit was found but no tag read are written out so they can be looked at.
/// </summary>
static class BatchTest
{
    public static void Run(string path, string missDump)
    {
        using var aruco = new ArucoService([22]);
        Directory.CreateDirectory(missDump);

        Mat? trackMask = null;
        Size maskSize = default;
        int total = 0, withUnit = 0, hit = 0;
        var idsSeen = new Dictionary<int, int>();
        var misses = new List<int>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        foreach (var (frame, index) in EnumerateFrames(path))
        {
            using (frame)
            {
                total++;
                if (trackMask == null || maskSize != frame.Size())
                {
                    trackMask?.Dispose();
                    maskSize = frame.Size();
                    trackMask = UnitFinder.BuildTrackMask(maskSize, new Config().Layout);
                }

                var units = UnitFinder.Find(frame, trackMask);
                if (units.Count == 0) continue;
                withUnit++;

                bool any = false;
                foreach (var unit in units)
                {
                    var box = unit.Rect.BoundingRect();
                    var roi = new Rect(box.X - 30, box.Y - 30, box.Width + 60, box.Height + 60);
                    foreach (var (_, id) in aruco.Find(frame, roi))
                    {
                        any = true;
                        idsSeen[id] = idsSeen.GetValueOrDefault(id) + 1;
                    }
                }

                if (any) hit++;
                else
                {
                    misses.Add(index);
                    Cv2.ImWrite(Path.Combine(missDump, $"miss_{index:00000}.jpg"), frame);
                }
            }
        }

        Console.WriteLine($"\n{total} frames in {sw.ElapsedMilliseconds}ms, {withUnit} with a unit found");
        Console.WriteLine($"tag read in {hit}/{withUnit} = {100.0 * hit / Math.Max(1, withUnit):0.0}%");
        Console.WriteLine($"ids seen: {string.Join(", ", idsSeen.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} x{kv.Value}"))}");
        if (misses.Count > 0)
            Console.WriteLine($"missed frames: {string.Join(" ", misses)}  (written to {missDump})");

        trackMask?.Dispose();
    }

    static IEnumerable<(Mat Frame, int Index)> EnumerateFrames(string path)
    {
        if (Directory.Exists(path))
        {
            var files = Directory.GetFiles(path, "*.jpg").Concat(Directory.GetFiles(path, "*.png")).OrderBy(f => f).ToArray();
            for (int i = 0; i < files.Length; i++)
            {
                var m = Cv2.ImRead(files[i]);
                if (!m.Empty()) yield return (m, i);
            }
            yield break;
        }

        using var capture = new VideoCapture(path);
        if (!capture.IsOpened())
            throw new Exception($"Couldn't open {path}");

        for (int i = 0; ; i++)
        {
            var m = new Mat();
            if (!capture.Read(m) || m.Empty()) { m.Dispose(); break; }
            yield return (m, i);
        }
    }
}
