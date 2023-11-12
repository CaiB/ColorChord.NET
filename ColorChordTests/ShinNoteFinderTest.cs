using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using ColorChord.NET.NoteFinder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTK.Graphics.OpenGL.GL;

namespace ColorChordTests;

[TestClass]
public class ShinNoteFinderTest
{
    [TestMethod]
    public void TestInit()
    {
        ShinNoteFinderDFT.Reconfigure();
    }

    [TestMethod]
    public void TestDataAdd()
    {
        ShinNoteFinderDFT.AddAudioData(new short[] { 1, 2, 3, 4, 5, 6, 7, 8 });
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

        ShinNoteFinderDFT.AddAudioData(AudioData);
        ShinNoteFinderDFT.CalculateOutput();
    }

    [TestMethod]
    public void TestSineInputAndOutputEachStep()
    {
        float Frequency = 1046.502F;
        float Omega = Frequency * MathF.Tau / ShinNoteFinderDFT.SampleRate;
        const int SAMPLE_COUNT = 16384;

        string[] Output = new string[SAMPLE_COUNT];

        for (int s = 0; s < SAMPLE_COUNT; s++)
        {
            float Sin = MathF.Sin(s * Omega);
            short AudioData = (short)MathF.Round(200 * Sin);
            ShinNoteFinderDFT.AddAudioData(new short[] { AudioData });
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

        ShinNoteFinderDFT.AddAudioData(AudioData);
        ShinNoteFinderDFT.CalculateOutput();
        ShinNoteFinderDFT.AddAudioData(new short[8192]);
        ShinNoteFinderDFT.CalculateOutput();
    }

    [TestMethod]
    public void TestDCInput()
    {
        const short DC_VALUE = 500;
        short[] AudioData = new short[4096];
        for (int i = 0; i < AudioData.Length; i++) { AudioData[i] = DC_VALUE; }
        ShinNoteFinderDFT.AddAudioData(AudioData);
        ShinNoteFinderDFT.CalculateOutput();
        ShinNoteFinderDFT.AddAudioData(new short[8192]);
        ShinNoteFinderDFT.CalculateOutput();
    }

    [TestMethod]
    public void OutputResponseData()
    {
        float MinFreq = 880F;
        float MaxFreq = 1760F;
        int Steps = 400;

        string[] OutputLines = new string[Steps];

        int WindowSize = ShinNoteFinderDFT.MaxWindowSize;
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

            ShinNoteFinderDFT.AddAudioData(AudioData);
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
        const short TABLE_AMPLITUDE = short.MaxValue / 2;
        const float ALLOWED_ERROR = 2.0F; // %


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
}
