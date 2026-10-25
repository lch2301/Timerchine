namespace IntervalTimer;

internal enum TimerState { Idle, Running, Alarming, Stopped }

internal sealed class TimerSession(TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private long startedAt;
    private int durationSeconds;
    private double stoppedRemaining;
    public TimerState State { get; private set; }
    public bool IsActive => State is TimerState.Running or TimerState.Alarming;
    public int DurationSeconds => durationSeconds;
    public double RemainingExact => State == TimerState.Alarming ? 0 :
        State == TimerState.Stopped ? stoppedRemaining : State == TimerState.Idle ? durationSeconds :
        Math.Max(0, durationSeconds - clock.GetElapsedTime(startedAt).TotalSeconds);
    public int RemainingSeconds => (int)Math.Ceiling(RemainingExact);
    public double ElapsedSeconds => Math.Clamp(durationSeconds - RemainingExact, 0, durationSeconds);

    public void Start(int seconds)
    {
        durationSeconds = Math.Max(0, seconds);
        startedAt = clock.GetTimestamp();
        State = TimerState.Running;
    }

    public bool Tick()
    {
        if (State != TimerState.Running || RemainingSeconds > 0) return false;
        State = TimerState.Alarming;
        return true;
    }

    public void Stop() => State = TimerState.Idle;

    public void StopCurrent()
    {
        stoppedRemaining = RemainingExact;
        State = TimerState.Stopped;
    }

    public void Reset(int seconds)
    {
        durationSeconds = Math.Max(0, seconds);
        stoppedRemaining = durationSeconds;
        State = TimerState.Idle;
    }

    public static string Format(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }
}
