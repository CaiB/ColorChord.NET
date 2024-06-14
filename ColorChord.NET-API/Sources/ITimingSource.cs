namespace ColorChord.NET.API.Sources;

public interface ITimingSource
{
    /// <summary> Adds a receiver for callbacks at regular intervals. </summary>
    /// <param name="receiver"> The receiver callback method </param>
    /// <param name="period"> The period, in seconds (positive numbers), or samples (negative numbers) </param>
    public void AddTimingReceiver(TimingReceiver receiver, float period);

    public void RemoveTimingReceiver(TimingReceiver receiver);
}

public delegate void TimingReceiver();