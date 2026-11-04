using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Domain.Exceptions;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Domain;

public class BatchTests
{
    [Fact]
    public void Create_WithValidParameters_ShouldInitializeInScheduledStatus()
    {
        // Act
        var batch = Batch.Create("BATCH-2026-001", "Polymer-A12", "R-101", 12.5, "Standard test run");

        // Assert
        batch.Id.ShouldNotBe(Guid.Empty);
        batch.Code.ShouldBe("BATCH-2026-001");
        batch.ProductRecipe.ShouldBe("Polymer-A12");
        batch.ReactorId.ShouldBe("R-101");
        batch.TargetVolumeM3.ShouldBe(12.5);
        batch.Status.ShouldBe(BatchStatus.Scheduled);
        batch.StartTimeUtc.ShouldBeNull();
        batch.EndTimeUtc.ShouldBeNull();
        batch.AbortReason.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyCode_ShouldThrowDomainValidationException(string invalidCode)
    {
        Should.Throw<DomainValidationException>(() =>
            Batch.Create(invalidCode, "Polymer-A12", "R-101", 10.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void Create_WithNonPositiveVolume_ShouldThrowDomainValidationException(double invalidVolume)
    {
        Should.Throw<DomainValidationException>(() =>
            Batch.Create("BATCH-001", "Polymer-A12", "R-101", invalidVolume));
    }

    [Fact]
    public void Start_WhenScheduled_ShouldTransitionToInProgress()
    {
        // Arrange
        var batch = Batch.Create("BATCH-001", "Recipe-1", "R-101", 10.0);
        var startTime = DateTime.UtcNow;

        // Act
        batch.Start(startTime);

        // Assert
        batch.Status.ShouldBe(BatchStatus.InProgress);
        batch.StartTimeUtc.ShouldBe(startTime);
    }

    [Fact]
    public void Start_WhenAlreadyInProgress_ShouldThrowInvalidBatchStateTransitionException()
    {
        // Arrange
        var batch = Batch.Create("BATCH-001", "Recipe-1", "R-101", 10.0);
        batch.Start(DateTime.UtcNow);

        // Act & Assert
        var ex = Should.Throw<InvalidBatchStateTransitionException>(() =>
            batch.Start(DateTime.UtcNow));
        ex.FromStatus.ShouldBe(BatchStatus.InProgress);
        ex.ToStatus.ShouldBe(BatchStatus.InProgress);
    }

    [Fact]
    public void Complete_WhenInProgress_ShouldTransitionToCompleted()
    {
        // Arrange
        var batch = Batch.Create("BATCH-001", "Recipe-1", "R-101", 10.0);
        var start = DateTime.UtcNow.AddHours(-2);
        var end = DateTime.UtcNow;
        batch.Start(start);

        // Act
        batch.Complete(end);

        // Assert
        batch.Status.ShouldBe(BatchStatus.Completed);
        batch.EndTimeUtc.ShouldBe(end);
    }

    [Fact]
    public void Complete_WhenScheduled_ShouldThrowInvalidBatchStateTransitionException()
    {
        // Arrange
        var batch = Batch.Create("BATCH-001", "Recipe-1", "R-101", 10.0);

        // Act & Assert
        Should.Throw<InvalidBatchStateTransitionException>(() =>
            batch.Complete(DateTime.UtcNow));
    }

    [Fact]
    public void Complete_WithTimestampPrecedingStart_ShouldThrowDomainValidationException()
    {
        // Arrange
        var batch = Batch.Create("BATCH-001", "Recipe-1", "R-101", 10.0);
        var start = DateTime.UtcNow;
        batch.Start(start);

        // Act & Assert
        Should.Throw<DomainValidationException>(() =>
            batch.Complete(start.AddMinutes(-10)));
    }

    [Fact]
    public void Abort_WhenInProgress_ShouldTransitionToAbortedWithReason()
    {
        // Arrange
        var batch = Batch.Create("BATCH-001", "Recipe-1", "R-101", 10.0);
        batch.Start(DateTime.UtcNow.AddMinutes(-30));
        var abortTime = DateTime.UtcNow;

        // Act
        batch.Abort("Cooling jacket pressure loss detected", abortTime);

        // Assert
        batch.Status.ShouldBe(BatchStatus.Aborted);
        batch.EndTimeUtc.ShouldBe(abortTime);
        batch.AbortReason.ShouldBe("Cooling jacket pressure loss detected");
    }

    [Fact]
    public void Abort_WhenAlreadyCompleted_ShouldThrowInvalidBatchStateTransitionException()
    {
        // Arrange
        var batch = Batch.Create("BATCH-001", "Recipe-1", "R-101", 10.0);
        batch.Start(DateTime.UtcNow.AddHours(-1));
        batch.Complete(DateTime.UtcNow);

        // Act & Assert
        Should.Throw<InvalidBatchStateTransitionException>(() =>
            batch.Abort("Late issue", DateTime.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Abort_WithEmptyReason_ShouldThrowDomainValidationException(string emptyReason)
    {
        // Arrange
        var batch = Batch.Create("BATCH-001", "Recipe-1", "R-101", 10.0);
        batch.Start(DateTime.UtcNow);

        // Act & Assert
        Should.Throw<DomainValidationException>(() =>
            batch.Abort(emptyReason, DateTime.UtcNow));
    }
}
