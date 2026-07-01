using System.Collections.Concurrent;
using OpenCvSharp;

namespace DriveATrain.Services;

public static class DebugWindow
{
    private static readonly BlockingCollection<(string Name, Mat Frame)> _queue = new();
    private static Thread _uiThread;
    private static readonly ConcurrentDictionary<string, Mat> _latest = new();

    public static void Start()
    {
        _uiThread = new Thread(RunLoop) { IsBackground = true };
        _uiThread.Start();
    }

    public static void Show(string title, Mat mat)
    {
        // Clone because caller may dispose/reuse the Mat
        _queue.Add((title, mat.Clone()));
    }

    private static void RunLoop()
    {
        while (true)
        {
            // Drain whatever's queued, keep only latest per window name
            while (_queue.TryTake(out var item, 10))
            {
                if (_latest.TryGetValue(item.Name, out var old)) old.Dispose();
                _latest[item.Name] = item.Frame;
            }

            foreach (var kv in _latest)
                Cv2.ImShow(kv.Key, kv.Value);

            Cv2.WaitKey(1); // required to pump window messages / repaint
        }
    }
}