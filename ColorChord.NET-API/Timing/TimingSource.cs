namespace ColorChord.NET.API.Timing;

public class TimingSource(object parent, Converter<TimePeriod, TimePeriod> converter)
{
    private TimingReceiverData[] TimingReceivers = Array.Empty<TimingReceiverData>();
    private readonly Converter<TimePeriod, TimePeriod> Converter = converter;
    private readonly object Parent = parent;

    private static Converter<TimePeriod, TimePeriod>? BF_Passthrough;
    public static Converter<TimePeriod, TimePeriod> Passthrough { get { BF_Passthrough ??= (i => i); return BF_Passthrough; } }

    public void AddReceiver(TimingConnection receiver)
    {
        TimingReceiverData[] OldData = this.TimingReceivers;
        TimingReceiverData[] NewData = new TimingReceiverData[OldData.Length + 1];
        Array.Copy(OldData, NewData, OldData.Length);
        NewData[^1] = new()
        {
            Receiver = receiver,
            InternalPeriod = this.Converter(receiver.RequestedPeriod),
            CurrentIncrement = 0F
        };
        Log.Debug($"{this.Parent.GetType()} has new timing receiver {receiver.Receiver.GetType()} at {receiver.RequestedPeriod} ({NewData[^1].InternalPeriod})");

        this.TimingReceivers = NewData;
    }

    /// <summary> Removes a receiver from the internal list. </summary>
    /// <param name="receiver"> The receiver to remove </param>
    /// <returns> Whether any receivers still remain </returns>
    public bool RemoveReceiver(TimingConnection receiver)
    {
        int Index = Array.FindIndex(this.TimingReceivers, x => x.Receiver == receiver);
        if (Index < 0) { return this.TimingReceivers.Length > 0; }

        if (this.TimingReceivers.Length == 1) // Last receiver
        {
            this.TimingReceivers = Array.Empty<TimingReceiverData>();
            return false;
        }

        TimingReceiverData[] OldData = this.TimingReceivers;
        TimingReceiverData[] NewData = new TimingReceiverData[OldData.Length - 1];
        if (Index > 0) { Array.Copy(OldData, 0, NewData, 0, Index); } // Copy items on the left of the removed item
        else if (Index < OldData.Length - 1) { Array.Copy(OldData, Index + 1, NewData, Index, OldData.Length - Index - 1); } // Copy items on the right of the removed item

        this.TimingReceivers = NewData;
        return true;
    }

    public void Increment(float timePassed, Action? callbackOnFirst = null)
    {
        bool DidCallback = false;
        for (int i = 0; i < this.TimingReceivers.Length; i++)
        {
            ref TimingReceiverData Receiver = ref this.TimingReceivers[i];
            if (Receiver.Receiver.RequestedPeriod.Unit == TimeUnit.Minimum)
            {
                if (!DidCallback) { callbackOnFirst?.Invoke(); DidCallback = true; }
                Receiver.Receiver.Receiver.TimingCallback(this.Parent);
            }
            else
            {
                Receiver.CurrentIncrement += timePassed;
                if (Receiver.CurrentIncrement >= Receiver.InternalPeriod.Quantity)
                {
                    if (!DidCallback) { callbackOnFirst?.Invoke(); DidCallback = true; }
                    Receiver.Receiver.Receiver.TimingCallback(this.Parent);
                    Receiver.CurrentIncrement -= Receiver.InternalPeriod.Quantity;
                    if (Receiver.InternalPeriod.Quantity != 0 && Receiver.CurrentIncrement > Receiver.InternalPeriod.Quantity * 16)
                    {
                        Log.Warn($"{this.Parent?.GetType().ToString() ?? "(unknown)"} has a timing receiver that is falling behind, the receiver {Receiver.Receiver} has a period of {Receiver.InternalPeriod}, which is too short to effectively call.");
                    }
                }
            }
        }
    }

    public void RecalculatePeriods()
    {
        for (int i = 0; i < this.TimingReceivers.Length; i++)
        {
            this.TimingReceivers[i].InternalPeriod = this.Converter(this.TimingReceivers[i].Receiver.RequestedPeriod);
            this.TimingReceivers[i].CurrentIncrement = 0F;
        }
    }

    private struct TimingReceiverData
    {
        /// <summary> The receiver to call whenever this event occurs. </summary>
        public TimingConnection Receiver { get; init; }

        /// <summary> The period on which to send this event, in locally used units. </summary>
        public TimePeriod InternalPeriod { get; set; }

        /// <summary> How how much time has passed since this callback was last dispatched, in units of <see cref="InternalPeriod.Unit"/>. </summary>
        public float CurrentIncrement { get; set; }
    }
}
