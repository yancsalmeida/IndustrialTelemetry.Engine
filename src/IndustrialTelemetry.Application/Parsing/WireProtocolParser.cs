using System.Globalization;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;

namespace IndustrialTelemetry.Application.Parsing;

/// <summary>
/// High-throughput zero-allocation wire-protocol line enumerator over ReadOnlySpan&lt;char&gt;.
/// Avoids string.Split('\n') and temporary string[] allocations on the managed GC heap.
/// </summary>
public ref struct WireSpanLineEnumerator
{
    private ReadOnlySpan<char> _remaining;
    private ReadOnlySpan<char> _current;
    private bool _isActive;

    public WireSpanLineEnumerator(ReadOnlySpan<char> buffer)
    {
        _remaining = buffer;
        _current = default;
        _isActive = !buffer.IsEmpty;
    }

    public readonly ReadOnlySpan<char> Current => _current;

    public readonly WireSpanLineEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        while (_isActive)
        {
            if (_remaining.IsEmpty)
            {
                _isActive = false;
                return false;
            }

            var newlineIdx = _remaining.IndexOf('\n');
            ReadOnlySpan<char> line;

            if (newlineIdx >= 0)
            {
                line = _remaining[..newlineIdx];
                _remaining = _remaining[(newlineIdx + 1)..];
            }
            else
            {
                line = _remaining;
                _remaining = default;
                _isActive = false;
            }

            if (!line.IsEmpty && line[^1] == '\r')
            {
                line = line[..^1];
            }

            line = line.Trim();
            if (!line.IsEmpty)
            {
                _current = line;
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// High-performance zero-allocation wire-protocol parser for industrial SCADA/PLC telemetry streams.
/// Parses delimiter-separated records (default: '|') without intermediate string arrays:
/// Format: "TIMESTAMP|SENSOR_TAG|VALUE|UNIT|QUALITY"
/// </summary>
public static class WireProtocolParser
{
    public static int ParseLines(
        ReadOnlySpan<char> rawPayload,
        Guid batchId,
        List<TelemetryMeasurement> destination,
        out int totalLines,
        out int skippedLines,
        char delimiter = '|')
    {
        totalLines = 0;
        skippedLines = 0;
        int parsedCount = 0;

        foreach (var line in new WireSpanLineEnumerator(rawPayload))
        {
            totalLines++;
            if (TryParseRecord(line, batchId, out var measurement, delimiter))
            {
                destination.Add(measurement);
                parsedCount++;
            }
            else
            {
                skippedLines++;
            }
        }

        return parsedCount;
    }

    public static bool TryParseRecord(
        ReadOnlySpan<char> line,
        Guid batchId,
        out TelemetryMeasurement measurement,
        char delimiter = '|')
    {
        measurement = null!;
        var span = line.Trim();
        if (span.IsEmpty)
            return false;

        // 1. TIMESTAMP
        var sep1 = span.IndexOf(delimiter);
        if (sep1 <= 0)
            return false;

        var tsSpan = span[..sep1].Trim();
        if (!TryParseTimestamp(tsSpan, out var timestampUtc))
            return false;

        var rest = span[(sep1 + 1)..];

        // 2. SENSOR_TAG
        var sep2 = rest.IndexOf(delimiter);
        ReadOnlySpan<char> tagSpan;
        if (sep2 > 0)
        {
            tagSpan = rest[..sep2].Trim();
            rest = rest[(sep2 + 1)..];
        }
        else
        {
            return false; // Value is required
        }

        if (tagSpan.IsEmpty)
            return false;

        // 3. VALUE
        var sep3 = rest.IndexOf(delimiter);
        ReadOnlySpan<char> valSpan;
        ReadOnlySpan<char> unitSpan = default;
        ReadOnlySpan<char> qualitySpan = default;

        if (sep3 > 0)
        {
            valSpan = rest[..sep3].Trim();
            rest = rest[(sep3 + 1)..];

            // 4. UNIT
            var sep4 = rest.IndexOf(delimiter);
            if (sep4 > 0)
            {
                unitSpan = rest[..sep4].Trim();
                qualitySpan = rest[(sep4 + 1)..].Trim();
            }
            else
            {
                unitSpan = rest.Trim();
            }
        }
        else
        {
            valSpan = rest.Trim();
        }

        if (!double.TryParse(valSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            double.IsNaN(value) || double.IsInfinity(value))
        {
            return false;
        }

        var unit = ParseUnit(unitSpan);
        var quality = ParseQuality(qualitySpan);

        measurement = TelemetryMeasurement.Create(
            batchId,
            timestampUtc,
            tagSpan.ToString(),
            value,
            unit,
            quality
        );

        return true;
    }

    public static bool TryParseTimestamp(ReadOnlySpan<char> span, out DateTime timestampUtc)
    {
        timestampUtc = DateTime.UtcNow;
        if (span.IsEmpty || span.Equals("NOW", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (long.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixTime))
        {
            if (unixTime < 0)
                return false;

            // Differentiate epoch milliseconds (> 10^10) from epoch seconds
            if (unixTime > 10_000_000_000L)
            {
                timestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(unixTime).UtcDateTime;
            }
            else
            {
                timestampUtc = DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
            }
            return true;
        }

        if (DateTime.TryParse(span, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
        {
            timestampUtc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            return true;
        }

        return false;
    }

    public static MeasurementUnit ParseUnit(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
            return MeasurementUnit.Celsius;

        if (span.Equals("Celsius", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("C", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("degC", StringComparison.OrdinalIgnoreCase))
            return MeasurementUnit.Celsius;

        if (span.Equals("Bar", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("bar", StringComparison.OrdinalIgnoreCase))
            return MeasurementUnit.Bar;

        if (span.Equals("M3PerHour", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("m3/h", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("m3h", StringComparison.OrdinalIgnoreCase))
            return MeasurementUnit.M3PerHour;

        if (span.Equals("Rpm", StringComparison.OrdinalIgnoreCase))
            return MeasurementUnit.Rpm;

        if (span.Equals("Ph", StringComparison.OrdinalIgnoreCase))
            return MeasurementUnit.Ph;

        if (span.Equals("Percent", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("%", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("pct", StringComparison.OrdinalIgnoreCase))
            return MeasurementUnit.Percent;

        if (span.Equals("Kilogram", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("kg", StringComparison.OrdinalIgnoreCase))
            return MeasurementUnit.Kilogram;

        if (span.Equals("Meters", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("m", StringComparison.OrdinalIgnoreCase))
            return MeasurementUnit.Meters;

        return MeasurementUnit.Celsius;
    }

    public static TelemetryQuality ParseQuality(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
            return TelemetryQuality.Good;

        if (span.Equals("Good", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("1", StringComparison.OrdinalIgnoreCase))
            return TelemetryQuality.Good;

        if (span.Equals("Bad", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("0", StringComparison.OrdinalIgnoreCase))
            return TelemetryQuality.Bad;

        if (span.Equals("Uncertain", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("?", StringComparison.OrdinalIgnoreCase))
            return TelemetryQuality.Uncertain;

        return TelemetryQuality.Good;
    }
}
