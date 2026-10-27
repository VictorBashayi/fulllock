using System;

namespace F1Controller.Engineer;

/// <summary>
/// Runs the engineer on each race snapshot: rules propose calls, the dispatcher picks
/// at most one to send. A new session (new UID, or session time going backwards on a
/// restart) clears all state, and the first <see cref="EngineerThresholds.WarmupSeconds"/>
/// of a session stay silent so the first frames don't trigger a burst of calls.
/// </summary>
public sealed class EngineerService
{
    private readonly EngineerThresholds _t;
    private readonly EngineerRules _rules;
    private readonly RadioDispatcher _dispatcher;
    private readonly Action<RadioCall> _send;
    private RaceSnapshot? _previous;
    private float _sessionStart;

    public EngineerService(EngineerThresholds t, Action<RadioCall> send)
    {
        _t = t;
        _rules = new EngineerRules(t);
        _dispatcher = new RadioDispatcher(t);
        _send = send;
    }

    public void OnSnapshot(RaceSnapshot s)
    {
        if (_previous == null || s.SessionUid != _previous.SessionUid || s.SessionTime < _previous.SessionTime)
        {
            _rules.Reset();
            _dispatcher.Reset();
            _previous = null;
            _sessionStart = s.SessionTime;
        }

        // Evaluate even while warming up, so rule memory reflects the current state
        var calls = _rules.Evaluate(_previous, s);
        _previous = s;
        if (s.SessionTime - _sessionStart < _t.WarmupSeconds) return;

        _dispatcher.Enqueue(calls, s.SessionTime);
        if (_dispatcher.Next(s.SessionTime) is RadioCall call)
            _send(call);
    }
}
