using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace F1Controller.Pad.HidHide;

/// <summary>
/// Hides the real DS4 from games while FullLock runs, so F1 sees only the virtual pad, and undoes
/// exactly what it changed on exit (or on the next launch after a crash). Never touches devices or
/// settings the user configured, and never throws: a HidHide problem must not stop a drive.
/// </summary>
public sealed class HidHideGuard(IHidHide hidHide, string exePath, string statePath, Action<string> log)
{
    private readonly object _lock = new();
    private readonly List<string> _hiddenByUs = new();
    private bool _cloakTurnedOnByUs;

    /// <summary>Undo changes left behind by a previous run that didn't exit cleanly.</summary>
    public void RestorePending()
    {
        lock (_lock)
        {
            HidHideRestoreState? pending;
            try { pending = HidHideRestoreState.Load(statePath); }
            catch (Exception ex)
            {
                log($"[HidHide] Ignoring unreadable restore file ({ex.Message}).");
                TryDeleteState();
                return;
            }
            if (pending == null) return;

            Try(() =>
            {
                Undo(pending.HiddenByUs, pending.CloakTurnedOnByUs);
                TryDeleteState();
                log("[HidHide] Restored your DS4 after the last session ended unexpectedly.");
            });
        }
    }

    public void EnsureHidden(string instancePath)
    {
        lock (_lock)
        {
            Try(() =>
            {
                bool registered = hidHide.RegisteredApps().Any(a => string.Equals(a, exePath, StringComparison.OrdinalIgnoreCase));
                bool hidden = hidHide.HiddenDevices().Any(d => SamePath(d, instancePath));
                bool cloaked = hidHide.IsCloaked();
                if (registered && hidden && cloaked) return;

                // Record first: if we die between here and the end, the next launch can undo it
                if (!hidden && !_hiddenByUs.Any(d => SamePath(d, instancePath))) _hiddenByUs.Add(instancePath);
                if (!cloaked) _cloakTurnedOnByUs = true;
                SaveState();

                // Whitelist before hiding, or we'd hide the pad from ourselves
                if (!registered) hidHide.RegisterApp(exePath);
                if (!hidden) hidHide.Hide(instancePath);
                if (!cloaked) hidHide.SetCloak(true);
                log("Hid your real DS4 from games so F1 sees only one controller.");
            });
        }
    }

    /// <summary>Undo what this session changed. Safe to call more than once.</summary>
    public void Restore()
    {
        lock (_lock)
        {
            if (_hiddenByUs.Count == 0 && !_cloakTurnedOnByUs) { TryDeleteState(); return; }
            Try(() =>
            {
                Undo(_hiddenByUs, _cloakTurnedOnByUs);
                _hiddenByUs.Clear();
                _cloakTurnedOnByUs = false;
                TryDeleteState();
            });
        }
    }

    /// <summary>Uninstall: restore everything and remove FullLock from HidHide's whitelist.</summary>
    public void Cleanup()
    {
        RestorePending();
        Restore();
        lock (_lock) Try(() => hidHide.UnregisterApp(exePath));
    }

    private void Undo(IEnumerable<string> hidden, bool cloakTurnedOn)
    {
        var currentlyHidden = hidHide.HiddenDevices();
        foreach (var path in hidden.ToList())
        {
            // Unhide with HidHide's own spelling of the path
            var listed = currentlyHidden.FirstOrDefault(d => SamePath(d, path));
            if (listed != null) hidHide.Unhide(listed);
        }
        if (cloakTurnedOn) hidHide.SetCloak(false);
    }

    private void SaveState() =>
        new HidHideRestoreState(new List<string>(_hiddenByUs), _cloakTurnedOnByUs).Save(statePath);

    private void TryDeleteState()
    {
        try { File.Delete(statePath); } catch (Exception) { /* nothing useful to do */ }
    }

    private void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) { log($"[HidHide] {ex.Message} — games may see your DS4 twice."); }
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
