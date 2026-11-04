using SIMOrchestrator.Options;
using Xunit;

namespace SIMOrchestrator.Tests.Options;

public class ApiKeyOptionsValidatorTests
{
    private readonly ApiKeyOptionsValidator _validator = new();

    [Fact]
    public void Validate_NoKeys_Fails()
    {
        var result = _validator.Validate(null, new ApiKeyOptions());

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_ShortKey_Fails()
    {
        var result = _validator.Validate(null, new ApiKeyOptions { ApiKey = "too-short" });

        Assert.True(result.Failed);
        Assert.Contains("32", result.FailureMessage);
    }

    [Fact]
    public void Validate_ShortKeyInsideList_Fails()
    {
        var options = new ApiKeyOptions
        {
            ApiKeys = [new string('a', 40), "short"]
        };

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Fact]
    public void Validate_LongEnoughKeys_Succeeds()
    {
        var options = new ApiKeyOptions
        {
            ApiKey = new string('a', 32),
            ApiKeys = [new string('b', 48)]
        };

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void GetAllKeys_DeduplicatesAndSkipsBlanks()
    {
        var key = new string('k', 32);
        var options = new ApiKeyOptions { ApiKey = key, ApiKeys = [key, "", "  ", new string('z', 32)] };

        var keys = options.GetAllKeys();

        Assert.Equal(2, keys.Count);
        Assert.Contains(key, keys);
    }
}
