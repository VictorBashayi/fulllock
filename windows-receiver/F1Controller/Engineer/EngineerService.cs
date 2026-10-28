using System;
using System.Collections.Generic;
using System.Linq;

namespace F1Controller.Engineer;

/// <summary>
/// Runs the engineer on each race snapshot: rules propose calls, the dispatcher picks
/// at most one to send. A new session (new UID, or session time going back more than
/// <see cref="EngineerThresholds.ReorderToleranceSeconds"/> on a restart) clears all state, and the first <see cref="EngineerThresholds.WarmupSeconds"/>
/// of a session stay silent so the first frames don't trigger a burst of calls.
/// </summary>
public sealed class EngineerService
{
    private readonly EngineerThresholds _t;
    private readonly EngineerRules _rules;
    private readonly RadioDispatcher _dispatcher;
    private readonly Action<RadioCall> _send;
    private readonly Action<Exception>? _onError;
    private readonly Func<int>? _level;
    private readonly Action<string>? _log;
    private RaceSnapshot? _previous;
    private float _sessionStart;

    /// <param name="onError">
    /// Receives any failure inside the engineer. Errors never propagate: the caller is the
    /// telemetry receive loop, which also feeds the phone's dashboard.
    /// </param>
    /// <param name="level">The phone's chattiness (0 Off … 3 Talkative); Balanced when not given.</param>
    /// <param name="log">Receives log-only lines (never sent to the phone), e.g. per-lap tyre temperatures.</param>
    public EngineerService(EngineerThresholds t, Action<RadioCall> send, Action<Exception>? onError = null,
        Func<int>? level = null, Action<string>? log = null)
    {
        _t = t;
        _rules = new EngineerRules(t);
        _dispatcher = new RadioDispatcher(t);
        _send = send;
        _onError = onError;
        _level = level;
        _log = log;
    }

    public void OnSnapshot(RaceSnapshot s)
    {
        try
        {
            Process(s);
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
    }

    private void Process(RaceSnapshot s)
    {
        // A packet that arrived out of order steps back a fraction of a second: skip it.
        // A bigger step back is a restart (the clock back near zero) or a flashback.
        bool restart = _previous == null || s.SessionUid != _previous.SessionUid;
        if (!restart && s.SessionTime < _previous!.SessionTime)
        {
            if (_previous.SessionTime - s.SessionTime <= _t.ReorderToleranceSeconds)
                return;
            if (s.SessionTime < _t.RestartMaxSessionSeconds)
                restart = true;
            else
                _previous = null;  // flashback: keep what the engineer learned (best lap, tyre mutes),
                                   // but don't compare across the jump, so no lap completes by it
        }

        if (restart)
        {
            _rules.Reset();
            _dispatcher.Reset();
            _previous = null;
            _sessionStart = s.SessionTime;
        }

        // Evaluate even while warming up, so rule memory reflects the current state
        IEnumerable<RadioCall> calls = _rules.Evaluate(_previous, s, _level?.Invoke() ?? 2);
        // In the pit lane only critical news gets through (and the in-lap's report, which ends there)
        if (s.Race.Timing.PitStatus != 0)
            calls = calls.Where(c => c.Priority == RadioPriority.Critical || c.Key == "lap-report").ToList();
        foreach (var line in _rules.TakeLogLines())
            _log?.Invoke(line);
        _previous = s;
        if (s.SessionTime - _sessionStart < _t.WarmupSeconds) return;

        // Past the chequered flag (or out of the race): nothing left to say
        if (s.Race.Timing.Finished)
        {
            _dispatcher.Reset();
            return;
        }

        _dispatcher.Enqueue(calls, s.SessionTime);
        if (_dispatcher.Next(s.SessionTime) is RadioCall call)
            _send(call);
    }
}
