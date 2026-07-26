using DriveATrain.Services;

namespace DriveATrain.OpenCv;

public static class ResolutionScaler
{
    /// <summary>
    /// Scales a value from the original resolution to match the new resolution.
    /// </summary>
    /// <param name="value">The original value (e.g. a coordinate, size, or threshold).</param>
    /// <param name="originalWidth">Width the value was originally calibrated at.</param>
    /// <param name="originalHeight">Height the value was originally calibrated at.</param>
    /// <param name="newWidth">Width of the new (lowered) resolution.</param>
    /// <param name="newHeight">Height of the new (lowered) resolution.</param>
    /// <returns>The scaled value.</returns>
    private static float ScaleValue(float value, int originalWidth, int originalHeight, int newWidth, int newHeight)
    {
        // Uses average of x/y scale to handle non-uniform resizes gracefully.
        // If your resolution scales uniformly (e.g. exactly half in both dimensions),
        // widthScale and heightScale will be identical anyway.
        float widthScale = (float)newWidth / originalWidth;
        float heightScale = (float)newHeight / originalHeight;
        float scale = (widthScale + heightScale) / 2f;

        return value * scale;
    }

    // Kernal values need to be odd
    // TODO delete this can just get the values right, with as lower resolution as possible. This doesnt really scale well enough, its not really linier
    public static int ScaleKernel(float value)
    {
        var scaled = (int)ScaleValue(value);
        if (scaled % 2 == 0)
            scaled++;

        return scaled;
    }

    public static float ScaleValue(float value)
    {
        return ScaleValue(value, 1920, 1080, CaptureService.detectionWidth, CaptureService.detectionHeight);
    }
    
    private static float ScaleArea(float value, int originalWidth, int originalHeight, int newWidth, int newHeight)
    {
        // Same linear scale as ScaleValue, but squared since this scales an area
        // (e.g. a contour area in px^2) rather than a length.
        float widthScale = (float)newWidth / originalWidth;
        float heightScale = (float)newHeight / originalHeight;
        float scale = (widthScale + heightScale) / 2f;
        return value * scale * scale;
    }

    public static float ScaleArea(float value)
    {
        return ScaleArea(value, 1920, 1080, CaptureService.detectionWidth, CaptureService.detectionHeight);
    }
}