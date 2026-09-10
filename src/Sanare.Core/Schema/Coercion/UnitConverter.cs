namespace Sanare.Core.Schema.Coercion;

/// <summary>Converts a small, explicit set of canonical engineering units.</summary>
public static class UnitConverter
{
    public static bool TryConvert(decimal value, string sourceUnit, string targetUnit, out decimal converted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUnit);
        var source = sourceUnit.Trim();
        var target = targetUnit.Trim();
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            converted = value;
            return true;
        }

        if (string.Equals(source, "in", StringComparison.OrdinalIgnoreCase) && string.Equals(target, "mm", StringComparison.OrdinalIgnoreCase))
        {
            converted = value * 25.4m;
            return true;
        }

        if (string.Equals(source, "g", StringComparison.OrdinalIgnoreCase) && string.Equals(target, "kg", StringComparison.OrdinalIgnoreCase))
        {
            converted = value / 1000m;
            return true;
        }

        converted = default;
        return false;
    }

    public static bool TryConvertMilliampHoursToWattHours(decimal milliampHours, decimal volts, out decimal wattHours)
    {
        if (volts <= 0)
        {
            wattHours = default;
            return false;
        }

        wattHours = milliampHours * volts / 1000m;
        return true;
    }
}
