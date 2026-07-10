using OpenCvSharp;

namespace DriveATrain.OpenCv;

public class Blend
{
    /// <summary>
    /// Alpha-blends a BGRA source image onto a BGR (or BGRA) target at a given global opacity.
    /// Both images must be the same size. Modifies target in place.
    /// </summary>
    /// <param name="source">Source overlay image, must be BGRA (4 channels).</param>
    /// <param name="target">Destination image (BGR or BGRA), modified in place.</param>
    /// <param name="opacity">Global opacity multiplier, 0.0 (invisible) to 1.0 (full source alpha).</param>
    /// <summary>
    /// Alpha-blends a BGRA source image onto a BGR (or BGRA) target at a given global opacity.
    /// Both images must be the same size. Modifies target in place.
    /// </summary>
    /// <param name="source">Source overlay image, must be BGRA (4 channels).</param>
    /// <param name="target">Destination image (BGR or BGRA), modified in place.</param>
    /// <param name="opacity">Global opacity multiplier, 0.0 (invisible) to 1.0 (full source alpha).</param>
    public static void BlendOverlay(Mat source, Mat target, double opacity = 0.2)
    {
        if (source.Channels() != 4)
            throw new ArgumentException("source must be BGRA (4 channels)");
        if (target.Size() != source.Size())
            throw new ArgumentException("target and source must be the same size");
        opacity = Math.Clamp(opacity, 0.0, 1.0);

        Mat[] srcChannels = Cv2.Split(source);
        try
        {
            using Mat sourceBgr = new Mat();
            Cv2.Merge(new[] { srcChannels[0], srcChannels[1], srcChannels[2] }, sourceBgr);

            using Mat alphaF = new Mat();
            srcChannels[3].ConvertTo(alphaF, MatType.CV_32FC1, opacity / 255.0);

            using Mat alpha3 = new Mat();
            Cv2.CvtColor(alphaF, alpha3, ColorConversionCodes.GRAY2BGR);

            using Mat invAlpha3 = new Mat(alpha3.Size(), alpha3.Type(), Scalar.All(1.0)) - alpha3;

            // Clone (not alias) when 3-channel, so disposal doesn't touch target
            using Mat targetBgr = target.Channels() == 4
                ? SplitBgr(target)
                : target.Clone();

            using Mat srcF = new Mat();
            sourceBgr.ConvertTo(srcF, MatType.CV_32FC3);

            using Mat dstF = new Mat();
            targetBgr.ConvertTo(dstF, MatType.CV_32FC3);

            using Mat blended = srcF.Mul(alpha3) + dstF.Mul(invAlpha3);

            if (target.Channels() == 4)
            {
                using Mat blended8u = new Mat();
                blended.ConvertTo(blended8u, MatType.CV_8UC3);
                Mat[] dstChannels = Cv2.Split(target);
                Mat[] blendedChannels = Cv2.Split(blended8u);
                Cv2.Merge(new[] { blendedChannels[0], blendedChannels[1], blendedChannels[2], dstChannels[3] }, target);
                foreach (var c in dstChannels) c.Dispose();
                foreach (var c in blendedChannels) c.Dispose();
            }
            else
            {
                blended.ConvertTo(target, target.Type());
            }
        }
        finally
        {
            foreach (var c in srcChannels) c.Dispose();
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