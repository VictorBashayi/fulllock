using System;
using System.Collections.Generic;

namespace F1Controller.Pad.HidHide;

/// <summary>The HidHide operations FullLock uses. Implementations throw <see cref="HidHideException"/> on failure.</summary>
public interface IHidHide
{
    bool IsCloaked();
    IReadOnlyList<string> HiddenDevices();
    IReadOnlyList<string> RegisteredApps();
    void RegisterApp(string exePath);
    void UnregisterApp(string exePath);
    void Hide(string instancePath);
    void Unhide(string instancePath);
    void SetCloak(bool on);
}

public sealed class HidHideException(string message) : Exception(message);
