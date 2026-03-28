namespace ColorChord.NET.API.Timing;

public interface ITimingSource
{
    /// <summary> Adds a receiver for callbacks at regular intervals. </summary>
    /// <param name="receiver"> The receiver callback information </param>
    public void AddTimingReceiver(TimingConnection receiver);

    public void RemoveTimingReceiver(TimingConnection receiver);
}