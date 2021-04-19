using System;

namespace ColorChord.NET.NoteFinder
{
    public static class BaseNoteFinderDFT
    {
        private const byte OCTAVES = 5;
        private const ushort BINSPEROCT = 24;
        private const uint TOTALBINS = BINSPEROCT * OCTAVES;
        private const uint BINCYCLE = 1 << OCTAVES;
        private const byte DFTIIR = 6;

        /// <summary>Where we are currently writing new bin information to be passed out to the requestor.</summary>
        private static float[] OutputBins;

        /// <summary>Indicates whether <see cref="OctaveProcessingSchedule"/> has been set up.</summary>
        private static bool SetupDone = false;

        /// <summary>A table of precomputed sin values ranging -1500 to +1500.</summary>
        /// <remarks>If we increase the magnitude, it may cause overflows elsewhere in code.</remarks>
        private static readonly short[] SineLUT = new short[]
        {
             0,    36,    73,   110,   147,   183,   220,   256,
           292,   328,   364,   400,   435,   470,   505,   539,
           574,   607,   641,   674,   707,   739,   771,   802,
           833,   863,   893,   922,   951,   979,  1007,  1034,
          1060,  1086,  1111,  1135,  1159,  1182,  1204,  1226,
          1247,  1267,  1286,  1305,  1322,  1339,  1355,  1371,
          1385,  1399,  1412,  1424,  1435,  1445,  1455,  1463,
          1471,  1477,  1483,  1488,  1492,  1495,  1498,  1499,
          1500,  1499,  1498,  1495,  1492,  1488,  1483,  1477,
          1471,  1463,  1455,  1445,  1435,  1424,  1412,  1399,
          1385,  1371,  1356,  1339,  1322,  1305,  1286,  1267,
          1247,  1226,  1204,  1182,  1159,  1135,  1111,  1086,
          1060,  1034,  1007,   979,   951,   922,   893,   863,
           833,   802,   771,   739,   707,   674,   641,   607,
           574,   539,   505,   470,   435,   400,   364,   328,
           292,   256,   220,   183,   147,   110,    73,    36,
             0,   -36,   -73,  -110,  -146,  -183,  -219,  -256,
          -292,  -328,  -364,  -399,  -435,  -470,  -505,  -539,
          -573,  -607,  -641,  -674,  -706,  -739,  -771,  -802,
          -833,  -863,  -893,  -922,  -951,  -979, -1007, -1034,
         -1060, -1086, -1111, -1135, -1159, -1182, -1204, -1226,
         -1247, -1267, -1286, -1305, -1322, -1339, -1355, -1371,
         -1385, -1399, -1412, -1424, -1435, -1445, -1454, -1463,
         -1471, -1477, -1483, -1488, -1492, -1495, -1498, -1499,
         -1500, -1499, -1498, -1495, -1492, -1488, -1483, -1477,
         -1471, -1463, -1455, -1445, -1435, -1424, -1412, -1399,
         -1385, -1371, -1356, -1339, -1322, -1305, -1286, -1267,
         -1247, -1226, -1204, -1182, -1159, -1135, -1111, -1086,
         -1060, -1034, -1007,  -979,  -951,  -923,  -893,  -863,
          -833,  -802,  -771,  -739,  -707,  -674,  -641,  -608,
          -574,  -540,  -505,  -470,  -435,  -400,  -364,  -328,
          -292,  -256,  -220,  -183,  -147,  -110,   -73,   -37
        };

        /// <summary>Contains data about where in the sine table each bin is looking, and how far it needs to step forward with each sample.</summary>
        /// <remarks>
        /// 2 pieces of data interleaved: {Step Size, Current Index} x <see cref="TOTALBINS"/>.
        /// Step Size is how much to advance index every sample, and depends on the frequency of the given bin.
        /// Current Index is 8b+8b fixed-point location in the sine table, which gets advanced by [Step Size] every cycle that this bin is active.
        /// </remarks>
        private static readonly ushort[] WaveIndexData = new ushort[TOTALBINS * 2];

        /// <summary>Stores sums of (sin * sample) and (cos * sample) interleaved for each bin. Data gets added to the relevant octave with every input sample, and then gets output and old data removed once per schedule iteration.</summary>
        private static readonly int[] SampleTrigProductAccumulators = new int[TOTALBINS * 2];

        /// <summary>The sample*trig product sums, updated once per full schedule run.</summary>
        /// <remarks>
        /// This gets updated 1 out of 2^(OCTAVES) cycles, and since every sample uses 2 cycles, this is 1 out of every 2^(OCTAVES - 1) input samples.
        /// Contains sine and cosine data interleaved, in format {SinProductSum, CosProductSum} repeated <see cref="TOTALBINS"/> times.
        /// </remarks>
        private static readonly int[] SampleTrigProductSums = new int[TOTALBINS * 2];

        /// <summary>Holds the schedule for which octaves need to be proccessed in each cycle. The top octave needs to be calculated every cycle regardless, but other octaves are updated less frequently, and this array contains the octave index to process with each incoming sample cycle.</summary>
        private static readonly byte[] OctaveProcessingSchedule = new byte[BINCYCLE];

        /// <summary>The input sample gets added to each accumulator until that octave gets processed, when it gets set back to 0. This resamples the input data to each lower octave.</summary>
        private static readonly int[] OctaveResamplingAccumulators = new int[OCTAVES];

        /// <summary>Keeps track of which cycle we are in, which dictates what octave(s) are processed in the current cycle according to <see cref="OctaveProcessingSchedule"/>.</summary>
        private static byte OctaveCycleCounter;

        /// <summary>Where in the input audio buffer we last read data. Used to know which samples to process at each run.</summary>
        private static int LastBufferReadLocation;

        /// <summary>The data in the output bins from last time the DFT was run.</summary>
        private static readonly float[] LastRunOutput = new float[TOTALBINS];

        /// <summary>Sets up <see cref="OctaveProcessingSchedule"/>. Must be done once before the first sample is processed, no need to redo it later.</summary>
        private static void SetupDFTProgressive32()
        {
            OctaveProcessingSchedule[0] = 0xFF; // Process all octaves in the first cycle and every 2^(OCTAVES) cycles thereafter.
            for (int i = 0; i < BINCYCLE - 1; i++)
            {
                // Example resulting sequence for OCTAVES = 5:
                //   [255 4] [3 4] [2 4] [3 4] [1 4] [3 4] [2 4] [3 4] [0 4] [3 4] [2 4] [3 4] [1 4] [3 4] [2 4] [3 4]
                // Initial state is special one, then at step i do specified octave, with averaged samples from last update of that octave
                // Note that each sample has 2 iterations run, one to handle the lower octaves/reset, followed by one to always process the top octave, as shown in square brackets above.
                byte j;
                for (j = 0; j <= OCTAVES; j++) // search for "first" zero
                {
                    if (((1 << j) & i) == 0) { break; }
                }
                if (j > OCTAVES) { throw new Exception("BaseNoteFinderDFT octave scheduler encountered an algorithm fault."); }
                OctaveProcessingSchedule[i + 1] = (byte)(OCTAVES - j - 1);
            }
            SetupDone = true;
        }

        /// <summary>Updates the wave index steps for each bin, so that subsequent steps create a wave of the correct frequency.</summary>
        /// <remarks>Note that only the top octave's frequencies are looked at, all bins below are assumed to be consecutive octaves below.</remarks>
        /// <param name="frequencies">The frequency of each output bin</param>
        private static void UpdateBinsForDFT32(float[] frequencies)
        {
            for (int i = 0; i < TOTALBINS; i++)
            {
                float FreqAtBin = frequencies[(i % BINSPEROCT) + (BINSPEROCT * (OCTAVES - 1))]; // Repeat the top octave's frequencies for the whole range
                WaveIndexData[i * 2] = (ushort)(65536.0F / FreqAtBin);
            }
        }

        /// <summary>Calculates either one octave of sin/cos products, or updates all bins and removes old data, chosen by the current operation in <see cref="OctaveProcessingSchedule"/>.</summary>
        /// <remarks>Needs to run twice on each input sample, first to process reset/lower octaves, then again to process top octave.</remarks>
        /// <param name="sample">The input audio sample to process.</param>
        private static void HandleInt(short sample)
        {
            byte OctaveToProcess = OctaveProcessingSchedule[OctaveCycleCounter];
            OctaveCycleCounter++;
            OctaveCycleCounter &= (byte)(BINCYCLE - 1); // Increment the cycle counter, and wrap it back to 0 once we reach the end of the schedule.

            for (int i = 0; i < OCTAVES; i++) { OctaveResamplingAccumulators[i] += sample; }

            if (OctaveToProcess > 128) // Special case: Update all bins. This happens at the first cycle, and every 2^(OCTAVES) cycles thereafter.
            {
                for (int i = 0; i < TOTALBINS; i++)
                {
                    // Grab the most recent product sums
                    int SinSum = SampleTrigProductAccumulators[(i * 2) + 0];
                    int CosSum = SampleTrigProductAccumulators[(i * 2) + 1];

                    // Place the accumulator contents into the array for magnitude calculation and output.
                    SampleTrigProductSums[(i * 2) + 0] = SinSum;
                    SampleTrigProductSums[(i * 2) + 1] = CosSum;

                    // Decay the accumulators by a fraction of their value, as a cheap means of removing old data.
                    SampleTrigProductAccumulators[(i * 2) + 0] -= SinSum >> DFTIIR;
                    SampleTrigProductAccumulators[(i * 2) + 1] -= CosSum >> DFTIIR;
                }
            }
            else // Process only 1 octave, as decided per the schedule.
            {
                short ResampledInput = (short)(OctaveResamplingAccumulators[OctaveToProcess] >> (OCTAVES - OctaveToProcess));
                OctaveResamplingAccumulators[OctaveToProcess] = 0;

                short IndexOffset = (short)(OctaveToProcess * BINSPEROCT * 2); // Place of this octave's first bin in the overall array
                for (int i = 0; i < BINSPEROCT; i++) // Each bin in this 1 octave
                {
                    ushort StepSize = WaveIndexData[IndexOffset + (i * 2) + 0]; // Set by UpdateBinsForDFT32, contains (65536 / BinFreq)
                    byte SineTableIndex = (byte)(WaveIndexData[IndexOffset + (i * 2) + 1] >> 8); // Where in the table we need to look now
                    WaveIndexData[IndexOffset + (i * 2) + 1] += StepSize; // Shift our table index forward by 1 frequency-dependent step for next time

                    SampleTrigProductAccumulators[IndexOffset + (i * 2) + 0] += (SineLUT[SineTableIndex] * ResampledInput);

                    SineTableIndex += 64; // Offset by 1/4 wavelength to get the cosine value
                    SampleTrigProductAccumulators[IndexOffset + (i * 2) + 1] += (SineLUT[SineTableIndex] * ResampledInput);
                }
            }
        }

        /// <summary>Calculates the output magnitudes for all bins based on the most recent sin/cos product sums.</summary>
        private static void UpdateOutputBins32()
        {
            for (int i = 0; i < TOTALBINS; i++)
            {
                int SinProductSum = SampleTrigProductSums[(i * 2) + 0];
                int CosProductSum = SampleTrigProductSums[(i * 2) + 1];
                SinProductSum = Math.Abs(SinProductSum);
                CosProductSum = Math.Abs(CosProductSum);
                int Octave = i / BINSPEROCT;

                float MagnitudeSquared = ((float)SinProductSum * SinProductSum) + ((float)CosProductSum * CosProductSum);
                OutputBins[i] = MathF.Sqrt(MagnitudeSquared) / 65536.0F; // scale by 2^16
                OutputBins[i] /= (78 << DFTIIR) * (1 << Octave); // 78 is an arbitrary number selected by experimentation, which provides "about the right amount of dampening"
            }
        }

        /// <summary>Runs the DFT on any new audio data in the circular input buffer.</summary>
        /// <param name="binsOut">The DFT output bins. Length must always be equal to <see cref="TOTALBINS"/>.</param>
        /// <param name="binFrequencies">The frequency for each of the bins. Length must always be equal to <see cref="TOTALBINS"/>.</param>
        /// <param name="audioBuffer">The array to read audio data from. Only data added since last time will be read each time this function is called.</param>
        /// <param name="locationInAudioData">The location in the audio buffer to read up to, and where reading will continue on the next run.</param>
        public static void DoDFTProgressive32(ref float[] binsOut, float[] binFrequencies, float[] audioBuffer, int locationInAudioData)
        {
            OutputBins = binsOut;
            Array.Copy(LastRunOutput, binsOut, TOTALBINS);

            if (TOTALBINS != binsOut.Length) { throw new ArgumentOutOfRangeException($"Error: Bins were reconfigured. Only a constant number of bins is supported (configured for {TOTALBINS}, received request for {binsOut.Length})."); }
            if (!SetupDone)
            {
                SetupDFTProgressive32();
                UpdateBinsForDFT32(binFrequencies); // TODO this needs to be done any time the frequencies change. I am assuming they won't, which may not be true in some applications.
            }

            for (int i = LastBufferReadLocation; i != locationInAudioData; i = (i + 1) % audioBuffer.Length)
            {
                short InputSample = (short)(audioBuffer[i] * 4095);
                HandleInt(InputSample); // Handle reset or lower octaves (even indices in schedule)
                HandleInt(InputSample); // Handle top octave (odd indices in schedule)
            }

            UpdateOutputBins32();
            LastBufferReadLocation = locationInAudioData;
            Array.Copy(binsOut, LastRunOutput, TOTALBINS);
        }
    }
}