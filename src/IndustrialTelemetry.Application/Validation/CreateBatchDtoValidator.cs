using FluentValidation;
using IndustrialTelemetry.Application.DTOs;

namespace IndustrialTelemetry.Application.Validation;

public class CreateBatchDtoValidator : AbstractValidator<CreateBatchDto>
{
    public CreateBatchDtoValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Batch code is required.")
            .MaximumLength(64).WithMessage("Batch code cannot exceed 64 characters.")
            .Matches(@"^[A-Za-z0-9\-_]+$").WithMessage("Batch code must be alphanumeric (hyphens and underscores allowed).");

        RuleFor(x => x.ProductRecipe)
            .NotEmpty().WithMessage("Product recipe is required.")
            .MaximumLength(128).WithMessage("Product recipe cannot exceed 128 characters.");

        RuleFor(x => x.ReactorId)
            .NotEmpty().WithMessage("Reactor identifier is required.")
            .MaximumLength(32).WithMessage("Reactor identifier cannot exceed 32 characters.");

        RuleFor(x => x.TargetVolumeM3)
            .GreaterThan(0.0).WithMessage("Target volume must be greater than zero.");
    }
}
