using System.Diagnostics;

namespace DriveATrain.OpenCv;

public class DetectionTimingWindow
{
    private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private readonly Stopwatch _windowStopwatch = Stopwatch.StartNew();
    private readonly Dictionary<string, StageTiming> _stages = new();
    private readonly List<string> _stageOrder = [];
    private int _frameCount;
    private TimeSpan _frameElapsed = TimeSpan.Zero;

    public void RecordStage(string stageName, TimeSpan elapsed)
    {
        lock (_sync)
        {
            if (!_stages.TryGetValue(stageName, out var stage))
            {
                stage = new StageTiming();
                _stages[stageName] = stage;
                _stageOrder.Add(stageName);
            }

            stage.CallCount++;
            stage.TotalElapsed += elapsed;
        }
    }

    public void RecordFrame(TimeSpan elapsed)
    {
        string? message = null;

        lock (_sync)
        {
            _frameCount++;
            _frameElapsed += elapsed;

            if (_windowStopwatch.Elapsed < LogInterval || _frameCount == 0)
                return;

            double processedFps = _frameCount / _windowStopwatch.Elapsed.TotalSeconds;
            double averageFrameMs = _frameElapsed.TotalMilliseconds / _frameCount;
            var stageSummary = BuildStageSummary();

            message = $"[detect] total: {processedFps:F2} fps ({averageFrameMs:F2} ms/frame)";
            if (stageSummary.Count > 0)
                message += Environment.NewLine + string.Join(Environment.NewLine, stageSummary);

            _frameCount = 0;
            _frameElapsed = TimeSpan.Zero;
            _windowStopwatch.Restart();

            foreach (var stage in _stages.Values)
                stage.Reset();
        }

        // Debug.WriteLine(message);
        // Console.WriteLine(message);
    }

    private List<string> BuildStageSummary()
    {
        var lines = new List<string>();

        foreach (var stageName in _stageOrder.Where(IsRootStage))
            AppendStageGroup(lines, stageName);

        return lines;
    }

    private void AppendStageGroup(List<string> lines, string stageName)
    {
        if (!_stages.TryGetValue(stageName, out var stage) || stage.CallCount == 0)
            return;

        lines.Add(FormatStageHeader(GetDisplayName(stageName), stage));

        var childLeaves = new List<string>();

        foreach (var childName in GetDirectChildren(stageName))
        {
            if (!_stages.TryGetValue(childName, out var childStage) || childStage.CallCount == 0)
                continue;

            var grandChildren = GetDirectChildren(childName)
                .Where(grandChildName => _stages.TryGetValue(grandChildName, out var grandChildStage) &&
                                         grandChildStage.CallCount > 0)
                .ToList();

            if (grandChildren.Count == 0)
            {
                childLeaves.Add(FormatInlineStage(GetDisplayName(childName), childStage));
                continue;
            }

            var grandChildSummary = string.Join(" | ", grandChildren.Select(grandChildName =>
                FormatInlineStage(GetDisplayName(grandChildName), _stages[grandChildName])));
            lines.Add($"    {FormatInlineStage(GetDisplayName(childName), childStage)} -> {grandChildSummary}");
        }

        if (childLeaves.Count > 0)
            lines.Add($"    {string.Join(" | ", childLeaves)}");
    }

    private IEnumerable<string> GetDirectChildren(string parentStageName) =>
        _stageOrder.Where(stageName => GetParentStageName(stageName) == parentStageName);

    private static bool IsRootStage(string stageName) => !stageName.Contains('.');

    private static string? GetParentStageName(string stageName)
    {
        int splitIndex = stageName.LastIndexOf('.');
        return splitIndex >= 0 ? stageName[..splitIndex] : null;
    }

    private static string GetDisplayName(string stageName)
    {
        int splitIndex = stageName.LastIndexOf('.');
        return splitIndex >= 0 ? stageName[(splitIndex + 1)..] : stageName;
    }

    private static string FormatStageHeader(string displayName, StageTiming stage)
    {
        double averageMs = stage.TotalElapsed.TotalMilliseconds / stage.CallCount;
        double fps = stage.TotalElapsed.TotalSeconds > 0
            ? stage.CallCount / stage.TotalElapsed.TotalSeconds
            : 0;

        return $"  {displayName,-20} {averageMs,8:F2} ms avg  {fps,8:F2} fps";
    }

    private static string FormatInlineStage(string displayName, StageTiming stage)
    {
        double averageMs = stage.TotalElapsed.TotalMilliseconds / stage.CallCount;
        return $"{displayName} {averageMs:F2} ms";
    }

    private sealed class StageTiming
    {
        public int CallCount { get; set; }
        public TimeSpan TotalElapsed { get; set; }

        public void Reset()
        {
            CallCount = 0;
            TotalElapsed = TimeSpan.Zero;
        }
    }
}
