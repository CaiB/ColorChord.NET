using System;
using System.IO;
using System.Reflection;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Serialization;
using System.Text;
using ColorChord.NET.NoteFinder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTK.Graphics.OpenGL.GL;

namespace ColorChordTests;

[TestClass]
public class ShinNoteFinderTest
{
    public ShinNoteFinderTest()
    {
        ShinNoteFinder a = new("Testing NoteFinder", new());
    }

    [TestMethod]
    public void TestInit()
    {
        ShinNoteFinderDFT.Reconfigure();
    }
    
    [TestMethod]
    public void TestDataAdd()
    {
        ShinNoteFinderDFT.AddAudioData(new short[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 }, 32);
    }

    [TestMethod]
    public void TestSineInput()
    {
        float Frequency = 1046.502F;
        float Omega = Frequency * MathF.Tau / ShinNoteFinderDFT.SampleRate;

        short[] AudioData = new short[2500];
        for (int i = 0; i < AudioData.Length; i++)
        {
            float Sin = MathF.Sin(i * Omega);
            AudioData[i] = (short)MathF.Round(200 * Sin);
        }

        ShinNoteFinderDFT.AddAudioData(AudioData, (uint)AudioData.Length);
        ShinNoteFinderDFT.CalculateOutput();
    }

    [TestMethod]
    public void TestSineInputAndOutputEachStep()
    {
        float Frequency = 622.2540F; // 1046.502F;
        float Omega = Frequency * MathF.Tau / ShinNoteFinderDFT.SampleRate;
        const int SAMPLE_COUNT = 16384;

        string[] Output = new string[SAMPLE_COUNT];

        for (int s = 0; s < SAMPLE_COUNT; s++)
        {
            float Sin = MathF.Sin(s * Omega);
            short AudioData = (short)MathF.Round(200 * Sin);
            ShinNoteFinderDFT.AddAudioData(new short[] { AudioData }, 1);
            ShinNoteFinderDFT.CalculateOutput();

            StringBuilder Line = new();
            for (int b = 96; b < 120; b++)
            {
                Line.Append(ShinNoteFinderDFT.RawBinMagnitudes[b]);
                Line.Append(',');
            }
            Output[s] = Line.ToString();
        }

        File.WriteAllLines("ProgressiveOutputData.csv", Output);
    }

    [TestMethod]
    public void TestSineInputAndOutputEachStep256()
    {
        float Frequency = 622.2540F; //1046.502F;
        float Omega = Frequency * MathF.Tau / ShinNoteFinderDFT.SampleRate;
        const int SAMPLE_COUNT = 16384;

        string[] Output = new string[SAMPLE_COUNT / 16];

        for (int s = 0; s < SAMPLE_COUNT / 16; s++)
        {
            short[] Samples = new short[16];
            for (int t = 0; t < 16; t++)
            {
                float Sin = MathF.Sin(((s * 16) + t) * Omega);
                short AudioData = (short)MathF.Round(200 * Sin);
                Samples[t] = AudioData;
            }
            ShinNoteFinderDFT.AddAudioData(Samples, (uint)Samples.Length);
            ShinNoteFinderDFT.CalculateOutput();

            StringBuilder Line = new();
            for (int b = 96; b < 120; b++)
            {
                Line.Append(ShinNoteFinderDFT.RawBinMagnitudes[b]);
                Line.Append(',');
            }
            Output[s] = Line.ToString();
        }

        File.WriteAllLines("ProgressiveOutputData256.csv", Output);
    }

    [TestMethod]
    public void TestSineInputAndOutputEachStepSimultaneous()
    {
        float Frequency = 622.2540F; //1046.502F;
        float Omega = Frequency * MathF.Tau / ShinNoteFinderDFT.SampleRate;
        const int SAMPLE_COUNT = 16384;

        string[] Output = new string[SAMPLE_COUNT / 16];

        for (int s = 0; s < SAMPLE_COUNT / 16; s++)
        {
            short[] Samples = new short[16];
            for (int t = 0; t < 16; t++)
            {
                float Sin = MathF.Sin(((s * 16) + t) * Omega);
                short AudioData = (short)MathF.Round(200 * Sin);
                Samples[t] = AudioData;
                ShinNoteFinderDFT.AddAudioData(new short[] { AudioData }, 1);
            }
            ShinNoteFinderDFT.AddAudioData(Samples, (uint)Samples.Length);
            ShinNoteFinderDFT.CalculateOutput();

            StringBuilder Line = new();
            for (int b = 96; b < 120; b++)
            {
                Line.Append(ShinNoteFinderDFT.RawBinMagnitudes[b]);
                Line.Append(',');
            }
            Output[s] = Line.ToString();
        }

        File.WriteAllLines("ProgressiveOutputData256.csv", Output);
    }

    [TestMethod]
    public void TestSineThenSilentInput()
    {
        float Frequency = 1046.502F;
        float Omega = Frequency * MathF.Tau / ShinNoteFinderDFT.SampleRate;

        short[] AudioData = new short[4096];
        for (int i = 0; i < AudioData.Length; i++)
        {
            float Sin = MathF.Sin(i * Omega);
            AudioData[i] = (short)MathF.Round(short.MaxValue * Sin);
        }

        ShinNoteFinderDFT.AddAudioData(AudioData, (uint)AudioData.Length);
        ShinNoteFinderDFT.CalculateOutput();
        ShinNoteFinderDFT.AddAudioData(new short[8192], 8192);
        ShinNoteFinderDFT.CalculateOutput();
    }

    [TestMethod]
    public void TestDCInput()
    {
        const short DC_VALUE = 500;
        short[] AudioData = new short[4096];
        for (int i = 0; i < AudioData.Length; i++) { AudioData[i] = DC_VALUE; }
        ShinNoteFinderDFT.AddAudioData(AudioData, (uint)AudioData.Length);
        ShinNoteFinderDFT.CalculateOutput();
        ShinNoteFinderDFT.AddAudioData(new short[8192], 8192);
        ShinNoteFinderDFT.CalculateOutput();
    }

    [TestMethod]
    public void OutputResponseData()
    {
        float MinFreq = 880F;
        float MaxFreq = 1760F;
        int Steps = 400;

        string[] OutputLines = new string[Steps];

        uint WindowSize = ShinNoteFinderDFT.MaxPresentWindowSize;
        for (int f = 0; f < Steps; f++)
        {
            float FreqHere = MinFreq + ((float)f / Steps * (MaxFreq - MinFreq)); // Linear, maybe make log later on?
            float Omega = FreqHere * MathF.Tau / ShinNoteFinderDFT.SampleRate;

            short[] AudioData = new short[WindowSize + 123];
            for (int s = 0; s < AudioData.Length; s++)
            {
                float Sin = MathF.Sin(s * Omega);
                AudioData[s] = (short)MathF.Round(1000 * Sin);
            }

            ShinNoteFinderDFT.AddAudioData(AudioData, (uint)AudioData.Length);
            ShinNoteFinderDFT.CalculateOutput();
            StringBuilder ThisLine = new();
            ThisLine.Append(FreqHere);
            ThisLine.Append(',');
            for (int b = 0; b < ShinNoteFinderDFT.RawBinMagnitudes.Length; b++)
            {
                ThisLine.Append(ShinNoteFinderDFT.RawBinMagnitudes[b]);
                ThisLine.Append(',');
            }
            OutputLines[f] = ThisLine.ToString();
        }

        File.WriteAllLines("OutputResponseData.csv", OutputLines);
    }

    [TestMethod]
    public void TestSinInterpolation()
    {
        const float WAVE_LOCATION = 0.18F;
        const float TEST_RANGE = 0.03F;
        const int TEST_STEPS = 20;
        const short TABLE_AMPLITUDE = 4095;
        const float ALLOWED_ERROR = 2.0F; // %

        ShinNoteFinderDFT.GetSine(new() { NCLeft = 9725, NCRight = 52764 }, false);

        for (int i = 0; i < TEST_STEPS; i++)
        {
            float WaveLocHere = WAVE_LOCATION - TEST_RANGE + (TEST_RANGE * 2 * ((float)i / TEST_STEPS)); // From (WAVE_LOCATION - TEST_RANGE) to (WAVE_LOCATION + TEST_RANGE)
            ShinNoteFinderDFT.DualU16 Position = new() { NCLeft = (ushort)(WaveLocHere * 65535.0F), NCRight = (ushort)(WaveLocHere * 65535.0F) };
            ShinNoteFinderDFT.DualI16 LUTSine = ShinNoteFinderDFT.GetSine(Position, false);

            float RealSine = TABLE_AMPLITUDE * MathF.Sin(WaveLocHere * MathF.Tau);
            short RealSineRounded = (short)MathF.Round(RealSine);
            float Difference = 100F * (LUTSine.NCLeft - RealSineRounded) / RealSineRounded;
            Console.WriteLine($"{i}:{WaveLocHere:F5},0x{Position.NCLeft:X4},{LUTSine.NCLeft},{RealSineRounded},{Difference:+0.00;-0.00;0.00}%");
            Assert.IsTrue(Difference > -ALLOWED_ERROR, $"The interpolated LUT sine output {LUTSine.NCLeft} was too small compared to the expected value of {RealSine}");
            Assert.IsTrue(Difference < ALLOWED_ERROR, $"The interpolated LUT sine output {LUTSine.NCLeft} was too large compared to the expected value of {RealSine}");
        }
    }

    [TestMethod]
    public void TestVecSine()
    {
        const float WAVE_LOCATION = 0.58F;
        const float TEST_RANGE = 0.60F;
        const int TEST_STEPS = 200;
        const short TABLE_AMPLITUDE = 4095;
        const float ALLOWED_ERROR = 2.0F; // %

        ShinNoteFinderDFT.GetSine(new() { NCLeft = 9725, NCRight = 52764 }, false);

        for (int i = 0; i < TEST_STEPS; i++)
        {
            float WaveLocHere = WAVE_LOCATION - TEST_RANGE + (TEST_RANGE * 2 * ((float)i / TEST_STEPS)); // From (WAVE_LOCATION - TEST_RANGE) to (WAVE_LOCATION + TEST_RANGE)
            Vector256<ushort> Position = Vector256.Create((ushort)(WaveLocHere * 65535.0F)); // yes this is dumb but meh
            short LUTSine = ShinNoteFinderDFT.GetSine256(Position)[0];

            float RealSine = TABLE_AMPLITUDE * MathF.Sin(WaveLocHere * MathF.Tau);
            short RealSineRounded = (short)MathF.Round(RealSine);
            float Difference = (Math.Abs(LUTSine - RealSineRounded) > 5) ? 100F * (LUTSine - RealSineRounded) / RealSineRounded : 0;
            Console.WriteLine($"{i}:{WaveLocHere:F5},0x{Position[0]:X4},{LUTSine},{RealSineRounded},{Difference:+0.00;-0.00;0.00}%");
            Assert.IsTrue(Difference > -ALLOWED_ERROR, $"The interpolated LUT sine output {LUTSine} was too small compared to the expected value of {RealSine}");
            Assert.IsTrue(Difference < ALLOWED_ERROR, $"The interpolated LUT sine output {LUTSine} was too large compared to the expected value of {RealSine}");
        }
    }

    [TestMethod]
    public void TestVecWrapRead()
    {
        ushort BUFFER_LEN = 48;
        short[] DataBuffer = new short[BUFFER_LEN + 16];
        for (int i = 0; i < DataBuffer.Length; i++) { DataBuffer[i] = (short)((i >= BUFFER_LEN) ? -i : i); }

        MethodInfo TargetMethod = typeof(ShinNoteFinderDFT).GetMethod("ReadArrayWraparound", BindingFlags.Static | BindingFlags.NonPublic);

        for (int h = BUFFER_LEN - 15; h < BUFFER_LEN; h++)
        {
            short[] Expected = new short[16];
            for (int i = 0; i < Expected.Length; i++) { Expected[i] = DataBuffer[(h + i) % BUFFER_LEN]; }

            object? ResultObj = TargetMethod.Invoke(null, new object[] { DataBuffer, BUFFER_LEN, (uint)h });
            Vector256<short> Result = ResultObj as Vector256<short>? ?? throw new Exception("Could not get result from method call");

            for (int i = 0; i < Expected.Length; i++) { Assert.AreEqual(Expected[i], Result[i], $"Item at index {i} was not as expected when reading from {h}."); }
            Console.WriteLine($"Finished checking offset {h} ({BUFFER_LEN - h} from back + {16 - (BUFFER_LEN - h)} from front)");
        }
    }
}
