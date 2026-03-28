namespace ColorChord.NET.API.Timing;

public enum TimeUnit
{
    Minimum,
    Millisecond,
    Frame,
    Sample
}

public struct TimePeriod(float quantity, TimeUnit unit)
{
    public float Quantity = quantity;
    public TimeUnit Unit = unit;

    public static TimePeriod Minimum { get => new(0F, TimeUnit.Minimum); }

    public static bool TryParse(ReadOnlySpan<char> input, out TimePeriod result)
    {
        result = new();
        if (MemoryExtensions.Equals(input, nameof(TimeUnit.Minimum), StringComparison.Ordinal))
        {
            result.Quantity = 1;
            result.Unit = TimeUnit.Minimum;
            return true;
        }

        int SpaceIndex = input.IndexOf(' ');
        if (SpaceIndex != -1)
        {
            if (!float.TryParse(input.Slice(0, SpaceIndex), out result.Quantity)) { return false; }
            if (Enum.TryParse(input.Slice(SpaceIndex + 1), true, out result.Unit)) { return true; }
            {
                if (Enum.TryParse(input.Slice(SpaceIndex + 1, input.Length - SpaceIndex - 2), true, out result.Unit)) { return true; }
            }
        }
        return false;
    }

    public override readonly string ToString() => (this.Unit == TimeUnit.Minimum) ? nameof(TimeUnit.Minimum) : $"{this.Quantity} {this.Unit}";
}