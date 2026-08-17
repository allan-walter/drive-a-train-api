using OpenCvSharp;

namespace DriveATrain.OpenCv;

public static class InRange
{
    public static void InRangeHue(Mat hsv, ColorRange range, Mat dst)
    {
        double h = range.Color.Val0;
        double lowH = h - range.HTol;
        double highH = h + range.HTol;

        double sLow = Math.Max(0, range.Color.Val1 - range.STol);
        double sHigh = Math.Min(255, range.Color.Val1 + range.STol);
        double vLow = Math.Max(0, range.Color.Val2 - range.VTol);
        double vHigh = Math.Min(255, range.Color.Val2 + range.VTol);

        if (lowH >= 0 && highH <= 179)
        {
            // no wraparound, behaves exactly as before
            Cv2.InRange(hsv, range.Lower, range.Upper, dst);
            return;
        }

        using var mask1 = new Mat();
        using var mask2 = new Mat();

        if (lowH < 0)
        {
            Cv2.InRange(hsv,
                new Scalar(0, sLow, vLow),
                new Scalar(highH, sHigh, vHigh),
                mask1);

            Cv2.InRange(hsv,
                new Scalar(180 + lowH, sLow, vLow),
                new Scalar(179, sHigh, vHigh),
                mask2);
        }
        else // highH > 179
        {
            Cv2.InRange(hsv,
                new Scalar(lowH, sLow, vLow),
                new Scalar(179, sHigh, vHigh),
                mask1);

            Cv2.InRange(hsv,
                new Scalar(0, sLow, vLow),
                new Scalar(highH - 180, sHigh, vHigh),
                mask2);
        }

        Cv2.BitwiseOr(mask1, mask2, dst);
        mask1.Dispose();
        mask2.Dispose();
    }
}