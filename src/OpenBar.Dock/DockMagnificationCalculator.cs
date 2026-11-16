namespace OpenBar.Dock;

/// <summary>
/// Progressive dock magnification. Item centers are expressed along the dock
/// axis (X for horizontal docks, Y for vertical docks); neighbors react to the
/// real cursor position without moving the dock itself.
/// </summary>
public static class DockMagnificationCalculator
{
    public static double[] Calculate(
        IReadOnlyList<double> itemCenters,
        double cursorPosition,
        double iconSize,
        double spacing,
        double maximumMagnification)
    {
        ArgumentNullException.ThrowIfNull(itemCenters);

        var result = new double[itemCenters.Count];
        Array.Fill(result, 1.0);

        if (maximumMagnification <= 1 || itemCenters.Count == 0)
        {
            return result;
        }

        var radius = Math.Max(iconSize * 1.8, iconSize + spacing * 2);
        if (radius <= 0)
        {
            return result;
        }

        var extra = maximumMagnification - 1;

        for (var index = 0; index < itemCenters.Count; index++)
        {
            var distance = Math.Abs(itemCenters[index] - cursorPosition);
            if (distance >= radius)
            {
                continue;
            }

            var linear = 1 - distance / radius;
            var smooth = linear * linear * (3 - 2 * linear);
            result[index] = 1 + extra * smooth;
        }

        return result;
    }
}
