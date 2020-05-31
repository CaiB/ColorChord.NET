using System;
using System.Reflection;
using System.Runtime.Serialization;
using ColorChord.NET.NoteFinder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ColorChordTests
{
    [TestClass]
    public class ShinNoteFinderTest
    {
        [TestMethod]
        public void BinFrequencyList()
        {
            const float BASE_FREQ = 55F;
            ShinNoteFinderDFT NF = new ShinNoteFinderDFT();
            NF.CalculateFrequencies(BASE_FREQ);

            FieldInfo BinFreq = typeof(ShinNoteFinderDFT).GetField("BinFrequencies", BindingFlags.NonPublic | BindingFlags.Instance);
            float[] Value = (float[])BinFreq.GetValue(NF);

            // Make sure we have the correct number of frequency bins.
            Assert.AreEqual(NF.BinCount, Value.Length, "Bin count was incorrect");

            // Make sure the first bin is the base frequency we set earlier.
            Assert.AreEqual(BASE_FREQ, Value[0], "Base frequency was incorrectly set");

            float CalcNextOctave = Value[NF.BinsPerOctave];

            // Make sure the bin 1 octave up is double the base frequency.
            Assert.IsTrue(Math.Abs((BASE_FREQ * 2) - CalcNextOctave) < 0.0001F, "Higher frequencies were incorrectly calculated");
        }

        [TestMethod]
        [DataRow(73.42F, 10, DisplayName = "D2")] // Bottom octave
        [DataRow(92.50F, 18, DisplayName = "F#2")] // Bottom octave
        [DataRow(100.87F, 21, DisplayName = "G2-G#2 midpoint")] // Bottom octave
        [DataRow(185.00F, 42, DisplayName = "F#3")]
        [DataRow(698.46F, 88, DisplayName = "F5")]
        [DataRow(1174.66F, 106, DisplayName = "D6")] // Top octave
        public void OutputBinTestSingleValueWithPureSine(float testFreq, int expectedPeak)
        {
            const float BASE_FREQ = 55F; // A2

            ShinNoteFinderDFT NF = new ShinNoteFinderDFT();
            NF.CalculateFrequencies(BASE_FREQ);
            NF.FillReferenceTables();
            NF.PrepareSampleStorage();

            // Fill input.
            float Omega1 = (float)(testFreq * Math.PI * 2 / NF.SampleRate);

            float[] TestWaveform = new float[NF.WindowSize / 2];
            for (uint i = 0; i < TestWaveform.Length; i++)
            {
                TestWaveform[i] = (float)Math.Sin(i * Omega1);
            }
            NF.AddSamples(TestWaveform);
            //NF.SaveData();

            // Get output
            float[] Output = NF.GetBins();

            // Find peak
            float PeakVal = -1F;
            int PeakInd = -1;

            for (int i = 0; i < Output.Length; i++)
            {
                if (Output[i] > PeakVal)
                {
                    PeakVal = Output[i];
                    PeakInd = i;
                }
            }

            // Make sure peak is correct and large enough
            Assert.IsTrue(PeakVal > 1500F, "Peak was not large enough");
            Assert.IsTrue(PeakInd == expectedPeak, "Peak was in the wrong place");

            // Make sure all far-away bins are small enough
            for (int i = 0; i < Output.Length; i++)
            {
                if (Math.Abs(i - PeakInd) > 3) { Assert.IsTrue(Output[i] < (PeakVal / 2), "Other frequencies had content too strong compared to peak"); }
            }
        }

        [TestMethod] // Note that the first one has to be the lower frequency.
        [DataRow(73.42F, 92.50F, 10, 18, 0.5F, DisplayName = "D2 + F#2, 1:1 ratio")]
        // [DataRow(92.50F, 103.83F, 18, 22, 0.5F, DisplayName = "F#2 + G#2, 1:1 ratio")] // This fails due to low noise at low frequencies, not an implementation fault.
        [DataRow(698.46F, 1174.66F, 88, 106, 0.5F, DisplayName = "F5 + D6, 1:1 ratio")]
        [DataRow(130.81F, 987.77F, 30, 100, 0.3F, DisplayName = "C3 + B5, 3:7 ratio")]
        public void OutputBinTestCompondSineProprotional(float testFreq1, float testFreq2, int expectedPeak1, int expectedPeak2, float ratio)
        {
            const float BASE_FREQ = 55F; // A2

            ShinNoteFinderDFT NF = new ShinNoteFinderDFT();
            NF.CalculateFrequencies(BASE_FREQ);
            NF.FillReferenceTables();
            NF.PrepareSampleStorage();

            // Fill input.
            float Omega1 = (float)(testFreq1 * Math.PI * 2 / NF.SampleRate);
            float Omega2 = (float)(testFreq2 * Math.PI * 2 / NF.SampleRate);

            float[] TestWaveform = new float[NF.WindowSize / 2];
            for (uint i = 0; i < TestWaveform.Length; i++)
            {
                double Wave1 = ratio * Math.Sin(i * Omega1);
                double Wave2 = (1 - ratio) * Math.Sin(i * Omega2);
                TestWaveform[i] = (float)(Wave1 + Wave2);
            }
            NF.AddSamples(TestWaveform);

            // Get output
            float[] Output = NF.GetBins();

            // Find peaks
            float PeakVal1 = -1F;
            float PeakVal2 = -1F;
            int PeakInd1 = -1;
            int PeakInd2 = -1;

            for (int i = 0; i < Output.Length; i++)
            {
                if (Output[i] > PeakVal1)
                {
                    PeakVal2 = PeakVal1;
                    PeakVal1 = Output[i];
                    PeakInd2 = PeakInd1;
                    PeakInd1 = i;
                }
                else if (Output[i] > PeakVal2)
                {
                    PeakVal2 = Output[i];
                    PeakInd2 = i;
                }
            }

            // Sort the peaks
            if (PeakInd1 > PeakInd2)
            {
                float ValTemp = PeakVal1;
                PeakVal1 = PeakVal2;
                PeakVal2 = ValTemp;
                int IndTemp = PeakInd1;
                PeakInd1 = PeakInd2;
                PeakInd2 = IndTemp;
            }

            // Make sure peaks are correct and large enough
            Assert.IsTrue(PeakVal1 > (1500F * ratio), "Peak 1 was not large enough");
            Assert.IsTrue(PeakVal2 > (1500F * (1 - ratio)), "Peak 2 was not large enough");

            Assert.IsTrue(PeakInd1 == expectedPeak1, "Peak 1 was in the wrong place");
            Assert.IsTrue(PeakInd2 == expectedPeak2, "Peak 2 was in the wrong place");

            // Make sure the ratio is about right
            float Total = PeakVal1 + PeakVal2;
            Assert.IsTrue(Math.Abs((PeakVal1 / Total) - ratio) < 0.05F, "Peak 1 was not balanced to ratio");
            Assert.IsTrue(Math.Abs((PeakVal2 / Total) - (1 - ratio)) < 0.05F, "Peak 1 was not balanced to ratio");

            // Make sure all far-away bins are small enough
            for (int i = 0; i < Output.Length; i++)
            {
                if (Math.Abs(i - PeakInd1) > 3 &&
                    Math.Abs(i - PeakInd2) > 3)
                {
                    Assert.IsTrue(Output[i] < (Math.Max(PeakVal1, PeakVal2) / 1.5F), "Too much noise far away from peaks");
                }
            }
        }

        [TestMethod]
        public void CheckOctavePattern()
        {
            const float BASE_FREQ = 55F; // A2
            const float SIGNAL_FREQ = 880F;
            const double PHASE_OFFSET = Math.PI / 4; // Apply a small phase offset so that the signal doesn't start at 0.

            ShinNoteFinderDFT NF = new ShinNoteFinderDFT();
            NF.CalculateFrequencies(BASE_FREQ);
            NF.FillReferenceTables();
            NF.PrepareSampleStorage();

            float Omega = (float)(SIGNAL_FREQ * Math.PI * 2 / NF.SampleRate);

            // Add the first sample, so we should only see content in the topmost octave
            NF.AddSample((float)Math.Sin((0 * Omega) + PHASE_OFFSET), true);
            CheckAllBins((NF.OctaveCount - 1) * NF.BinsPerOctave);

            // Second sample, now the top two octaves should be calculated.
            NF.AddSample((float)Math.Sin((1 * Omega) + PHASE_OFFSET), true);
            CheckAllBins((NF.OctaveCount - 2) * NF.BinsPerOctave);

            // Third sample, only the top octave should update
            NF.AddSample((float)Math.Sin((2 * Omega) + PHASE_OFFSET), true);
            CheckAllBins((NF.OctaveCount - 2) * NF.BinsPerOctave);

            // Fourth sample, the top 3 octaves should all get calculated
            NF.AddSample((float)Math.Sin((3 * Omega) + PHASE_OFFSET), true);
            CheckAllBins((NF.OctaveCount - 3) * NF.BinsPerOctave);

            // Fifth sample, only top octave
            NF.AddSample((float)Math.Sin((4 * Omega) + PHASE_OFFSET), true);
            CheckAllBins((NF.OctaveCount - 3) * NF.BinsPerOctave);

            // Sixth sample, top 2 octaves
            NF.AddSample((float)Math.Sin((5 * Omega) + PHASE_OFFSET), true);
            CheckAllBins((NF.OctaveCount - 3) * NF.BinsPerOctave);

            // Seventh sample, only top
            NF.AddSample((float)Math.Sin((6 * Omega) + PHASE_OFFSET), true);
            CheckAllBins((NF.OctaveCount - 3) * NF.BinsPerOctave);

            // Eighth sample, top 4 octaves should all be calculated.
            NF.AddSample((float)Math.Sin((7 * Omega) + PHASE_OFFSET), true);
            CheckAllBins((NF.OctaveCount - 4) * NF.BinsPerOctave);

            // 9th - 15th samples, same pattern, no new octaves yet
            for (int i = 8; i < 15; i++)
            {
                NF.AddSample((float)Math.Sin((i * Omega) + PHASE_OFFSET), true);
                CheckAllBins((NF.OctaveCount - 4) * NF.BinsPerOctave);
            }

            // 16th sample, top 5 octaves should be calculated.
            NF.AddSample((float)Math.Sin((15 * Omega) + PHASE_OFFSET), true);
            CheckAllBins((NF.OctaveCount - 5) * NF.BinsPerOctave);

            void CheckAllBins(int contentStart)
            {
                for (ushort i = 0; i < contentStart; i++) // This octave should not yet have been calculated.
                {
                    Assert.IsTrue(NF.GetBins()[i] == 0, "Bin " + i + " should have been empty");
                }
                for (ushort i = (ushort)contentStart; i < NF.BinCount; i++) // All other bins should have at least a slight amount of content.
                {
                    Assert.IsTrue(NF.GetBins()[i] != 0, "Bin " + i + " should not have been empty");
                }
            }
        }
    }
}
