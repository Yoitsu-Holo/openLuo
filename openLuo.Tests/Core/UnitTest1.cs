using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Core.Tests;

public class CommandResultTests
{
    [Fact]
    public void Ok_SetsSuccessTrue()
    {
        var result = CommandResult.Ok("hello");
        Assert.True(result.Success);
        Assert.Equal("hello", result.Output);
        Assert.Null(result.Error);
        Assert.NotEmpty(result.Presentation.Messages);
        Assert.Equal("hello", result.Presentation.ToPlainText());
    }

    [Fact]
    public void Ok_WithPresentation_SetsBoth()
    {
        var presentation = CommandPresentation.FromText("hello");
        var result = CommandResult.Ok(presentation);
        Assert.True(result.Success);
        Assert.Equal("hello", result.Output);
        Assert.Same(presentation, result.Presentation);
    }

    [Fact]
    public void Fail_SetsSuccessFalse()
    {
        var result = CommandResult.Fail("oops");
        Assert.False(result.Success);
        Assert.Equal("oops", result.Error);
    }
}
