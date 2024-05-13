using ColorChord.NET.API.Sources;

namespace ColorChord.NET.API.Utility;

public class GenericTimingSourceSingle : ITimingSource
{
    private TimingReceiver? Receiver;
    private Timer? Timer;

    public void AddTimingReceiver(TimingReceiver receiver, float period)
    {
        if (period < 0) { throw new Exception("Generic timer doesn't support sample count-based timing"); }
        if (this.Receiver != null && this.Receiver != receiver) { throw new Exception($"Cannot add more than one receiver to ${nameof(GenericTimingSourceSingle)}, use ${nameof(GenericTimingSourceMulti)} for this purpose."); }
        this.Receiver = receiver;
        this.Timer = new(TimerCallback, null, 0, (int)MathF.Round(period * 1000));
    }

    private void TimerCallback(object? unused) => this.Receiver?.Invoke();

    public void RemoveTimingReceiver(TimingReceiver receiver)
    {
        this.Receiver = null;
        this.Timer?.Dispose();
        this.Timer = null;
    }
}

public class GenericTimingSourceMulti : ITimingSource
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
