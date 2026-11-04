using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using F1Controller.Pad.HidHide;

namespace F1Controller.Tests;

internal sealed class FakeHidHide : IHidHide
{
    public bool Cloaked;
    public readonly List<string> Hidden = new();
    public readonly List<string> Apps = new();
    public readonly List<string> Calls = new();
    public string? ThrowOn;              // call prefix, e.g. "hide"
    public Action<string>? BeforeHide;   // lets a test inspect state at hide time

    private void Maybe(string call)
    {
        if (ThrowOn != null && call.StartsWith(ThrowOn)) throw new HidHideException("simulated CLI failure");
        Calls.Add(call);
    }

    public bool IsCloaked() => Cloaked;
    public IReadOnlyList<string> HiddenDevices() => Hidden.ToList();
    public IReadOnlyList<string> RegisteredApps() => Apps.ToList();
    public void RegisterApp(string exePath) { Maybe("reg:" + exePath); Apps.Add(exePath); }
    public void UnregisterApp(string exePath) { Maybe("unreg:" + exePath); Apps.Remove(exePath); }
    public void Hide(string instancePath) { BeforeHide?.Invoke(instancePath); Maybe("hide:" + instancePath); Hidden.Add(instancePath); }
    public void Unhide(string instancePath) { Maybe("unhide:" + instancePath); Hidden.RemoveAll(h => string.Equals(h, instancePath, StringComparison.OrdinalIgnoreCase)); }
    public void SetCloak(bool on) { Maybe(on ? "cloak:on" : "cloak:off"); Cloaked = on; }
}

public sealed class HidHideGuardTests : IDisposable
{
    private const string Exe = @"C:\Program Files\FullLock\FullLock.exe";
    private const string P = @"HID\VID_054C&PID_09CC&MI_03\7&18EAC8C&0&0000";
    private const string P2 = @"HID\VID_054C&PID_09CC&MI_03\7&210509CF&0&0000";
    private const string U = @"HID\VID_046D&PID_C52B&MI_00\8&USER&0&0000";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fulllock-hh-" + Guid.NewGuid().ToString("N"));
    private string State => Path.Combine(_dir, "hidhide-state.json");
    private readonly FakeHidHide _hh = new();
    private readonly List<string> _log = new();

    public HidHideGuardTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private HidHideGuard Guard() => new(_hh, Exe, State, _log.Add);

    [Fact]
    public void Guard_AlreadyConfigured_NoChanges()
    {
        _hh.Apps.Add(Exe); _hh.Hidden.Add(P.ToLowerInvariant()); _hh.Cloaked = true;
        Guard().EnsureHidden(P);
        Assert.Empty(_hh.Calls);
        Assert.False(File.Exists(State));
    }

    [Fact]
    public void Guard_Fresh_RegistersBeforeHideThenCloaks()
    {
        Guard().EnsureHidden(P);
        Assert.Equal(new[] { "reg:" + Exe, "hide:" + P, "cloak:on" }, _hh.Calls);
        var state = HidHideRestoreState.Load(State)!;
        Assert.Equal(new[] { P }, state.HiddenByUs);
        Assert.True(state.CloakTurnedOnByUs);
        Assert.Contains("Hid your real DS4 from games so F1 sees only one controller.", _log);
    }

    [Fact]
    public void Guard_StateWrittenBeforeApplying()
    {
        bool sawState = false;
        _hh.BeforeHide = path => sawState = HidHideRestoreState.Load(State)?.HiddenByUs.Contains(path) == true;
        Guard().EnsureHidden(P);
        Assert.True(sawState);
    }

    [Fact]
    public void Guard_Restore_UndoesOnlyOwnChanges()
    {
        _hh.Apps.Add(Exe); _hh.Hidden.Add(U); _hh.Cloaked = true;
        var g = Guard();
        g.EnsureHidden(P);
        _hh.Calls.Clear();

        g.Restore();

        Assert.Equal(new[] { "unhide:" + P }, _hh.Calls);
        Assert.Contains(U, _hh.Hidden);
        Assert.True(_hh.Cloaked);
        Assert.Contains(Exe, _hh.Apps);
        Assert.False(File.Exists(State));
    }

    [Fact]
    public void Guard_Restore_TurnsCloakOffIfWeTurnedItOn()
    {
        var g = Guard();
        g.EnsureHidden(P);
        g.Restore();
        Assert.Contains("cloak:off", _hh.Calls);
        Assert.False(_hh.Cloaked);
    }

    [Fact]
    public void Guard_SecondPath_HidesAndRestoresBoth()
    {
        var g = Guard();
        g.EnsureHidden(P);
        g.EnsureHidden(P2);
        Assert.Contains(P2, _hh.Hidden);
        Assert.Equal(new[] { P, P2 }, HidHideRestoreState.Load(State)!.HiddenByUs);

        g.Restore();
        Assert.Empty(_hh.Hidden);
    }

    [Fact]
    public void Guard_RestorePending_RestoresCrashState()
    {
        _hh.Hidden.Add(P); _hh.Cloaked = true; _hh.Apps.Add(Exe);
        new HidHideRestoreState(new List<string> { P }, CloakTurnedOnByUs: true).Save(State);

        Guard().RestorePending();

        Assert.DoesNotContain(P, _hh.Hidden);
        Assert.False(_hh.Cloaked);
        Assert.False(File.Exists(State));
    }

    [Fact]
    public void Guard_CorruptState_IgnoredAndDeleted()
    {
        File.WriteAllText(State, "{not json");
        var ex = Record.Exception(() => Guard().RestorePending());
        Assert.Null(ex);
        Assert.False(File.Exists(State));
        Assert.Contains(_log, l => l.Contains("HidHide"));
    }

    [Fact]
    public void Guard_CliThrows_LogsAndContinues()
    {
        _hh.ThrowOn = "hide";
        var ex = Record.Exception(() => Guard().EnsureHidden(P));
        Assert.Null(ex);
        Assert.Contains(_log, l => l.Contains("HidHide"));
    }

    [Fact]
    public void Guard_Cleanup_UnregistersApp()
    {
        var g = Guard();
        g.EnsureHidden(P);
        g.Cleanup();
        Assert.DoesNotContain(Exe, _hh.Apps);
        Assert.Empty(_hh.Hidden);
        Assert.False(File.Exists(State));
    }
}
