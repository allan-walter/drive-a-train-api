using OpenCvSharp;

namespace DriveATrain.OpenCv;

public class Blend
{
    public class PreparedOverlay : IDisposable
    {
        public required Mat SourceBgr; // 8-bit, full frame size
        public required Mat AlphaF; // float32, single channel
        public required Mat InvAlphaF; // float32, single channel

        public void Dispose()
        {
            SourceBgr.Dispose();
            AlphaF.Dispose();
            InvAlphaF.Dispose();
        }
    }

    public static PreparedOverlay? Prepare(Mat source, double opacity)
    {
        if (source.Channels() != 4)
            throw new ArgumentException("source must be BGRA (4 channels)");
        opacity = Math.Clamp(opacity, 0.0, 1.0);

        Mat[] srcChannels = Cv2.Split(source);
        try
        {
            if (Cv2.CountNonZero(srcChannels[3]) == 0)
                return null; // fully transparent, nothing to ever blend

            var sourceBgr = new Mat();
            Cv2.Merge(new[] { srcChannels[0], srcChannels[1], srcChannels[2] }, sourceBgr);

            var alphaF = new Mat();
            srcChannels[3].ConvertTo(alphaF, MatType.CV_32FC1, opacity / 255.0);

            var invAlphaF = new Mat();
            Cv2.Subtract(Scalar.All(1.0), alphaF, invAlphaF);

            return new PreparedOverlay { SourceBgr = sourceBgr, AlphaF = alphaF, InvAlphaF = invAlphaF };
        }
        finally
        {
            foreach (var c in srcChannels) c.Dispose();
        }
    }
    
    public static void BlendOverlay(Mat source, Mat target, double opacity)
    {
        if (target.Size() != source.Size())
            throw new ArgumentException("target and source must be the same size");

        using var prepared = Prepare(source, opacity);
        if (prepared == null) return;
        BlendPrepared(prepared, target);
    }

    public static void BlendPrepared(PreparedOverlay overlay, Mat target)
    {
        bool targetHasAlpha = target.Channels() == 4;
        using Mat targetBgr = targetHasAlpha ? SplitBgr(target) : target.Clone();

        using var blended = new Mat();
        Cv2.BlendLinear(overlay.SourceBgr, targetBgr, overlay.AlphaF, overlay.InvAlphaF, blended);

        if (!targetHasAlpha)
        {
            blended.CopyTo(target);
            return;
        }

        Mat[] dstChannels = Cv2.Split(target);
        Mat[] blendedChannels = Cv2.Split(blended);
        try
        {
            using var dstAlphaF = new Mat();
            dstChannels[3].ConvertTo(dstAlphaF, MatType.CV_32FC1, 1.0 / 255.0);

            using var keptDstAlpha = new Mat();
            Cv2.Multiply(dstAlphaF, overlay.InvAlphaF, keptDstAlpha);

            using var outAlphaF = new Mat();
            Cv2.Add(overlay.AlphaF, keptDstAlpha, outAlphaF);

            using var outAlpha = new Mat();
            outAlphaF.ConvertTo(outAlpha, MatType.CV_8UC1, 255.0);

            Cv2.Merge(new[] { blendedChannels[0], blendedChannels[1], blendedChannels[2], outAlpha }, target);
        }
        finally
        {
            foreach (var c in dstChannels) c.Dispose();
            foreach (var c in blendedChannels) c.Dispose();
        }
    }

    static Mat SplitBgr(Mat bgra)
    {
        Mat[] c = Cv2.Split(bgra);
        Mat bgr = new Mat();
        Cv2.Merge(new[] { c[0], c[1], c[2] }, bgr);
        foreach (var ch in c) ch.Dispose();
        return bgr;
    }
}