using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Sources;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
namespace ColorChord.NET.Extensions.FFTNoteFinder;

public unsafe partial class FFT : NoteFinderCommon
{
    private readonly IAudioSource AudioSource;

    public override string Name { get; protected init; }

    [ConfigInt("WindowSize", 64, 65536, 1024)]
    public uint WindowSize { get; private set; }

    public uint SampleRate { get; private set; }

    private Thread? ProcessThread;
    private bool KeepGoing = true;

    private double* MainBufferPtr, ResultBufferPtr;
    private uint MainBufferCount, ResultBufferCount;
    private IntPtr FFTPlan;

    private readonly object LockObj = new();

    private Span<double> MainBuffer => new((double*)MainBufferPtr, (int)MainBufferCount);
    private Span<double> ResultBuffer => new((double*)ResultBufferPtr, (int)ResultBufferCount);

    private uint ResultCount => (this.WindowSize / 2) + 1;

    private float AllBinMax = 0.01F, AllBinMaxSmoothed = 0.01F;
    private float[] AllBins;
    public override ReadOnlySpan<float> AllBinValues => this.AllBins;
    public override ReadOnlySpan<float> OctaveBinValues => throw new NotImplementedException();
    public override ReadOnlySpan<Note> Notes => throw new NotImplementedException();
    public override ReadOnlySpan<int> PersistentNoteIDs => throw new NotImplementedException();
    public override int NoteCount => throw new NotImplementedException();
    public override float StartFrequency => throw new NotImplementedException();
    public override int Octaves => throw new NotImplementedException();

    public override int BinsPerOctave => 1;

    private readonly Stopwatch CycleTimer = new();
    private float CycleTimeTicks;
    private uint CycleCount = 0;

    public FFT(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        ColorChordAPI.Configurer.Configure(this, config);
        this.AudioSource = ColorChordAPI.Configurer.FindSource(config) ?? throw new Exception($"{nameof(FFT)} \"{name}\" could not find the audio source to get data from.");
        this.AudioSource.AttachNoteFinder(this);

        SetupBuffers();
        InitFFTW();
    }

    [MemberNotNull(nameof(AllBins))]
    private void InitFFTW()
    {
        uint LenMain = this.WindowSize;
        uint LenResult = this.ResultCount * 2;

        if (this.MainBufferPtr != null) { FFTW_Free(this.MainBufferPtr); }
        if (this.ResultBufferPtr != null) { FFTW_Free(this.ResultBufferPtr); }
        this.MainBufferPtr = FFTW_Malloc(sizeof(double) * LenMain); // TODO: Not thread-safe
        this.ResultBufferPtr = FFTW_Malloc(sizeof(double) * LenResult);
        this.MainBufferCount = LenMain;
        this.ResultBufferCount = LenResult;
        Log.Debug($"Allocated FFT buffers of sizes {LenMain} and {LenResult}.");

        if (this.FFTPlan != IntPtr.Zero) { FFTW_DestroyPlan(this.FFTPlan); }
        const uint FFTW_ESTIMATE = 1 << 6;
        this.FFTPlan = FFTW_MakePlan((int)LenMain, this.MainBufferPtr, this.ResultBufferPtr, FFTW_ESTIMATE);

        this.AllBins = new float[(this.ResultCount / 2) + 1];
    }

    public override void AdjustOutputSpeed(uint period)
    {
        throw new NotImplementedException();
    }

    public override void SetSampleRate(int sampleRate) { this.SampleRate = (uint)sampleRate; }

    public override void Start()
    {
        this.KeepGoing = true;
        this.ProcessThread = new(DoProcessing) { Name = nameof(FFT) };
        ProcessThread.Start();
    }

    private void DoProcessing()
    {
        while (this.KeepGoing)
        {
            InputDataEvent.WaitOne();
            lock (this.LockObj) { Cycle(); }
        }
    }

    private unsafe void Cycle()
    {
        bool MoreBuffers;
        do
        {
            short[]? Buffer = GetBufferToRead(out int NFBufferRef, out uint AddBufferSize, out MoreBuffers);
            if (Buffer == null) { break; }

            // TODO: This assumes incoming buffers will always be smaller than our window size
            for (uint i = 0; i < this.MainBufferCount - AddBufferSize; i++) { this.MainBuffer[(int)i] = this.MainBuffer[(int)(AddBufferSize + i)]; }

            // Copy new data to end of MainBuffer
            int Copied = 0;
            int MainBufferIndex = (int)this.MainBufferCount - (int)AddBufferSize;
            while (Copied + 8 <= AddBufferSize && Avx2.IsSupported)
            {
                Vector128<short> SourceData = Vector128.LoadUnsafe(ref Buffer[Copied]);
                Vector256<int> AsI32 = Avx2.ConvertToVector256Int32(SourceData);
                Vector256<double> AsF64Low = Avx.ConvertToVector256Double(AsI32.GetLower());
                Vector256<double> AsF64High = Avx.ConvertToVector256Double(AsI32.GetUpper());
                AsF64Low.StoreUnsafe(ref MainBuffer[MainBufferIndex]);
                AsF64High.StoreUnsafe(ref MainBuffer[MainBufferIndex + 4]);
                Copied += 8;
                MainBufferIndex += 8;
            }
            while (Copied < AddBufferSize)
            {
                this.MainBuffer[MainBufferIndex++] = (double)Buffer[Copied++];
            }
            Debug.Assert(MainBufferIndex == this.MainBufferCount);
            FinishBufferRead(NFBufferRef);
        } while (MoreBuffers);
    }

    public override unsafe void UpdateOutputs()
    {
        lock (this.LockObj)
        {
            CycleTimer.Restart();
            if (this.FFTPlan == IntPtr.Zero) { return; }
            FFTW_Execute(this.FFTPlan);

            float NewAllBinMax = 0F;
            {
                Vector128<float> IntermediateMax = Vector128<float>.Zero;
                int i = 0;
                while (Avx2.IsSupported && i + 8 <= this.ResultCount)
                {
                    Vector256<double> ComplexA = Vector256.Load(this.ResultBufferPtr + i);
                    Vector256<double> ComplexB = Vector256.Load(this.ResultBufferPtr + (i + 4));
                    Vector256<double> ComplexSquareA = Avx.Multiply(ComplexA, ComplexA);
                    Vector256<double> ComplexSquareB = Avx.Multiply(ComplexB, ComplexB);
                    Vector256<double> Sum = Avx.HorizontalAdd(ComplexSquareA, ComplexSquareB);
                    Vector256<double> SumOrdered = Avx2.Permute4x64(Sum, 0b11011000);
                    Vector256<double> Magnitude = Avx.Sqrt(SumOrdered);
                    Vector128<float> MagnitudeF32 = Avx.ConvertToVector128Single(Magnitude);
                    IntermediateMax = Sse.Max(IntermediateMax, MagnitudeF32);
                    MagnitudeF32.StoreUnsafe(ref this.AllBins[i / 2]);
                    i += 8;
                }
                NewAllBinMax = MathF.Max(MathF.Max(IntermediateMax[0], IntermediateMax[1]), MathF.Max(IntermediateMax[2], IntermediateMax[3]));
                while (i < this.ResultCount)
                {
                    double ValA = this.ResultBuffer[i * 2];
                    double ValB = this.ResultBuffer[(i * 2) + 1];
                    float Magnitude = (float)Math.Sqrt((ValA * ValA) + (ValB * ValB));
                    NewAllBinMax = MathF.Max(NewAllBinMax, Magnitude);
                    this.AllBins[i / 2] = Magnitude;
                    i += 2;
                }
            }
            
            this.AllBinMaxSmoothed = this.AllBinMax < NewAllBinMax ? (this.AllBinMax * 0.8F) + (NewAllBinMax * 0.2F) : (this.AllBinMax * 0.995F) + (NewAllBinMax * 0.005F);
            this.AllBinMax = Math.Max(0.01F, AllBinMaxSmoothed);
            
            {
                float ScaleDivision = this.AllBinMaxSmoothed * 4.5F;
                Vector256<float> ScaleDivisionVec = Vector256.Create(ScaleDivision);
                float CutoffVal = 0F; // = this.AllBinMaxSmoothed * 0.02F;
                Vector256<float> CutoffValVec = Vector256.Create(CutoffVal);
                int j = 0;
                while (Avx.IsSupported && j + 8 <= this.AllBins.Length)
                {
                    Vector256<float> MiddleValues = Vector256.LoadUnsafe(ref this.AllBins[j]);
                    Vector256<float> Scaled = Avx.Divide(MiddleValues, ScaleDivisionVec);
                    Vector256<float> WithCutoff = Avx.Max(Vector256<float>.Zero, Avx.Subtract(Scaled, CutoffValVec));
                    WithCutoff.StoreUnsafe(ref this.AllBins[j]);
                    j += 8;
                }
                while (j < this.AllBins.Length)
                {
                    float Scaled = this.AllBins[j] / ScaleDivision;
                    this.AllBins[j] = MathF.Max(0F, Scaled - CutoffVal);
                    j++;
                }
            }

            CycleTimer.Stop();
        }

        const float TIMER_IIR = 0.97F;
        CycleTimeTicks = (CycleTimeTicks * TIMER_IIR) + ((float)CycleTimer.ElapsedTicks * (1F - TIMER_IIR));
        if (++CycleCount % 500 == 0) { Log.Debug($"{nameof(FFT)} is taking {CycleTimeTicks * 0.1F:F3}us per cycle."); }
    }

    public override void Stop()
    {
        this.KeepGoing = false;
        InputDataEvent.Set();
        ProcessThread?.Join();
    }

    private const string FFTW_DLL_NAME = "libfftw3-3.dll";

    [LibraryImport(FFTW_DLL_NAME, EntryPoint = "fftw_malloc")]
    protected static unsafe partial double* FFTW_Malloc(uint byteCount);

    [LibraryImport(FFTW_DLL_NAME, EntryPoint = "fftw_free")]
    protected static partial void FFTW_Free(double* ptr);

    [LibraryImport(FFTW_DLL_NAME, EntryPoint = "fftw_plan_dft_r2c_1d")]
    protected static partial IntPtr FFTW_MakePlan(int inputCount, double* inputData, double* outputArr, uint flags);

    [LibraryImport(FFTW_DLL_NAME, EntryPoint = "fftw_execute")]
    protected static partial void FFTW_Execute(IntPtr plan);

    [LibraryImport(FFTW_DLL_NAME, EntryPoint = "fftw_destroy_plan")]
    protected static partial void FFTW_DestroyPlan(IntPtr plan);
}
