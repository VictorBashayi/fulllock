using F1Controller.Security;
using Xunit;

namespace F1Controller.Tests;

public class PairingCodeTests
{
    [Fact]
    public void GenerateUsesAlphabetAndLength()
    {
        var all = Enumerable.Range(0, 1000).Select(_ => PairingCode.Generate()).ToList();
        Assert.All(all, c =>
        {
            Assert.Equal(10, c.Length);
            Assert.All(c, ch => Assert.Contains(ch, PairingCode.Alphabet));
        });
        Assert.True(all.Distinct().Count() > 1);
    }

    [Theory]
    [InlineData("k7qd-9mxp-2a")]
    [InlineData(" K7QD 9MXP 2A ")]
    public void NormalizeAcceptsVariants(string s) => Assert.Equal("K7QD9MXP2A", PairingCode.Normalize(s));

    [Fact]
    public void NormalizeMapsLookalikes()
    {
        Assert.Equal("K7QD9MXP10", PairingCode.Normalize("K7QD9MXPIO"));
        Assert.Equal("K7QD9MXP11", PairingCode.Normalize("K7QD9MXPil"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("K7QD9MXP2")]
    [InlineData("K7QD9MXP2AA")]
    [InlineData("K7QD9MXP2U")]
    [InlineData("K7QD9MXP2!")]
    public void NormalizeRejects(string? s) => Assert.Null(PairingCode.Normalize(s));

    [Fact]
    public void NormalizeRejectsNonAscii()
    {
        Assert.Null(PairingCode.Normalize("K7QD9MXP2\u017F"));
        Assert.Null(PairingCode.Normalize("K7QD9MXP2\uFF11"));
    }

    [Fact]
    public void FormatGroups() => Assert.Equal("K7QD-9MXP-2A", PairingCode.Format("K7QD9MXP2A"));

    [Fact]
    public void QrText() => Assert.Equal("fulllock://pair?code=K7QD9MXP2A", PairingCode.ToQrText("K7QD9MXP2A"));
}
