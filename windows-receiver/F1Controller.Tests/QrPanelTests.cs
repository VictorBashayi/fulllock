using F1Controller.Security;
using F1Controller.UI;
using QRCoder;
using Xunit;

namespace F1Controller.Tests;

public class QrPanelTests
{
    static readonly string Text = PairingCode.ToQrText("K7QD9MXP2A");

    static bool[,] Matrix(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var m = data.ModuleMatrix; // QRCoder's matrix may include its own quiet zone
        var n = m.Count;
        var g = new bool[n, n];
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++) g[y, x] = m[y][x];
        return g;
    }

    [Fact]
    public void RendersSquareWithQuietZone()
    {
        var lines = QrPanel.Render(Text, 80, 40)!;
        Assert.NotNull(lines);
        var size = lines[0].Length; // modules + 4
        Assert.All(lines, l => Assert.Equal(size, l.Length));
        Assert.Equal((size + 1) / 2, lines.Count);
        Assert.Equal(new string('█', size), lines[0]); // light quiet zone is drawn filled
    }

    [Fact]
    public void MatrixMatchesQRCoder()
    {
        var lines = QrPanel.Render(Text, 80, 40)!;
        var size = lines[0].Length;
        // inverted mapping: a filled half is a LIGHT module
        bool Dark(int y, int x)
        {
            var c = lines[y / 2][x];
            var topLight = c is '█' or '▀';
            var botLight = c is '█' or '▄';
            return !(y % 2 == 0 ? topLight : botLight);
        }
        var expected = Matrix(Text);
        var n = expected.GetLength(0);
        // QRCoder with default quiet zone adds a border; compare the core centred in our grid
        var core = size - 4;
        var off = (n - core) / 2; // 0 if QRCoder added no quiet zone
        Assert.True(off >= 0);
        for (var y = 0; y < size + (size % 2); y++)
            for (var x = 0; x < size; x++)
            {
                var cy = y - 2 + off; var cx = x - 2 + off;
                var exp = y < size && cy >= off && cy < off + core && cx >= off && cx < off + core && expected[cy, cx];
                Assert.Equal(exp, Dark(y, x));
            }
    }

    [Fact] public void TooNarrowReturnsNoQr() => Assert.Null(QrPanel.Render(Text, 20, 40));
    [Fact] public void TooShortReturnsNoQr() => Assert.Null(QrPanel.Render(Text, 80, 5));
}
