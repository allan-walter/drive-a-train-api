using System.Diagnostics;
using DriveATrain.OpenCv;
using OpenCvSharp;

namespace DriveATrain.Services;

public class DetectorService(Try4 try4, LimiterService limiter, DccService dccService)
{
    // public static double CAMERA_WIDTH = 640.0;
    // public static double CAMERA_HEIGHT = 360.0;
    public static double CAMERA_WIDTH = 1920.0;
    public static double CAMERA_HEIGHT = 1080.0;
    
    // public static int STREAM_WIDTH = 640;
    // public static int STREAM_HEIGHT = 360;
    public static int STREAM_WIDTH = 1920;
    public static int STREAM_HEIGHT = 1080;

    // public static int STREAM_WIDTH = 1920;
    // public static int STREAM_HEIGHT = 1080;
    public static int STREAM_FPS = 30;

    public List<UnitMarkerResponse> Process(Mat frame)
    {
        DebugWindow.Show("test", frame);
        return new List<UnitMarkerResponse>();
        var markers = try4.GetMarkerSeeds(frame.Clone());
        var combinedMask = Helpers.CombineMasks(markers.Select(m => m.RawMask).ToList());
        var dirMarkers = try4.IdentifyDirectionMarkers(frame.Clone(), markers, combinedMask);
        var units = try4.GetRects(frame.Clone(), markers, dirMarkers);
        var train = units.FirstOrDefault(u => u.Marker.Unit?.Type == UnitType.Locomotive);

        if (train != null)
        {
            var limits = limiter.ProcessLimits(frame, train.Front, train.Back);
            dccService.SetLimits(limits.Forward, limits.Reverse);
        }
        else
        {
            // _dccService.SetLimits(SpeedLimit.Stop, SpeedLimit.Stop);
            dccService.SetLimits(SpeedLimit.NORMAL, SpeedLimit.NORMAL);
        }

        // return units;
        return new List<UnitMarkerResponse>();
    }
}