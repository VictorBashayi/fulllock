using System;
using System.IO;
using F1Controller.Pad;

namespace F1Controller.Tests;

public class VersionTests
{
    private static string ReadVersionFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VERSION")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "VERSION")).Trim();
    }

    [Fact]
    public void Assembly_Version_MatchesVersionFile()
    {
        var name = typeof(PadSettings).Assembly.GetName();
        Assert.Equal("FullLock", name.Name);
        Assert.Equal(ReadVersionFile(), name.Version!.ToString(3));
    }
}
