using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace DriveATrain.Services;

/// <summary>
/// Reads the 4x4 aruco tags on the units. The tags are only ~13px across in frame (about 2px per bit),
/// so this leans on three things, all measured on real footage:
///   - work on a crop around the unit, upscaled 2x, rather than the whole frame
///   - a dictionary holding only the ids we've printed, so bit errors get corrected against 4 codes
///     instead of confused between 50
///   - a second, more lenient thresholding pass only when the first finds nothing
/// </summary>
public class ArucoService : IDisposable
{
    private const int Scale = 2;

    private readonly int[] _ids;
    private readonly OpenCvSharp.Aruco.Dictionary _dictionary;
    private readonly ArucoDetector _strict;
    private readonly ArucoDetector _lenient;

    /// <param name="ids">The 4x4_50 ids actually printed on units. Fewer is better.</param>
    public ArucoService(IEnumerable<int> ids)
    {
        _ids = ids.ToArray();
        _dictionary = BuildDictionary(_ids);
        _strict = new ArucoDetector(_dictionary, Parameters(threshConstant: 7), new RefineParameters());
        _lenient = new ArucoDetector(_dictionary, Parameters(threshConstant: 4), new RefineParameters());
    }

    private static DetectorParameters Parameters(double threshConstant) => new()
    {
        // Fine steps through the threshold window sizes, or a dim tag gets lost against the loco
        AdaptiveThreshWinSizeMin = 3,
        AdaptiveThreshWinSizeMax = 35,
        AdaptiveThreshWinSizeStep = 2,
        AdaptiveThreshConstant = threshConstant,
        // Use the dictionary's full correction budget (2 bits with our sparse dictionary)
        ErrorCorrectionRate = 1.0,
    };

    // 4x4_50 with only our rows kept. Its codes are >= 8 bits apart so 2-bit correction is safe;
    // the stock dictionary only allows 1 because some of its 50 codes are 4 apart.
    // OpenCvSharp exposes no constructor for a custom dictionary, so it goes via a file.
    private static OpenCvSharp.Aruco.Dictionary BuildDictionary(int[] ids)
    {
        using var source = CvAruco.GetPredefinedDictionary(PredefinedDictionaryType.Dict4X4_50);

        var lines = new List<string>
        {
            "%YAML:1.0", "---",
            $"nmarkers: {ids.Length}", "markersize: 4", "maxCorrectionBits: 2",
        };
        for (int i = 0; i < ids.Length; i++)
        {
            using var row = source.BytesList.Row(ids[i]);
            using var bits = OpenCvSharp.Aruco.Dictionary.GetBitsFromByteList(row, 4);
            var code = new System.Text.StringBuilder(16);
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    code.Append(bits.At<byte>(r, c) == 0 ? '0' : '1');
            lines.Add($"marker_{i}: \"{code}\"");
        }

        var path = Path.Combine(Path.GetTempPath(), $"aruco-{Guid.NewGuid():N}.yml");
        try
        {
            File.WriteAllLines(path, lines);
            return CvAruco.ReadDictionary(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Finds tags, optionally only within a region. Pass the unit's bounding box (padded a little)
    /// when you have it - a tag this small is unreliable when searched for across a whole frame.
    /// Positions come back in full-frame coordinates.
    /// </summary>
    public List<(Point2f Position, int Id)> Find(Mat frame, Rect? region = null)
    {
        var roi = (region ?? new Rect(0, 0, frame.Width, frame.Height))
            .Intersect(new Rect(0, 0, frame.Width, frame.Height));

        if (roi.Width <= 0 || roi.Height <= 0)
            return [];

        using var crop = new Mat(frame, roi);
        using var scaled = new Mat();
        Cv2.Resize(crop, scaled, new Size(roi.Width * Scale, roi.Height * Scale), 0, 0, InterpolationFlags.Cubic);

        _strict.DetectMarkers(scaled, out var corners, out var indices, out _);
        if (indices.Length == 0)
            _lenient.DetectMarkers(scaled, out corners, out indices, out _);

        var found = new List<(Point2f, int)>(indices.Length);
        for (int i = 0; i < indices.Length; i++)
        {
            var c = corners[i];
            found.Add((new Point2f(
                c.Average(p => p.X) / Scale + roi.X,
                c.Average(p => p.Y) / Scale + roi.Y), _ids[indices[i]]));
        }

        return found;
    }

    public void Dispose()
    {
        _strict.Dispose();
        _lenient.Dispose();
        _dictionary.Dispose();
    }
}
