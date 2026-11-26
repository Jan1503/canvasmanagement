using CanvasManagement.Interfaces;
using Xunit;

namespace CanvasManagement.Tests;

public class DisplayNumberTests
{
    [Theory]
    [InlineData(-0.3, 0, "0")]
    [InlineData(-0.04, 1, "0.0")]
    [InlineData(-0.6, 0, "-1")]
    [InlineData(2.5, 0, "3")]
    [InlineData(21.45, 1, "21.5")]
    [InlineData(-12.0, 0, "-12")]
    public void Rounds_without_negative_zero(double value, int decimals, string expected) =>
        Assert.Equal(expected, DisplayNumber.Round(value, decimals));

    [Fact]
    public void Not_a_number_shows_a_dash() => Assert.Equal("–", DisplayNumber.Round(double.NaN));
}
