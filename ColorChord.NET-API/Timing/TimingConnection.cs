namespace ColorChord.NET.API.Timing;

public class TimingConnection
{
    public ITimingSource Source { get; private init; }
    public ITimingReceiver Receiver { get; private init; }
    public TimePeriod RequestedPeriod { get; private init; }
    public bool IsSynchronous { get; private init; }
    public bool IsOwnGeneric { get; private init; }

    public TimingConnection(ITimingSource source, TimePeriod period, ITimingReceiver receiver, bool isSynchronous = false, bool isGeneric = false)
    {
        this.Source = source;
        this.RequestedPeriod = period;
        this.Receiver = receiver;
        this.IsSynchronous = isSynchronous;
        this.IsOwnGeneric = isGeneric;

        this.Source.AddTimingReceiver(this);
    }

    public TimingConnection(ITimingSource source, ReadOnlySpan<char> periodText, ITimingReceiver receiver, bool isSynchronous = false, bool isGeneric = false)
    {
        this.Source = source;
        if (!TimePeriod.TryParse(periodText, out TimePeriod ParsedPeriod)) { throw new Exception($"'{periodText}' could not be parsed as a period"); }
        this.RequestedPeriod = ParsedPeriod;
        this.Receiver = receiver;
        this.IsSynchronous = isSynchronous;
        this.IsOwnGeneric = isGeneric;

        this.Source.AddTimingReceiver(this);
    }

    public void Remove() => this.Source.RemoveTimingReceiver(this);
}
