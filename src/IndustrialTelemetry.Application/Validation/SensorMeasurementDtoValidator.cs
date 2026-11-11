using FluentValidation;
using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Domain.Enums;

namespace IndustrialTelemetry.Application.Validation;

public class SensorMeasurementDtoValidator : AbstractValidator<SensorMeasurementDto>
{
    public SensorMeasurementDtoValidator()
    {
        RuleFor(x => x.SensorTag)
            .NotEmpty().WithMessage("Sensor tag is required.")
            .MaximumLength(32);

        RuleFor(x => x.Unit)
            .NotEmpty().WithMessage("Measurement unit is required.")
            .IsEnumName(typeof(MeasurementUnit), caseSensitive: false)
            .WithMessage("Unit must be a valid MeasurementUnit (Celsius, Bar, M3PerHour, Rpm, Ph, Percent, Kilogram, Meters, MolPerLiter).");

        RuleFor(x => x.Quality)
            .IsEnumName(typeof(TelemetryQuality), caseSensitive: false)
            .When(x => !string.IsNullOrEmpty(x.Quality))
            .WithMessage("Quality must be Good, Bad, or Uncertain.");
    }
}
