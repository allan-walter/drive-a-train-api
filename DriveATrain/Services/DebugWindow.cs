using System.Collections.Concurrent;
using OpenCvSharp;

namespace DriveATrain.Services;

public static class DebugWindow
{
    private static readonly BlockingCollection<(string Name, Mat Frame)> _queue = new();
    private static Thread? _uiThread;
    private static readonly ConcurrentDictionary<string, Mat> _latest = new();

    private static void Start()
    {
        _uiThread = new Thread(RunLoop) { IsBackground = true };
        _uiThread.Start();
    }

    private static List<string> debugCategories =
    [
        // "colorSplit",
        // "dirMarkers"
        // "noiseRemoval"
    ];

    public static void ShowAlways(string title, Mat mat)
    {
        if (_uiThread == null)
            Start();

        // Clone because caller may dispose/reuse the Mat
        _queue.Add(($"{title}", mat.Clone()));
    }

    // do NOT call clone, this method internally clones again and if you do that it'll never get disposed of
    public static void Show(string category, string title, Mat mat)
    {
        if (category != "always" && !debugCategories.Contains(category))
            return;

        if (_uiThread == null)
            Start();

        // Clone because caller may dispose/reuse the Mat
        _queue.Add(($"{category}_{title}", mat.Clone()));
    }

    public static void ShowMaskOnFrame(string category, string title, Mat mask, Mat frame)
    {
        if (category != "always" && !debugCategories.Contains(category))
            return;

        if (_uiThread == null)
            Start();

        var cut = new Mat();
        frame.CopyTo(cut, mask);

        // Clone because caller may dispose/reuse the Mat
        _queue.Add(($"{category}_mask_{title}", cut));
    }

    private static void RunLoop()
    {
        while (true)
        {
            while (_queue.TryTake(out var item, 10))
            {
                if (_latest.TryGetValue(item.Name, out var old)) old.Dispose();
                _latest[item.Name] = item.Frame;
            }

            foreach (var kv in _latest)
                Cv2.ImShow(kv.Key, kv.Value);
            if (_latest.Count > 0)
                Cv2.WaitKey(1); // just pump messages, don't touch the Mats
        }
    }
}