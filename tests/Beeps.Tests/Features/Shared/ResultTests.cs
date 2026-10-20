using Beeps.Features.Shared;

namespace Beeps.Tests.Features.Shared;

public sealed class ResultTests
{
    [Fact]
    public void ValueConvertsToSuccess()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ErrorConvertsToFailure()
    {
        Result<int> result = Error.NotFound("beep_not_found");

        Assert.False(result.IsSuccess);
        Assert.Equal("beep_not_found", result.Error.Code);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
    }

    [Fact]
    public void ReadingTheValueOfAFailureThrows()
    {
        Result<string> result = Error.Conflict("taken");

        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("taken", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonGenericResultCarriesSuccessOrError()
    {
        Result failure = Error.Validation("bad_input");

        Assert.True(Result.Success.IsSuccess);
        Assert.False(failure.IsSuccess);
        Assert.Equal("bad_input", failure.Error.Code);
    }

    [Fact]
    public void RateLimitedErrorHasTheSharedCodeAndWait()
    {
        var error = Error.RateLimited(TimeSpan.FromSeconds(30), "slow down");

        Assert.Equal(ErrorKind.RateLimited, error.Kind);
        Assert.Equal(ErrorCodes.RateLimited, error.Code);
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }
}
