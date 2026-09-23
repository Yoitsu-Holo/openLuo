using openLuo.Infrastructure.Security;
using Xunit;

namespace openLuo.Tests.Security;

public class InputValidatorTests
{
    private readonly InputValidator _validator = new();

    [Fact]
    public void ValidateUserInput_RejectsNull()
    {
        Assert.False(_validator.ValidateUserInput(null!));
    }

    [Fact]
    public void ValidateUserInput_RejectsEmpty()
    {
        Assert.False(_validator.ValidateUserInput(""));
    }

    [Fact]
    public void ValidateUserInput_RejectsTooLong()
    {
        var longInput = new string('a', 1001);
        Assert.False(_validator.ValidateUserInput(longInput));
    }

    [Fact]
    public void ValidateUserInput_AcceptsValid()
    {
        Assert.True(_validator.ValidateUserInput("Hello, world!"));
    }

    [Theory]
    [InlineData("DROP TABLE users")]
    [InlineData("SELECT * FROM users")]
    [InlineData("'; DELETE FROM users--")]
    [InlineData("UNION SELECT password")]
    [InlineData("/* comment */ EXEC")]
    public void ValidateUserInput_RejectsSqlInjection(string input)
    {
        Assert.False(_validator.ValidateUserInput(input));
    }

    [Fact]
    public void ValidateResourceId_AcceptsValid()
    {
        Assert.True(_validator.ValidateResourceId("stamina-123"));
        Assert.True(_validator.ValidateResourceId("mood_level"));
        Assert.True(_validator.ValidateResourceId("abc123"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("has.dot")]
    [InlineData("has@symbol")]
    public void ValidateResourceId_RejectsInvalid(string? id)
    {
        Assert.False(_validator.ValidateResourceId(id!));
    }

    [Fact]
    public void ValidateJson_AcceptsValid()
    {
        Assert.True(_validator.ValidateJson("{\"key\":\"value\"}", out var result));
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("{invalid}")]
    [InlineData("not json")]
    public void ValidateJson_RejectsInvalid(string? json)
    {
        Assert.False(_validator.ValidateJson(json!, out var result));
        Assert.Null(result);
    }
}
