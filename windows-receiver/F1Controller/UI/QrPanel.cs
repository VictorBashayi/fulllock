using System.Text;
using QRCoder;

namespace F1Controller.UI;

public static class QrPanel
{
    const int Quiet = 2;

    /// <summary>
    /// Half-block QR for a DARK console. Rule (inverted on purpose): LIGHT modules, including the
    /// 2-module quiet zone, are drawn as filled blocks; DARK modules are spaces (the console's own
    /// dark background). Per cell: top and bottom light = '█', top only = '▀', bottom only = '▄', neither = ' '.
    /// Two module rows per text line. Returns null if it doesn't fit maxWidth x maxHeight.
    /// </summary>
    public static IReadOnlyList<string>? Render(string text, int maxWidth, int maxHeight)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var m = data.ModuleMatrix;
        var core = 17 + 4 * data.Version;
        var skip = (m.Count - core) / 2; // QRCoder's own quiet zone, if it added one
        var size = core + 2 * Quiet;
        var rows = (size + 1) / 2;
        if (size > maxWidth || rows > maxHeight) return null;

        bool Light(int y, int x)
        {
            int cy = y - Quiet + skip, cx = x - Quiet + skip;
            return y >= size || cy < skip || cy >= skip + core || cx < skip || cx >= skip + core || !m[cy][cx];
        }

        var lines = new List<string>(rows);
        for (var r = 0; r < rows; r++)
        {
            var sb = new StringBuilder(size);
            for (var x = 0; x < size; x++)
            {
                var top = Light(2 * r, x);
                var bot = Light(2 * r + 1, x);
                sb.Append(top ? (bot ? '█' : '▀') : (bot ? '▄' : ' '));
            }
            lines.Add(sb.ToString());
        }
        return lines;
    }
}
