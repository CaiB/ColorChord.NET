using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ColorChord.NET.NewNote
{
    public static class NoteFinder2
    {
        /// <summary> The buffer for audio data gathered from a system device. Circular buffer, with the current read position stored in <see cref="AudioBufferHead"/>. </summary>
        public static float[] AudioBuffer = new float[8192]; // TODO: Make buffer size adjustable or auto-set based on sample rate (might be too short for super-high rates)

        /// <summary> Where in <see cref="AudioBuffer"/> we are currently reading. </summary>
        public static int AudioBufferHeadRead = 0;

        /// <summary> Where in the <see cref="AudioBuffer"/> we are currently adding new audio data. </summary>
        public static int AudioBufferHeadWrite = 0;

        /// <summary> When data was last added to the buffer. Used to detect idle state. </summary>
        public static DateTime LastDataAdd;

        /// <summary> The speed (in ms between runs) at which the note finder needs to run, set by the fastest visualizer. </summary>
        public static uint ShortestPeriod { get; private set; } = 100;

        /// <summary> How many bins compose one octave in the raw DFT data. </summary>
        private const int OctaveBinCount = 24;

        /// <summary> Over how many octaves the raw DFT data will be processed. </summary>
        private const int OctaveCount = 5;

        /// <summary> How many bins the DFT will class sound frequency data into. </summary>
        private const int DFTRawBinCount = OctaveBinCount * OctaveCount;

        /// <summary> Determines how much the previous frame's DFT data is used in the next frame. Smooths out rapid changes from frame-to-frame, but can cause delay if too strong. </summary>
        /// <remarks> Lower values will mean less inter-frame smoothing. Range: 0.0~1.0 </remarks>
        private static float DFTIIRMultiplier = 0.65F;

        /// <summary> The non-folded frequency bins, used inter-frame to do smoothing, then folded to form the spectrum. </summary>
        /// <remarks> Re-used between cycles to do smoothing. </remarks>
        private static readonly float[] FrequencyBinValues = new float[DFTRawBinCount];

        /// <summary> Determines how much the raw DFT data is amplified before being used. </summary>
        /// <remarks> Range 0.0+ </remarks>
        private static float DFTDataAmplifier = 2F;

        /// <summary> The slope of the extra frequency-dependent amplification done to raw DFT data. Positive values increase sensitivity at higher frequencies. </summary>
        /// <remarks> Amplification is 1.0 at the minimum frequency, and 1.0 + (<see cref="DFTSensitivitySlope"/> * <see cref="DFTRawBinCount"/>) at the highest, increasing by <see cref="DFTSensitivitySlope"/> at each bin. </remarks>
        private static float DFTSensitivitySlope = 0.1F;

        /// <summary> The frequency spectrum, folded to overlap into a single octave length. </summary>
        /// <remarks> Not re-used between cycles. </remarks>
        private static readonly float[] OctaveBinValues = new float[OctaveBinCount];

        /// <summary> How often to run the octave data filter. This smoothes out each bin with adjacent ones. </summary>
        private static int OctaveFilterIterations = 2;
        
        /// <summary> How strong the octave data filter is. Higher values mean each bin is more aggresively averaged with adjacent bins. </summary>
        /// <remarks> Higher values mean less glitchy, but also less clear note peaks. Range: 0.0~1.0 </remarks>
        private static float OctaveFilterStrength = 0.5F;

        /// <summary> Up to how many note peaks can be extracted from the frequency data. </summary>
        private const int NoteDistributionCount = OctaveBinCount / 2;

        /// <summary> The individual note distributions (peaks) detected this cycle. </summary>
        /// <remarks> Not re-used between cycles. </remarks>
        private static NoteDistribution[] NoteDistributions = new NoteDistribution[NoteDistributionCount];

        /// <summary> The sigma value to use for <see cref="NoteDistribution"/> by default. </summary>
        private const float DefaultDistributionSigma = 1.4F;

        /// <summary> Used in normalizing all peak amplitudes. </summary>
        private const float PeakCompressCoefficient = 1F;

        /// <summary> Used in normalizing all peak amplitudes. </summary>
        private const float PeakCompressExponent = 0.5F;

        /// <summary> Whether to keep processing, or shut down operations.  </summary>
        private static bool KeepGoing = true;

        /// <summary> The thread doing the actual note data processing. </summary>
        private static Thread ProcessThread;

        /// <summary> Updates the sample rate if the audio source has changed. </summary>
        public static void SetSampleRate(int sampleRate)
        {
            for (int i = 0; i < Freqs; i++)
            {
                Frequencies[i] = (float)((sampleRate / BaseHz) / Math.Pow(2, (float)i / FreqBinCount));
            }
        }

        /// <summary> Adjusts the note finder run interval if the newly added visualizer/output needs it to run faster, otherwise does nothing. </summary>
        /// <param name="period"> The period, in milliseconds, that you need the note finder to run at or faster than. </param>
        public static void AdjustOutputSpeed(uint period)
        {
            if (period < ShortestPeriod) { ShortestPeriod = period; }
        }

        /// <summary> Starts the processing thread. </summary>
        public static void Start()
        {
            KeepGoing = true;
            ProcessThread = new Thread(DoProcessing);
            ProcessThread.Start();
        }

        /// <summary> Stops the processing thread. </summary>
        public static void Stop()
        {
            KeepGoing = false;
            ProcessThread.Join();
        }

        /// <summary> Runs until <see cref="KeepGoing"/> becomes false, processing incoming audio data. </summary>
        private static void DoProcessing()
        {
            Stopwatch Timer = new Stopwatch();
            while (KeepGoing)
            {
                Timer.Restart();
                Cycle();
                int WaitTime = (int)(ShortestPeriod - (Timer.ElapsedMilliseconds));
                if (WaitTime > 0) { Thread.Sleep(WaitTime); }
            }
        }

        private static void Cycle()
        {
            // DFT outputs only a small number of bins, we'll need to process this data a lot to get smooth note positions.
            float[] DFTBinData = new float[DFTRawBinCount];

            // Pre-process input DFT data.
            for (int RawBinIndex = 0; RawBinIndex < DFTRawBinCount; RawBinIndex++)
            {
                float NewData = DFTBinData[RawBinIndex]; // The raw DFT data for this bin
                NewData *= DFTDataAmplifier; // Amplify incoming data by a constant
                NewData *= (1 + DFTSensitivitySlope * RawBinIndex); // Apply a frequency-dependent amplifier to increase sensitivity at higher frequencies.

                FrequencyBinValues[RawBinIndex] = (FrequencyBinValues[RawBinIndex] * DFTIIRMultiplier) + // Keep data from last frame, but reduce by a factor.
                                               (NewData * (1 - DFTIIRMultiplier)); // Add new data
            }

            // Taper off the first and last octave.
            for (int OctaveBinIndex = 0; OctaveBinIndex < OctaveBinCount; OctaveBinIndex++)
            {
                FrequencyBinValues[OctaveBinIndex] *= (OctaveBinIndex + 1F) / OctaveBinCount; // Taper the first octave
                FrequencyBinValues[DFTRawBinCount - OctaveBinIndex - 1] *= (OctaveBinIndex + 1F) / OctaveBinCount; // Taper the last octave
            }

            // Fold the bins to make one single octave-length array, where all like notes (e.g. C2, C3, C4) are combined, regardless of their original octave.
            for (int BinIndex = 0; BinIndex < OctaveBinCount; BinIndex++)
            {
                float Amplitude = 0;
                for (int Octave = 0; Octave < OctaveCount; Octave++) { Amplitude += FrequencyBinValues[(Octave * OctaveBinCount) + BinIndex]; }
                OctaveBinValues[BinIndex] = Amplitude;
            }

            // Do some filtering on the now-folded bins to remove meaningless peaks.
            // Averages out each bin a little bit with adjacent bins.
            // Runs [OctaveFilterIterations] times, averaging with strength [OctaveFilterStrength].
            float[] OctaveBinValuesPre = new float[OctaveBinCount];
            for (int Iteration = 0; Iteration < OctaveFilterIterations; Iteration++)
            {
                Array.Copy(OctaveBinValues, OctaveBinValuesPre, OctaveBinCount); // COpy the octave data into our temporary array.
                for (int BinIndex = 0; BinIndex < OctaveBinCount; BinIndex++)
                {
                    int IndexRight = (BinIndex + OctaveBinCount + 1) & OctaveBinCount; // The next bin to the right (wrapping around if needed).
                    int IndexLeft = (BinIndex + OctaveBinCount - 1) % OctaveBinCount; // The next bin to the left (wrapping around if needed).
                    float ValueRight = OctaveBinValuesPre[IndexRight];
                    float ValueLeft = OctaveBinValuesPre[IndexLeft];

                    float NewValue = OctaveBinValues[BinIndex] * (1F - OctaveFilterStrength); // Some of the current value in the bin
                    NewValue += ((ValueLeft + ValueRight) / 2) * OctaveFilterStrength; // Add the average of the adjacent bins, scaled by the filter strength

                    OctaveBinValues[BinIndex] = NewValue;
                }
            }

            // Reset all note distributions to off state.
            for (int NoteIndex = 0; NoteIndex < NoteDistributionCount; NoteIndex++) { NoteDistributions[NoteIndex].Present = false; }

            // Find note distributions.
            // NOTE: This is decompose.c/DecomposeHistogram in TURBO_DECOMPOSE mode (single iteration).
            //       Non-TURBO_DECOMPOSE mode is currently not implemented here, as it doesn't seem to be used upstream.

            int DistributionsFound = 0; //peak

            for (int BinIndex = 0; BinIndex < OctaveBinCount; BinIndex++)
            {
                int IndexLeft = (BinIndex - 1 + OctaveBinCount) % OctaveBinCount;
                int IndexRight = (BinIndex + 1) % OctaveBinCount;
                float ValueLeft = OctaveBinValues[IndexLeft];
                float ValueHere = OctaveBinValues[BinIndex];
                float ValueRight = OctaveBinValues[IndexRight];

                if (ValueLeft > ValueHere || ValueRight > ValueHere) { continue; } // Adjacent bins are higher, this is not a peak.
                if (ValueLeft == ValueHere && ValueRight == ValueHere) { continue; } // Adjacent bins are both equal, this is a plateau (e.g. all 0).

                // TODO: This isn't 100% certain.
                // This bin is a peak, adjacent values are lower.
                // Now we try to locate where the peak should be within this one bin.
                float TotalAdjacentDifference = ((ValueHere - ValueLeft) + (ValueHere - ValueRight));
                float ProportionalDifferenceLeft = (ValueHere - ValueLeft) / TotalAdjacentDifference;
                float ProportionalDifferenceRight = (ValueHere - ValueRight) / TotalAdjacentDifference;

                float InternalOffset; // Where in this bin the peak is.
                if (ProportionalDifferenceLeft < ProportionalDifferenceRight) { InternalOffset = -(0.5F - ProportionalDifferenceLeft); } // In the left half of this bin.
                else { InternalOffset = (0.5F - ProportionalDifferenceRight); } // In the right half of this bin.

                // Output the distribution information.
                NoteDistributions[DistributionsFound].Mean = BinIndex + InternalOffset;
                NoteDistributions[DistributionsFound].Amplitude = ValueHere * 4;
                NoteDistributions[DistributionsFound].Sigma = DefaultDistributionSigma;

                DistributionsFound++;
            }

            // Clear out the distributions that are not currently active.
            for (int DistrIndex = DistributionsFound; DistrIndex < NoteDistributionCount; DistrIndex++)
            {
                NoteDistributions[DistrIndex].Mean = -1F;
                NoteDistributions[DistrIndex].Amplitude = 0F;
                NoteDistributions[DistrIndex].Sigma = DefaultDistributionSigma;
            }

            // Normalize distribution amplitudes.
            // Start by summing all peak amplitudes.
            float AmplitudeSum = 0;
            for (int DistrIndex = 0; DistrIndex < DistributionsFound; DistrIndex++) { AmplitudeSum += NoteDistributions[DistrIndex].Amplitude; }

            // Find coefficient to multiply all by.
            float AmplitudeCoefficient = (float)(PeakCompressCoefficient / Math.Pow(AmplitudeSum * PeakCompressCoefficient, PeakCompressExponent));

            // Scale peaks.
            for (int DistrIndex = 0; DistrIndex < DistributionsFound; DistrIndex++) { NoteDistributions[DistrIndex].Amplitude *= AmplitudeCoefficient; }

            // Sort peaks so they are in high-to-low amplitude order.
            Array.Sort(NoteDistributions);

        }

        /// <summary> A note, represented as a location, amplitude, and sigma, defining a normal (Gaussian) distribution. </summary>
        private struct NoteDistribution : IComparable<NoteDistribution>
        {
            /// <summary> The amplitude (relative strength) of the note. </summary>
            public float Amplitude;

            /// <summary> The mean (location) of the note in the frequency spectrum. </summary>
            /// <remarks> Range: 0.0 to <see cref="OctaveBinCount"/>. Fractional part shows where in the bin the peak is. </remarks>
            public float Mean;

            /// <summary> The sigma (spread) of the note. </summary>
            public float Sigma;

            /// <summary> Whether the note is present, or inactive. </summary>
            public bool Present;

            /// <summary> Compares the note amplitudes. </summary>
            /// <param name="other"> The note distribution to compare to. </param>
            /// <returns> </returns>
            public int CompareTo(NoteDistribution other)
            {
                if (this.Amplitude > other.Amplitude) { return -1; }
                else if (this.Amplitude < other.Amplitude) { return 1; }
                else { return 0; }
            }
        }

        [DllImport("ColorChordLib.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DoDFTProgressive32([In, Out] float[] OutBins, [In, Out] float[] Frequencies, int Bins, float[] DataBuffer, int DataBufferLoc, int DataBufferSize, float Q, float Speedup);

    }
}
