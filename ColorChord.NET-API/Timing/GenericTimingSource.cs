namespace ColorChord.NET.API.Timing;

public class GenericTimingSourceSingle : ITimingSource
{
    private TimingConnection? Receiver;
    private Timer? Timer;

    public void AddTimingReceiver(TimingConnection receiver)
    {
        if (receiver.RequestedPeriod.Unit != TimeUnit.Millisecond) { throw new Exception("Generic timer only supports millisecond-based time intervals"); }
        if (this.Receiver != null && this.Receiver != receiver) { throw new Exception($"Cannot add more than one receiver to ${nameof(GenericTimingSourceSingle)}, use ${nameof(GenericTimingSourceMulti)} for this purpose."); }
        this.Receiver = receiver;
        this.Timer = new(TimerCallback, null, 0, Math.Max(1, (int)MathF.Round(receiver.RequestedPeriod.Quantity)));
    }

    private void TimerCallback(object? unused) => this.Receiver?.Receiver.TimingCallback(this);

    public void RemoveTimingReceiver(TimingConnection receiver)
    {
        this.Receiver = null;
        this.Timer?.Dispose();
        this.Timer = null;
    }
}

public class GenericTimingSourceMulti : ITimingSource
{
    private readonly Dictionary<TimingConnection, Timer> Timers = new();

    public void AddTimingReceiver(TimingConnection receiver)
    {
        if (receiver.RequestedPeriod.Unit != TimeUnit.Millisecond) { throw new Exception("Generic timer only supports millisecond-based time intervals"); }
        Timer Timer = new(TimerCallback, receiver, 0, Math.Max(1, (int)MathF.Round(receiver.RequestedPeriod.Quantity)));
        Timers.Add(receiver, Timer);
    }

    private void TimerCallback(object? receiver) => (receiver as TimingConnection)?.Receiver.TimingCallback(this);

    public void RemoveTimingReceiver(TimingConnection receiver)
    {
        Timers[receiver].Dispose();
        Timers.Remove(receiver);
    }
}
