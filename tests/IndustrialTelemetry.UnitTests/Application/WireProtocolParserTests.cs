using IndustrialTelemetry.Application.Parsing;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Application;

public class WireProtocolParserTests
{
    private readonly Guid _batchId = Guid.NewGuid();

    [Fact]
    public void TryParseRecord_ValidCompleteLine_ParsesCorrectly()
    {
        const string line = "1730678400000|TT-101|85.45|Celsius|Good";

        var success = WireProtocolParser.TryParseRecord(line.AsSpan(), _batchId, out var m);

        success.ShouldBeTrue();
        m.ShouldNotBeNull();
        m.BatchId.ShouldBe(_batchId);
        m.SensorTag.ShouldBe("TT-101");
        m.Value.ShouldBe(85.45, 0.001);
        m.Unit.ShouldBe(MeasurementUnit.Celsius);
        m.Quality.ShouldBe(TelemetryQuality.Good);
    }

    [Fact]
    public void TryParseRecord_IsoTimestampAndBarUnit_ParsesCorrectly()
    {
        const string line = "2026-11-10T14:30:00Z|PT-202|4.82|bar|Uncertain";

        var success = WireProtocolParser.TryParseRecord(line.AsSpan(), _batchId, out var m);

        success.ShouldBeTrue();
        m.SensorTag.ShouldBe("PT-202");
        m.Value.ShouldBe(4.82, 0.001);
        m.Unit.ShouldBe(MeasurementUnit.Bar);
        m.Quality.ShouldBe(TelemetryQuality.Uncertain);
        m.TimestampUtc.Year.ShouldBe(2026);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1730678400000")] // Missing sensor tag and value
    [InlineData("1730678400000|")] // Empty tag
    [InlineData("1730678400000|TT-101")] // Missing value
    [InlineData("1730678400000|TT-101|NaN")] // Invalid NaN value
    [InlineData("1730678400000|TT-101|Infinity")] // Invalid Infinity value
    [InlineData("INVALID_DATE|TT-101|50.0")] // Malformed timestamp
    public void TryParseRecord_MalformedInput_ReturnsFalse(string rawLine)
    {
        var success = WireProtocolParser.TryParseRecord(rawLine.AsSpan(), _batchId, out var m);

        success.ShouldBeFalse();
        m.ShouldBeNull();
    }

    [Fact]
    public void ParseLines_MultiLinePayload_ParsesAllValidAndSkipsMalformed()
    {
        const string payload =
            "1730678400000|TT-101|85.5|Celsius|Good\r\n" +
            "1730678400100|PT-101|3.2|Bar|Good\n" +
            "\n" +
            "MALFORMED_LINE_SKIPPED\r\n" +
            "1730678400200|FT-101|120.0|M3PerHour|Good\n" +
            "1730678400300|AT-101|7.2|Ph|Uncertain";

        var measurements = new List<TelemetryMeasurement>();
        var parsedCount = WireProtocolParser.ParseLines(
            payload.AsSpan(),
            _batchId,
            measurements,
            out var totalLines,
            out var skippedLines);

        parsedCount.ShouldBe(4);
        totalLines.ShouldBe(5); // 4 valid + 1 malformed non-empty
        skippedLines.ShouldBe(1);
        measurements.Count.ShouldBe(4);

        measurements[0].SensorTag.ShouldBe("TT-101");
        measurements[1].SensorTag.ShouldBe("PT-101");
        measurements[2].SensorTag.ShouldBe("FT-101");
        measurements[3].SensorTag.ShouldBe("AT-101");
        measurements[3].Unit.ShouldBe(MeasurementUnit.Ph);
    }

    [Theory]
    [InlineData("MolPerLiter")]
    [InlineData("mol/L")]
    [InlineData("mol/l")]
    [InlineData("Molar")]
    public void TryParseRecord_MolPerLiterVariations_ParsesCorrectly(string unitStr)
    {
        var line = $"1730678400000|CA-101-EST|1.25|{unitStr}|Good";

        var success = WireProtocolParser.TryParseRecord(line.AsSpan(), _batchId, out var m);

        success.ShouldBeTrue();
        m.ShouldNotBeNull();
        m.SensorTag.ShouldBe("CA-101-EST");
        m.Value.ShouldBe(1.25, 0.001);
        m.Unit.ShouldBe(MeasurementUnit.MolPerLiter);
        m.Quality.ShouldBe(TelemetryQuality.Good);
    }
}
