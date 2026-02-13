using ColorChord.NET.API.NoteFinder;

namespace ColorChord.NET.Tests.Benchmarks.FakeData;

public class FakeNoteFinder : NoteFinderCommon
{
    private const int RANDOM_SEED = 0x53EB5;
    public override string Name { get; protected init; }

    private readonly float[] P_AllBinValues, P_OctaveBinValues;
    private readonly Note[] P_Notes;
    private readonly int[] P_NoteIDs;
    private readonly Random Rand;

    public override ReadOnlySpan<float> AllBinValues => this.P_AllBinValues;
    public override ReadOnlySpan<float> OctaveBinValues => this.P_OctaveBinValues;
    public override ReadOnlySpan<Note> Notes => this.P_Notes;
    public override ReadOnlySpan<int> PersistentNoteIDs => this.P_NoteIDs;

    public override int NoteCount => 12;
    public override int BinsPerOctave => 12;
    public override int Octaves => 5;
    public override float StartFrequency => 55F;

    public FakeNoteFinder(string name)
    {
        this.Name = name;
        this.P_AllBinValues = new float[this.BinsPerOctave * this.Octaves];
        this.P_OctaveBinValues = new float[this.BinsPerOctave];
        this.P_Notes = new Note[this.NoteCount];
        this.P_NoteIDs = new int[this.NoteCount];
        this.Rand = new(RANDOM_SEED);
    }

    public override void AdjustOutputSpeed(uint period) { throw new NotImplementedException(); }
    public override void SetSampleRate(int sampleRate) { throw new NotImplementedException(); }
    public override void Start() { }
    public override void Stop() { }

    public override void UpdateOutputs()
    {
        for (int i = 0; i < this.P_OctaveBinValues.Length; i++) { this.P_OctaveBinValues[i] = 0; }
        for (int i = 0; i < this.P_AllBinValues.Length; i++)
        {
            float NewVal = 2F * MathF.Max(0F, this.Rand.NextSingle() - 0.5F); // 50% chance of 0, otherwise 0.0~1.0
            this.P_AllBinValues[i] = NewVal;
            this.P_OctaveBinValues[i % BinsPerOctave] += NewVal;
        }
        for (int i = 0; i < this.NoteCount; i++)
        {
            float NewVal = 2F * MathF.Max(0F, this.Rand.NextSingle() - 0.5F); // 50% chance of 0, otherwise 0.0~1.0
            float NewPos = this.Rand.NextSingle() * this.NoteCount;
            this.P_Notes[i] = new()
            {
                Position = NewPos,
                Amplitude = NewVal,
                AmplitudeFiltered = NewVal,
                AmplitudeFinal = NewVal
            };
            this.P_NoteIDs[i] = NewVal > 0F ? Rand.Next(this.NoteCount) : -1;
        }
    }

}
