using System.Collections.Generic;

namespace F1Controller.Engineer;

/// <summary>
/// Radio discipline: decides which queued call goes out next. Calls are spaced
/// <see cref="EngineerThresholds.MinSpacingSeconds"/> apart (critical calls skip the
/// queue and the spacing), each key has a cooldown, and news that waited too long is
/// dropped. All times are game session time, so pausing doesn't burn cooldowns.
/// </summary>
public sealed class RadioDispatcher
{
    private sealed record Queued(RadioCall Call, float At);

    private readonly EngineerThresholds _t;
    private readonly List<Queued> _queue = new();
    private readonly Dictionary<string, float> _sentAt = new();
    private float? _lastSent;

    public RadioDispatcher(EngineerThresholds t) => _t = t;

    public void Enqueue(IEnumerable<RadioCall> calls, float now)
    {
        foreach (var call in calls)
        {
            if (_sentAt.TryGetValue(call.Key, out float sent) && now - sent < _t.CooldownFor(call.Key))
                continue;
            if (_queue.Exists(q => q.Call.Key == call.Key))
                continue;
            _queue.Add(new Queued(call, now));
        }
    }

    /// <summary>The call to send now, or null if nothing is due.</summary>
    public RadioCall? Next(float now)
    {
        _queue.RemoveAll(q => q.Call.Priority != RadioPriority.Critical && now - q.At > _t.StaleSeconds);

        Queued? best = null;
        foreach (var q in _queue)  // queue is in arrival order, so ties keep the oldest
            if (best == null || q.Call.Priority > best.Call.Priority)
                best = q;
        if (best == null) return null;

        bool spaced = _lastSent is not float last || now - last >= _t.MinSpacingSeconds;
        if (!spaced && best.Call.Priority != RadioPriority.Critical) return null;

        _queue.Remove(best);
        _sentAt[best.Call.Key] = now;
        _lastSent = now;
        return best.Call;
    }

    public void Reset()
    {
        _queue.Clear();
        _sentAt.Clear();
        _lastSent = null;
    }
}
