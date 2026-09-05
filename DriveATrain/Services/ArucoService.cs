using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace DriveATrain.Services;

public class ArucoService : IDisposable
{
    private readonly OpenCvSharp.Aruco.Dictionary _dictionary =
        CvAruco.GetPredefinedDictionary(PredefinedDictionaryType.Dict4X4_50);

    private readonly ArucoDetector _detector;

    public ArucoService()
    {
        // The tags are only ~25px, under the default 0.03 minimum perimeter rate
        var parameters = new DetectorParameters { MinMarkerPerimeterRate = 0.02 };
        _detector = new ArucoDetector(_dictionary, parameters, new RefineParameters());
    }

    public List<(Point2f Position, int Id)> Find(Mat frame)
    {
        _detector.DetectMarkers(frame, out var corners, out var ids, out _);

        var found = new List<(Point2f, int)>(ids.Length);
        for (int i = 0; i < ids.Length; i++)
        {
            var c = corners[i];
            found.Add((new Point2f(c.Average(p => p.X), c.Average(p => p.Y)), ids[i]));
        }

        return found;
    }

    public void Dispose()
    {
        _detector.Dispose();
        _dictionary.Dispose();
    }
}
