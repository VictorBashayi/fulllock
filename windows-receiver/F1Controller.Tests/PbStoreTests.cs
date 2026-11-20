using F1Controller.Validation;

namespace F1Controller.Tests;

public class PbStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pbstore-" + Guid.NewGuid().ToString("N"));
    private string PathIn => Path.Combine(_dir, "pbs.json");

    public PbStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void FirstLapIsPb()
    {
        var store = new PbStore(PathIn);
        Assert.True(store.TryRecord(5, 12, wet: false, 80000));
        Assert.Equal(80000u, store.Get(5, 12, wet: false));
    }

    [Fact]
    public void SlowerLapIgnored()
    {
        var store = new PbStore(PathIn);
        store.TryRecord(5, 12, false, 80000);
        Assert.False(store.TryRecord(5, 12, false, 81000));
        Assert.False(store.TryRecord(5, 12, false, 80000));
        Assert.Equal(80000u, store.Get(5, 12, false));
    }

    [Fact]
    public void DryAndWetAndSessionTypesAreSeparate()
    {
        var store = new PbStore(PathIn);
        store.TryRecord(5, 12, false, 80000);
        store.TryRecord(5, 12, true, 95000);
        store.TryRecord(5, 10, false, 82000);
        Assert.Equal(80000u, store.Get(5, 12, false));
        Assert.Equal(95000u, store.Get(5, 12, true));
        Assert.Equal(82000u, store.Get(5, 10, false));
        Assert.Equal(0u, store.Get(6, 12, false));
    }

    [Fact]
    public void SurvivesRestart()
    {
        new PbStore(PathIn).TryRecord(5, 12, true, 95000);
        Assert.Equal(95000u, new PbStore(PathIn).Get(5, 12, true));
    }

    [Fact]
    public void CorruptFileStartsEmptyAndKeepsBad()
    {
        File.WriteAllText(PathIn, "{not json");
        var store = new PbStore(PathIn);
        Assert.Equal(0u, store.Get(5, 12, false));
        Assert.True(File.Exists(PathIn + ".bad"));
        Assert.True(store.TryRecord(5, 12, false, 80000));
        Assert.Equal(80000u, new PbStore(PathIn).Get(5, 12, false));
    }

    [Fact]
    public void UnknownTrackOrZeroLapIgnored()
    {
        var store = new PbStore(PathIn);
        Assert.False(store.TryRecord(-1, 12, false, 80000));
        Assert.False(store.TryRecord(5, 12, false, 0));
        Assert.Equal(0u, store.Get(-1, 12, false));
    }

    [Fact]
    public void SaveFailureDoesNotThrow()
    {
        // Directory doesn't exist, so the write fails.
        var store = new PbStore(Path.Combine(_dir, "missing", "pbs.json"));
        Assert.True(store.TryRecord(5, 12, false, 80000));
        Assert.Equal(80000u, store.Get(5, 12, false));
    }

    [Fact]
    public void SectorsAreStoredAndSurviveRestart()
    {
        new PbStore(PathIn).TryRecord(5, 13, false, 80000, 25000, 30000);
        var list = new PbStore(PathIn).List(13);
        Assert.Equal(new PbStore.PbLap(80000, 25000, 30000), Assert.Single(list).Dry);
    }

    [Fact]
    public void OldFormatLoadsWithoutSectors()
    {
        File.WriteAllText(PathIn, "{\"3/13/dry\":{\"Ms\":86839,\"Date\":\"2026-11-20\"},\"3/13/wet\":{\"Ms\":116657,\"Date\":\"2026-11-20\"}}");
        var list = new PbStore(PathIn).List(13);
        Assert.Equal(new PbStore.TrackPbs(3, new PbStore.PbLap(86839, 0, 0), new PbStore.PbLap(116657, 0, 0)), Assert.Single(list));
    }

    [Fact]
    public void ListIsPerSessionTypeAndOrderedByTrack()
    {
        var store = new PbStore(PathIn);
        store.TryRecord(7, 13, false, 90000);
        store.TryRecord(3, 13, true, 99000);
        store.TryRecord(5, 10, false, 80000);
        Assert.Equal(new sbyte[] { 3, 7 }, store.List(13).Select(t => t.TrackId).ToArray());
        Assert.Equal(new PbStore.PbLap(99000, 0, 0), store.List(13)[0].Wet);
        Assert.Equal(default, store.List(13)[0].Dry);
    }

    [Fact]
    public void ChangedFiresOnlyOnNewPb()
    {
        var store = new PbStore(PathIn);
        int changes = 0;
        store.Changed += () => changes++;
        store.TryRecord(5, 13, false, 80000);
        store.TryRecord(5, 13, false, 81000);
        Assert.Equal(1, changes);
    }
}
