using Fms.Domain.Shared.Enums;

namespace Fms.Domain.Simulation;

public interface IEngineClock
{
    long CurrentTimestampMs { get; }
    DateTime CurrentTimeUtc { get; }
    double TimeScale { get; set; }
    ClockMode Mode { get; set; }
    bool IsPaused { get; }

    void Pause();
    void Resume();
    void SetTimeScale(double scale);
    void AdvanceFixedTick(long deltaMs);
    void StepDes();
    long ScheduleEvent(long triggerTimeMs, Action callback);
    bool CancelEvent(long eventId);
}

public record ScheduledEvent(long TriggerTimeMs, Action Action, long EventId);

public class EngineClock : IEngineClock
{
    private long _currentSimTimeMs;
    private readonly PriorityQueue<ScheduledEvent, long> _eventQueue = new();
    private readonly HashSet<long> _cancelledEventIds = new();
    private long _eventIdCounter = 0;

    public long CurrentTimestampMs => _currentSimTimeMs;
    public DateTime CurrentTimeUtc => DateTime.UnixEpoch.AddMilliseconds(_currentSimTimeMs);
    public double TimeScale { get; set; } = 1.0;
    public ClockMode Mode { get; set; } = ClockMode.RealtimeTick;
    public bool IsPaused { get; private set; } = false;

    public EngineClock(long initialTimeMs = 0)
    {
        _currentSimTimeMs = initialTimeMs > 0 ? initialTimeMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;
    public void SetTimeScale(double scale) => TimeScale = Math.Max(0.01, Math.Min(100.0, scale));

    public long ScheduleEvent(long triggerTimeMs, Action callback)
    {
        long id = Interlocked.Increment(ref _eventIdCounter);
        var evt = new ScheduledEvent(triggerTimeMs, callback, id);
        lock (_eventQueue)
        {
            _eventQueue.Enqueue(evt, triggerTimeMs);
        }
        return id;
    }

    public bool CancelEvent(long eventId)
    {
        lock (_eventQueue)
        {
            return _cancelledEventIds.Add(eventId);
        }
    }

    // 100ms Fixed-Tick 实时推进
    public void AdvanceFixedTick(long deltaMs)
    {
        if (IsPaused) return;

        long stepMs = (long)(deltaMs * TimeScale);
        _currentSimTimeMs += stepMs;

        ExecuteDueEvents(_currentSimTimeMs);
    }

    // 离散事件极速推进 (DES)
    public void StepDes()
    {
        if (IsPaused) return;

        ScheduledEvent? next = null;
        lock (_eventQueue)
        {
            while (_eventQueue.TryDequeue(out var evt, out var triggerTime))
            {
                if (_cancelledEventIds.Remove(evt.EventId))
                {
                    continue;
                }
                _currentSimTimeMs = triggerTime;
                next = evt;
                break;
            }
        }

        next?.Action.Invoke();
    }

    private void ExecuteDueEvents(long upToTimeMs)
    {
        while (true)
        {
            ScheduledEvent? due = null;
            lock (_eventQueue)
            {
                if (_eventQueue.TryPeek(out _, out var triggerTime) && triggerTime <= upToTimeMs)
                {
                    due = _eventQueue.Dequeue();
                }
            }

            if (due == null) break;

            bool isCancelled;
            lock (_eventQueue)
            {
                isCancelled = _cancelledEventIds.Remove(due.EventId);
            }

            if (!isCancelled)
            {
                due.Action.Invoke();
            }
        }
    }
}
