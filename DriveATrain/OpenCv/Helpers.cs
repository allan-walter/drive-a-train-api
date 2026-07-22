using DriveATrain.Services;
using OpenCvSharp;

public static class Helpers
{

    // Change white to transparent, and only return black
    public static Mat InverseMaskOverlay(Mat mat)
    {
        if (mat.Type() != MatType.CV_8UC1)
            throw new ArgumentException("Mask must be a single-channel 8-bit (CV_8UC1) binary mask.");

        using var alpha = new Mat();
        Cv2.BitwiseNot(mat, alpha);

        var result = new Mat(mat.Size(), MatType.CV_8UC4);
        using var black = Mat.Zeros(mat.Size(), MatType.CV_8UC1).ToMat();

        Cv2.Merge([black, black, black, alpha], result);

        return result;
    }
    
    public static Mat MaskToTransparentOverlay(Mat mask)
    {
        if (mask.Type() != MatType.CV_8UC1)
            throw new ArgumentException("Mask must be a single-channel 8-bit (CV_8UC1) binary mask.");

        // Result: white where mask is white, transparent where mask is black
        Mat result = new Mat(mask.Size(), MatType.CV_8UC4);

        Mat[] channels = new Mat[]
        {
            mask, // B - white where mask is white
            mask, // G
            mask, // R
            mask  // A - alpha follows the mask too (0 = transparent, 255 = opaque)
        };

        Cv2.Merge(channels, result);

        return result;
    }
    
    public static Mat CombineMasks(List<Mat> masks)
    {
        var type = MatType.CV_8UC1;
        if (masks.Count == 0)
        {
            // Default to 8-bit single channel (typical for masks) TODO
            return Mat.Zeros(new Size(CaptureService.width, CaptureService.height), type).ToMat();
        }

        var result = Mat.Zeros(masks[0].Size(), masks[0].Type()).ToMat();
        foreach (var mask in masks)
        {
            if (mask.Size() != result.Size())
                throw new ArgumentException("All masks must have the same size");
            if (mask.Type() != type)
                throw new ArgumentException("Not binary type");

            Cv2.BitwiseOr(result, mask, result);
        }

        return result;
    }

    private static Mat InvertBinary(Mat src)
    {
        var dst = new Mat();
        Cv2.BitwiseNot(src, dst);
        return dst;
    }

    private static double ColorDistance(Vec3b pixel, Scalar color)
    {
        double db = pixel.Item0 - color.Val0;
        double dg = pixel.Item1 - color.Val1;
        double dr = pixel.Item2 - color.Val2;
        return Math.Sqrt(db * db + dg * dg + dr * dr);
    }

    public static List<Mat> SplitMaskByNearestColor(Mat frame, Mat mask, List<Scalar> colors)
    {
        const double threshold = 60.0;

        var seeds = Enumerable.Range(0, colors.Count)
            .Select(_ => Mat.Zeros(frame.Size(), MatType.CV_8U).ToMat())
            .ToList();

        for (int row = 0; row < frame.Rows; row++)
        {
            for (int col = 0; col < frame.Cols; col++)
            {
                if (mask.At<byte>(row, col) == 0) continue;

                var pixel = frame.At<Vec3b>(row, col);

                int closestIndex = -1;
                double minDist = double.MaxValue;
                for (int i = 0; i < colors.Count; i++)
                {
                    double dist = ColorDistance(pixel, colors[i]);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestIndex = i;
                    }
                }

                if (closestIndex == -1) continue;

                if (minDist < threshold)
                {
                    seeds[closestIndex].Set(row, col, (byte)255);
                }
            }
        }

        var dists = seeds.Select(seed =>
        {
            var dist = new Mat();
            using var inverted = InvertBinary(seed);
            Cv2.DistanceTransform(inverted, dist, DistanceTypes.L2, DistanceTransformMasks.Precise);
            return dist;
        }).ToList();

        var masks = Enumerable.Range(0, colors.Count)
            .Select(_ => Mat.Zeros(frame.Size(), MatType.CV_8UC1).ToMat())
            .ToList();

        for (int row = 0; row < frame.Rows; row++)
        {
            for (int col = 0; col < frame.Cols; col++)
            {
                if (mask.At<byte>(row, col) == 0) continue;

                int closestIndex = -1;
                double minD = double.MaxValue;
                for (int i = 0; i < dists.Count; i++)
                {
                    double d = dists[i].At<float>(row, col);
                    if (d < minD)
                    {
                        minD = d;
                        closestIndex = i;
                    }
                }

                if (closestIndex == -1) continue;

                masks[closestIndex].Set(row, col, (byte)255);
            }
        }

        return masks;
    }

    public static Point? GetCenterOfShape(Mat mask)
    {
        Cv2.FindContours(mask, out Point[][] contours, out HierarchyIndex[] hierarchy,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0) return null;

        // Get the largest contour (in case there are small noise contours)
        Point[] largestContour = null;
        double largestArea = double.MinValue;
        foreach (var c in contours)
        {
            double area = Cv2.ContourArea(c);
            if (area > largestArea)
            {
                largestArea = area;
                largestContour = c;
            }
        }

        if (largestContour == null) return null;

        var moments = Cv2.Moments(largestContour);
        if (moments.M00 == 0.0) return null;

        double cx = moments.M10 / moments.M00;
        double cy = moments.M01 / moments.M00;
        return new Point(cx, cy);
    }

    public static Mat ConvexHullMask(Mat mask)
    {
        Cv2.FindContours(mask, out Point[][] contours, out HierarchyIndex[] hierarchy,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var output = Mat.Zeros(mask.Size(), MatType.CV_8UC1).ToMat();

        foreach (var contour in contours)
        {
            var hull = Cv2.ConvexHull(contour);
            Cv2.FillConvexPoly(output, hull, new Scalar(255.0));
        }

        return output;
    }
}