using ColorChord.NET.API.Sources;

namespace ColorChord.NET.API.Utility;

public class GenericTimingSource : ITimingSource
{
    private readonly Dictionary<TimingReceiver, Timer> Timers = new();

    public void AddTimingReceiver(TimingReceiver receiver, float period)
    {
        if (period < 0) { throw new Exception("Generic timer doesn't support sample count-based timing"); }
        Timer Timer = new(TimerCallback, receiver, 0, (int)MathF.Round(period * 1000));
        Timers.Add(receiver, Timer);
    }

    private void TimerCallback(object? receiver) => (receiver as TimingReceiver)?.Invoke();

    public void RemoveTimingReceiver(TimingReceiver receiver)
    {
        Timers[receiver].Dispose();
        Timers.Remove(receiver);
    }
}
