using FluentValidation;
using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Domain.Enums;

namespace IndustrialTelemetry.Application.Validation;

public class CreateAlertRuleDtoValidator : AbstractValidator<CreateAlertRuleDto>
{
    public CreateAlertRuleDtoValidator()
    {
        RuleFor(x => x.SensorTag)
            .NotEmpty().WithMessage("Sensor tag is required.")
            .MaximumLength(32);

        RuleFor(x => x)
            .Must(x => x.MinThreshold.HasValue || x.MaxThreshold.HasValue || x.MaxRateOfChangePerMinute.HasValue || x.MinRateOfChangePerMinute.HasValue)
            .WithMessage("At least one threshold (Min, Max, or Rate-of-Change) must be set.");

        RuleFor(x => x)
            .Must(x => !x.MinThreshold.HasValue || !x.MaxThreshold.HasValue || x.MinThreshold < x.MaxThreshold)
            .WithMessage("MinThreshold must be less than MaxThreshold.");

        RuleFor(x => x)
            .Must(x => !x.MinRateOfChangePerMinute.HasValue || !x.MaxRateOfChangePerMinute.HasValue || x.MinRateOfChangePerMinute < x.MaxRateOfChangePerMinute)
            .WithMessage("MinRateOfChangePerMinute must be less than MaxRateOfChangePerMinute.");

        RuleFor(x => x.WindowSeconds)
            .GreaterThan(0)
            .WithMessage("WindowSeconds must be greater than zero.");

        RuleFor(x => x.Severity)
            .NotEmpty()
            .IsEnumName(typeof(AlertSeverity), caseSensitive: false)
            .WithMessage("Severity must be Info, Warning, or Critical.");
    }
}
