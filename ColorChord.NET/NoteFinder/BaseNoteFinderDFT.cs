using System;
using System.Diagnostics;

namespace ColorChord.NET.NoteFinder
{
    public static class BaseNoteFinderDFT
    {
		private const byte OCTAVES = 5;
		private const ushort FIXBPERO = 24;
		private const uint FIXBINS = (FIXBPERO * OCTAVES);
		private const uint BINCYCLE = (1 << OCTAVES);
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

        private static readonly ushort[] Sdatspace32A = new ushort[FIXBINS * 2];  //(advances,places) full revolution is 256. 8bits integer part 8bit fractional
		private static readonly int[] SampleTrigProductsFiltered = new int[FIXBINS * 2];  //(isses,icses)

		/// <summary>The sample*trig products, updated once per full schedule run.</summary>
		/// <remarks>This gets updated 1 out of 2^(OCTAVES) cycles, and since every sample uses 2 cycvles, this is 1 out of every 2^(OCTAVES - 1) input samples.</remarks>
		private static readonly int[] SampleTrigProducts = new int[FIXBINS * 2];  //(isses,icses)

		/// <summary>Holds the schedule for which octaves need to be proccessed in each cycle. The top octave needs to be calculated every cycle regardless, but other octaves are updated less frequently, and this array contains the octave index to process with each incoming sample cycle.</summary>
		private static readonly byte[] OctaveProcessingSchedule = new byte[BINCYCLE];

		private static readonly int[] OctaveSampleAccumulators = new int[OCTAVES];

		/// <summary>Keeps track of which cycle we are in, which dictates what octave(s) are processed in the current cycle according to <see cref="OctaveProcessingSchedule"/>.</summary>
		private static byte OctaveCycleCounter;

		/// <summary>Where in the input audio buffer we last read data. Used to know which samples to process at each run.</summary>
		private static int LastBufferReadLocation;

		/// <summary>The data in the output bins last from last time the DFT was run.</summary>
		private static readonly float[] LastRunOutput = new float[FIXBINS];

		/// <summary>Sets up <see cref="OctaveProcessingSchedule"/>. Must be done once before the first sample is processed, no need to redo it later.</summary>
		private static void SetupDFTProgressive32()
		{
			OctaveProcessingSchedule[0] = 0xFF; // Process all octaves in the first cycle and every 2^(OCTAVES) cycles thereafter.
			for (int i = 0; i < BINCYCLE - 1; i++)
			{
				// Example resulting sequence for OCTAVES = 5:
				//   255 4 3 4 2 4 3 4 1 4 3 4 2 4 3 4 0 4 3 4 2 4 3 4 1 4 3 4 2 4 3 4
				// Initial state is special one, then at step i do specified octave, with averaged samples from last update of that octave
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

		private static void UpdateBinsForDFT32(float[] frequencies)
		{
			for (int i = 0; i < FIXBINS; i++)
			{
				float freq = frequencies[(i % FIXBPERO) + (FIXBPERO * (OCTAVES - 1))];
				Sdatspace32A[i * 2] = (ushort)(65536.0F / freq);// / oneoveroctave; // TODO Cast added
			}
		}

		private static void HandleInt(short sample)
		{
			byte OctaveToProcess = OctaveProcessingSchedule[OctaveCycleCounter];
			OctaveCycleCounter++;
			OctaveCycleCounter &= (byte)(BINCYCLE - 1);

			for (int i = 0; i < OCTAVES; i++) { OctaveSampleAccumulators[i] += sample; }

			if (OctaveToProcess > 128) // Special case: Update all bins. This happens at the first cycle, and every 2^(OCTAVES) cycles thereafter.
			{
				for (int i = 0; i < FIXBINS; i++)
				{
					int SinVal = SampleTrigProductsFiltered[(i * 2) + 0];
					SampleTrigProducts[(i * 2) + 0] = SinVal;
					SampleTrigProductsFiltered[(i * 2) + 0] -= SinVal >> DFTIIR;

					int CosVal = SampleTrigProductsFiltered[(i * 2) + 1];
					SampleTrigProducts[(i * 2) + 1] = CosVal;
					SampleTrigProductsFiltered[(i * 2) + 1] -= CosVal >> DFTIIR;
				}
				return;
			}

			// Process a filtered sample for one of the octaves (specified by the schedule)
			short FilteredSample = (short)(OctaveSampleAccumulators[OctaveToProcess] >> (OCTAVES - OctaveToProcess));
			OctaveSampleAccumulators[OctaveToProcess] = 0;

			short IndexOffset = (short)(OctaveToProcess * FIXBPERO * 2); // Place of this octave's first bin in the overall array
			for (int i = 0; i < FIXBPERO; i++) // Each bin in this 1 octave
			{
				ushort adv = Sdatspace32A[IndexOffset + (i * 2) + 0]; // TODO Figure this out
				byte SineTableIndex = (byte)(Sdatspace32A[IndexOffset + (i * 2) + 1] >> 8); // TODO added cast
				Sdatspace32A[IndexOffset + (i * 2) + 1] += adv;
				SampleTrigProductsFiltered[IndexOffset + (i * 2) + 0] += (SineLUT[SineTableIndex] * FilteredSample);

				SineTableIndex += 64; // Offset by 1/4 wavelength to get the cosine value
				SampleTrigProductsFiltered[IndexOffset + (i * 2) + 1] += (SineLUT[SineTableIndex] * FilteredSample);
			}
		}

		private static void UpdateOutputBins32()
		{
			for (int i = 0; i < FIXBINS; i++)
			{
				int SinValue = SampleTrigProducts[(i * 2) + 0];
				int CosValue = SampleTrigProducts[(i * 2) + 1];
				SinValue = Math.Abs(SinValue);
				CosValue = Math.Abs(CosValue);
				int Octave = i / FIXBPERO;

				float MagnitudeSquared = ((float)SinValue * SinValue) + ((float)CosValue * CosValue);
				OutputBins[i] = MathF.Sqrt(MagnitudeSquared) / 65536.0F; // scale by 2^16, reasonable (but arbitrary attenuation)
				OutputBins[i] /= (78 << DFTIIR) * (1 << Octave); // TODO WTF is 78???
			}
		}

		/// <summary>Runs the DFT on any new audio data in the circular input buffer.</summary>
		/// <param name="binsOut">The DFT output bins. Length must always be equal to <see cref="FIXBINS"/>.</param>
		/// <param name="binFrequencies">The frequency for each of the bins. Length must always be equal to <see cref="FIXBINS"/>.</param>
		/// <param name="audioBuffer">The array to read audio data from. Only data added since last time will be read each time this function is called.</param>
		/// <param name="locationInAudioData">The location in the audio buffer to read up to, and where reading will continue on the next run.</param>
		public static void DoDFTProgressive32(ref float[] binsOut, float[] binFrequencies, float[] audioBuffer, int locationInAudioData)
		{
			//Array.Clear(binsOut, 0, binsOut.Length); // TODO see if this is needed (I don't think so)
			OutputBins = binsOut;
			Array.Copy(LastRunOutput, binsOut, FIXBINS);

			if (FIXBINS != binsOut.Length) { throw new ArgumentOutOfRangeException($"Error: Bins were reconfigured. Only a constant number of bins is supported (configured for {FIXBINS}, received request for {binsOut.Length})."); }
			if (!SetupDone) { SetupDFTProgressive32(); }
			UpdateBinsForDFT32(binFrequencies);

			for (int i = LastBufferReadLocation; i != locationInAudioData; i = (i + 1) % audioBuffer.Length)
			{
				short InputSample = (short)(audioBuffer[i] * 4095);
				HandleInt(InputSample);
				HandleInt(InputSample);
			}

			UpdateOutputBins32();
			LastBufferReadLocation = locationInAudioData;
			Array.Copy(binsOut, LastRunOutput, FIXBINS);
		}
    }
}