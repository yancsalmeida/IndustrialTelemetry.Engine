using System.Text.Json;
using IndustrialTelemetry.Api.Filters;
using IndustrialTelemetry.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Api;

public class IdempotencyFilterTests
{
    private readonly IIdempotencyStore _store = Substitute.For<IIdempotencyStore>();
    private readonly IdempotencyFilter _filter;

    public IdempotencyFilterTests()
    {
        _filter = new IdempotencyFilter(_store);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithoutIdempotencyHeader_ExecutesNextDirectly()
    {
        // Arrange
        var httpContext = new DefaultHttpContext { Request = { Method = "POST" } };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executingContext = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());

        bool nextCalled = false;
        ActionExecutionDelegate next = () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object())
            {
                Result = new OkObjectResult(new { success = true })
            });
        };

        // Act
        await _filter.OnActionExecutionAsync(executingContext, next);

        // Assert
        nextCalled.ShouldBeTrue();
        await _store.DidNotReceiveWithAnyArgs().TryGetAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithExistingKey_ReturnsCachedResultWithoutCallingNext()
    {
        // Arrange
        const string key = "idemp-key-12345";
        var httpContext = new DefaultHttpContext { Request = { Method = "POST" } };
        httpContext.Request.Headers[IdempotencyFilter.HeaderName] = key;

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executingContext = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());

        var cachedEntry = new IdempotencyEntry(StatusCodes.Status200OK, "application/json", "{\"accepted\":true}");
        _store.TryGetAsync(key, Arg.Any<CancellationToken>()).Returns(cachedEntry);

        bool nextCalled = false;
        ActionExecutionDelegate next = () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        };

        // Act
        await _filter.OnActionExecutionAsync(executingContext, next);

        // Assert
        nextCalled.ShouldBeFalse();
        httpContext.Response.Headers["X-Cache-Lookup"].ToString().ShouldBe("HIT");

        var contentResult = executingContext.Result.ShouldBeOfType<ContentResult>();
        contentResult.StatusCode.ShouldBe(StatusCodes.Status200OK);
        contentResult.Content.ShouldBe("{\"accepted\":true}");
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithNewKey_ExecutesActionAndStoresResponse()
    {
        // Arrange
        const string key = "idemp-key-new";
        var httpContext = new DefaultHttpContext { Request = { Method = "POST" } };
        httpContext.Request.Headers[IdempotencyFilter.HeaderName] = key;

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executingContext = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());

        _store.TryGetAsync(key, Arg.Any<CancellationToken>()).Returns((IdempotencyEntry?)null);

        bool nextCalled = false;
        var responsePayload = new { id = 42, status = "Created" };
        ActionExecutionDelegate next = () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object())
            {
                Result = new OkObjectResult(responsePayload)
            });
        };

        // Act
        await _filter.OnActionExecutionAsync(executingContext, next);

        // Assert
        nextCalled.ShouldBeTrue();
        httpContext.Response.Headers["X-Cache-Lookup"].ToString().ShouldBe("MISS");

        await _store.Received(1).SetAsync(
            key,
            Arg.Is<IdempotencyEntry>(e => e.StatusCode == StatusCodes.Status200OK && e.Body.Contains("42")),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>()
        );
    }
}
