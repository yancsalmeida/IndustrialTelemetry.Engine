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
            .Must(x => x.MinThreshold.HasValue || x.MaxThreshold.HasValue)
            .WithMessage("At least one threshold (Min or Max) must be set.");

        RuleFor(x => x)
            .Must(x => !x.MinThreshold.HasValue || !x.MaxThreshold.HasValue || x.MinThreshold < x.MaxThreshold)
            .WithMessage("MinThreshold must be less than MaxThreshold.");

        RuleFor(x => x.Severity)
            .NotEmpty()
            .IsEnumName(typeof(AlertSeverity), caseSensitive: false)
            .WithMessage("Severity must be Info, Warning, or Critical.");
    }
}
