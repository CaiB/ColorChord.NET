using ColorChord.NET.NoteFinder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET.Tests.UnitTests;

[TestClass]
public class Gen2DFTTests
{
    private Gen2NoteFinderDFT DFTInst;

    [TestMethod]
    public void Init()
    {
        this.DFTInst = new(4, 24, 48000, 110F, 0F, 1F, null);
    }

    private void AssertZeroOutput()
    {
        List<int> NonzeroBinIndices = this.DFTInst.RawBinMagnitudes.Select((value, index) => value > 0 ? index : -1).Where(x => x >= 0).ToList();
        Assert.AreEqual(0, NonzeroBinIndices.Count, $"{NonzeroBinIndices.Count} bin(s) has non-zero values when zeroes were expected: [{string.Join(',', NonzeroBinIndices)}]");
    }

    [TestMethod]
    public void SineInput()
    {
        Init();
        float InputFrequency = 110F;
        short[] InputSamples = new short[5000];
        int InputAmplitude = short.MaxValue;

        uint SampleRate = this.DFTInst.SampleRate;
        for (int i = 0; i < InputSamples.Length; i++)
        {
            InputSamples[i] = (short)MathF.Round(InputAmplitude * MathF.Sin(2F * MathF.PI * InputFrequency * i / SampleRate));
        }

        this.DFTInst.AddAudioData(InputSamples);
        this.DFTInst.CalculateOutput();
        
        int ExpectedOutputBin = (int)MathF.Round(MathF.Log2(InputFrequency / this.DFTInst.StartFrequency) * this.DFTInst.BinsPerOctave);

        Console.Write($"Start freq {this.DFTInst.StartFrequency}Hz, input freq {InputFrequency}Hz, {this.DFTInst.BinsPerOctave} BPO => expecting output in bin {ExpectedOutputBin}");
        float[] OutputData = this.DFTInst.RawBinMagnitudes;

        int BinWithMaxVal = OutputData.ToList().IndexOf(OutputData.Max());
        Assert.AreEqual(ExpectedOutputBin, BinWithMaxVal, "Incorrect bin has maximum response value");
    }

    [TestMethod]
    public void InputSweep()
    {
        Init();
        float InputFrequencyStart = 440F;
        float InputFrequencyEnd = 880F;
        float InputFrequencyStep = 0.25F;
        float InputAmplitude = 8000;

        short[] Silence = new short[65536];
        short[] InputData = new short[8192];
        float SampleRate = this.DFTInst.SampleRate;

        using (StreamWriter Stream = new("Gen2DFTTests_InputSweep.csv"))
        {
            Stream.Write("Freq,");
            Stream.WriteLine(string.Join(',', this.DFTInst.RawBinFrequencies));
            float InputFrequency = InputFrequencyStart;
            while (InputFrequency <= InputFrequencyEnd)
            {
                float Coeff = MathF.Tau * InputFrequency / SampleRate;
                for (int i = 0; i < InputData.Length; i++)
                {
                    InputData[i] = (short)MathF.Round(InputAmplitude * MathF.Sin(Coeff * i));
                }

                this.DFTInst.AddAudioData(InputData);
                this.DFTInst.CalculateOutput();

                Stream.Write(InputFrequency);
                Stream.Write(',');
                Stream.WriteLine(string.Join(',', this.DFTInst.RawBinMagnitudes));
                float MaxValData = this.DFTInst.RawBinMagnitudes.Max();

                this.DFTInst.Clear();
                this.DFTInst.CalculateOutput();
                float MaxValAfter = this.DFTInst.RawBinMagnitudes.Max();
                AssertZeroOutput();

                InputFrequency += InputFrequencyStep;
            }
        }
    }

    [TestMethod]
    public void TestVecSine()
    {
        const float WAVE_LOCATION = 0.58F;
        const float TEST_RANGE = 0.60F;
        const int TEST_STEPS = 200;
        const short TABLE_AMPLITUDE = 4095;
        const float ALLOWED_ERROR = 1.0F; // %

        Gen2NoteFinderDFT.GetSine(new() { NCLeft = 9725, NCRight = 52764 }, false);

        for (int i = 0; i < TEST_STEPS; i++)
        {
            float WaveLocHere = WAVE_LOCATION - TEST_RANGE + TEST_RANGE * 2 * ((float)i / TEST_STEPS); // From (WAVE_LOCATION - TEST_RANGE) to (WAVE_LOCATION + TEST_RANGE)
            Vector256<ushort> Position = Vector256.Create((ushort)(WaveLocHere * 65535.0F)); // yes this is dumb but meh
            short LUTSine = Gen2NoteFinderDFT.GetSine256(Position)[0];

            float RealSine = TABLE_AMPLITUDE * MathF.Sin(WaveLocHere * MathF.Tau);
            short RealSineRounded = (short)MathF.Round(RealSine);
            float Difference = 100F * (LUTSine - RealSineRounded) / TABLE_AMPLITUDE;
            Console.WriteLine($"{i}:{WaveLocHere:F5},0x{Position[0]:X4},{LUTSine},{RealSineRounded},{Difference:+0.00;-0.00;0.00}%");
            Assert.IsTrue(Difference > -ALLOWED_ERROR, $"The interpolated LUT sine output {LUTSine} was too small compared to the expected value of {RealSine}");
            Assert.IsTrue(Difference < ALLOWED_ERROR, $"The interpolated LUT sine output {LUTSine} was too large compared to the expected value of {RealSine}");
        }
    }

    [TestMethod]
    public void TestVecSineAll()
    {
        const short TABLE_AMPLITUDE = 4095;
        const float ALLOWED_ERROR = 1.0F; // %
        Vector256<ushort> Incrementing = Vector256.Create((ushort)0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);

        int WorstIndex = 0;
        float WorstError = 0F;
        short WorstResult = 0, WorstExpected = 0;

        for (int i = 0; i < ushort.MaxValue; i += 16)
        {
            Vector256<ushort> Position = Vector256.Create((ushort)i) + Incrementing;
            Vector256<short> LUTSines = Gen2NoteFinderDFT.GetSine256(Position);

            for (int j = 0; j < 16; j++)
            {
                int InnerIndex = i + j;
                float RealSine = TABLE_AMPLITUDE * MathF.Sin(InnerIndex / 65536F * MathF.Tau);
                short RealSineRounded = (short)MathF.Round(RealSine);
                float Difference = 100F * (LUTSines[j] - RealSineRounded) / TABLE_AMPLITUDE;
                if (Math.Abs(WorstError) < Math.Abs(Difference))
                {
                    WorstIndex = InnerIndex;
                    WorstError = Difference;
                    WorstExpected = RealSineRounded;
                    WorstResult = LUTSines[j];
                }
                Assert.IsTrue(Difference > -ALLOWED_ERROR, $"The interpolated LUT sine output {LUTSines[j]} was too small compared to the expected value of {RealSine} at index {InnerIndex}");
                Assert.IsTrue(Difference < ALLOWED_ERROR, $"The interpolated LUT sine output {LUTSines[j]} was too large compared to the expected value of {RealSine} at index {InnerIndex}");
            }
        }
        Console.WriteLine($"Worst point was at i={WorstIndex} ({WorstIndex / 65536F:F5}): got {WorstResult}, should be {WorstExpected}, off by {WorstError:+0.00;-0.00;0.00}%");
    }

    [TestMethod]
    public void TestVecWrapRead()
    {
        ushort BUFFER_LEN = 48;
        short[] DataBuffer = new short[BUFFER_LEN + 16];
        for (int i = 0; i < DataBuffer.Length; i++) { DataBuffer[i] = (short)(i >= BUFFER_LEN ? -i : i); }

        MethodInfo TargetMethod = typeof(Gen2NoteFinderDFT).GetMethod("ReadArrayWraparound", BindingFlags.Static | BindingFlags.NonPublic);

        for (int h = BUFFER_LEN - 15; h < BUFFER_LEN; h++)
        {
            short[] Expected = new short[16];
            for (int i = 0; i < Expected.Length; i++) { Expected[i] = DataBuffer[(h + i) % BUFFER_LEN]; }

            object ResultObj = TargetMethod.Invoke(null, [DataBuffer, BUFFER_LEN, (uint)h]);
            Vector256<short> Result = ResultObj as Vector256<short>? ?? throw new Exception("Could not get result from method call");

            for (int i = 0; i < Expected.Length; i++) { Assert.AreEqual(Expected[i], Result[i], $"Item at index {i} was not as expected when reading from {h}."); }
            Console.WriteLine($"Finished checking offset {h} ({BUFFER_LEN - h} from back + {16 - (BUFFER_LEN - h)} from front)");
        }
    }
}
