using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET.NoteFinder
{
    class BaseNoteFinderDFT
    {
		private const int OCTAVES = 5;
		private const int FIXBPERO = 24;
		private const int FIXBINS = (FIXBPERO * OCTAVES);
		private const int BINCYCLE = (1 << OCTAVES);
		private const int DFTIIR = 6;

		static float[] goutbins;

		ushort[] embeddedbins32 = new ushort[FIXBINS];

		static byte Sdonefirstrun;

		//A table of precomputed sin() values.  Ranging -1500 to +1500
		//If we increase this, it may cause overflows elsewhere in code.
		readonly short[] Ssinonlytable = new short[] {
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
		  -292,  -256,  -220,  -183,  -147,  -110,   -73,   -37,};

		ushort[] Sdatspace32A = new ushort[FIXBINS * 2];  //(advances,places) full revolution is 256. 8bits integer part 8bit fractional
		int[] Sdatspace32B = new int[FIXBINS * 2];  //(isses,icses)

		//This is updated every time the DFT hits the octavecount, or 1 out of (1<<OCTAVES) times which is (1<<(OCTAVES-1)) samples
		int[] Sdatspace32BOut = new int[FIXBINS * 2];  //(isses,icses)

		//Sdo_this_octave is a scheduling state for the running SIN/COS states for
		//each bin.  We have to execute the highest octave every time, however, we can
		//get away with updating the next octave down every-other-time, then the next
		//one down yet, every-other-time from that one.  That way, no matter how many
		//octaves we have, we only need to update FIXBPERO*2 DFT bins.
		static byte[] Sdo_this_octave = new byte[BINCYCLE];

		static int[] Saccum_octavebins = new int[OCTAVES];
		static byte Swhichoctaveplace;

		//
		ushort[] embeddedbins = new ushort[FIXBINS];

		//From: http://stackoverflow.com/questions/1100090/looking-for-an-efficient-integer-square-root-algorithm-for-arm-thumb2
		//  for sqrt approx but also suggestion for quick norm approximation that would work in this DFT

		void UpdateOutputBins32()
		{
			int i;
			//int32_t* ipt = &Sdatspace32BOut[0];
			for (i = 0; i < FIXBINS; i++)
			{
				int isps = Sdatspace32BOut[(i * 2) + 0]; //keep 32 bits
				int ispc = Sdatspace32BOut[(i * 2) + 1];
				// take absolute values
				isps = isps < 0 ? -isps : isps;
				ispc = ispc < 0 ? -ispc : ispc;
				int octave = i / FIXBPERO;

				//If we are running DFT32 on regular ColorChord, then we will need to
				//also update goutbins[]... But if we're on embedded systems, we only
				//update embeddedbins32.
				// convert 32 bit precision isps and ispc to floating point
				float mux = ((float)isps * (float)isps) + ((float)ispc * (float)ispc);
				goutbins[i] = MathF.Sqrt(mux) / 65536.0F; // scale by 2^16
													//reasonable (but arbitrary attenuation)
				goutbins[i] /= (78 << DFTIIR) * (1 << octave);

				// using full 32 bit precision for isps and ispc
				uint rmux = (uint)(isps > ispc ? isps + (ispc >> 1) : ispc + (isps >> 1)); // TODO Cast added
				rmux = rmux >> 16; // keep most significant 16 bits

				//bump up all outputs here, so when we nerf it by bit shifting by
				//octave we don't lose a lot of detail.
				rmux = rmux << 1;

				embeddedbins32[i] = (ushort)(rmux >> octave); // TODO Cast added
			}
		}

		void HandleInt(short sample) // TODO un-static-ed
		{
			int i;
			ushort adv;
			byte localipl;
			short filteredsample;

			byte oct = Sdo_this_octave[Swhichoctaveplace];
			Swhichoctaveplace++;
			Swhichoctaveplace &= BINCYCLE - 1;

			for (i = 0; i < OCTAVES; i++)
			{
				Saccum_octavebins[i] += sample;
			}

			if (oct > 128)
			{
				//Special: This is when we can update everything.
				//This gets run once out of every (1<<OCTAVES) times.
				// which is half as many samples
				//It handles updating part of the DFT.
				//It should happen at the very first call to HandleInit
				//int32_t* bins = &Sdatspace32B[0];
				//int32_t* binsOut = &Sdatspace32BOut[0];

				for (i = 0; i < FIXBINS; i++)
				{
					//First for the SIN then the COS.
					int val = Sdatspace32B[(i * 2) + 0];
					Sdatspace32BOut[(i * 2) + 0] = val;
					Sdatspace32B[(i * 2) + 0] -= val >> DFTIIR;

					val = Sdatspace32B[(i * 2) + 1];
					Sdatspace32BOut[(i * 2) + 1] = val;
					Sdatspace32B[(i * 2) + 1] -= val >> DFTIIR;
				}
				return;
			}

			// process a filtered sample for one of the octaves
			//uint16_t* dsA = &Sdatspace32A[oct * FIXBPERO * 2];
			//int32_t* dsB = &Sdatspace32B[oct * FIXBPERO * 2];

			filteredsample = (short)(Saccum_octavebins[oct] >> (OCTAVES - oct)); // TODO Added cast
			Saccum_octavebins[oct] = 0;

			for (i = 0; i < FIXBPERO; i++)
			{
				adv = Sdatspace32A[(oct * FIXBPERO * 2) + (i * 2) + 0];
				localipl = (byte)(Sdatspace32A[(oct * FIXBPERO * 2) + (i * 2) + 1] >> 8); // TODO added cast
				Sdatspace32A[(oct * FIXBPERO * 2) + (i * 2) + 1] += adv;

				Sdatspace32B[(oct * FIXBPERO * 2) + (i * 2) + 0] += (Ssinonlytable[localipl] * filteredsample);
				//Get the cosine (1/4 wavelength out-of-phase with sin)
				localipl += 64;
				Sdatspace32B[(oct * FIXBPERO * 2) + (i * 2) + 1] += (Ssinonlytable[localipl] * filteredsample);
			}
		}

		int SetupDFTProgressive32()
		{
			int i;
			int j;

			Sdonefirstrun = 1;
			Sdo_this_octave[0] = 0xff;
			for (i = 0; i < BINCYCLE - 1; i++)
			{
				// Sdo_this_octave = 
				// 255 4 3 4 2 4 3 4 1 4 3 4 2 4 3 4 0 4 3 4 2 4 3 4 1 4 3 4 2 4 3 4 is case for 5 octaves.
				// Initial state is special one, then at step i do octave = Sdo_this_octave with averaged samples from last update of that octave
				//search for "first" zero

				for (j = 0; j <= OCTAVES; j++)
				{
					if (((1 << j) & i) == 0) break;
				}
				if (j > OCTAVES)
				{
					Console.WriteLine("Error: algorithm fault.\n");
					Environment.Exit(-1); // TODO Consider not doing this
					return -1;
				}
				Sdo_this_octave[i + 1] = (byte)(OCTAVES - j - 1); // TODO Cast added
			}
			return 0;
		}



		void UpdateBins32( ushort[] frequencies )
		{
			int i;
				int imod = 0;
			for(i = 0; i<FIXBINS; i++, imod++ )
			{
				if (imod >= FIXBPERO) imod=0;
				ushort freq = frequencies[imod];
				Sdatspace32A[i * 2] = freq;// / oneoveroctave;
			}
		}

		void PushSample32(short dat)
		{
			HandleInt(dat);
			HandleInt(dat);
		}



		void UpdateBinsForDFT32( float[] frequencies)
		{
			int i;
			for (i = 0; i < FIXBINS; i++)
			{
				float freq = frequencies[(i % FIXBPERO) + (FIXBPERO * (OCTAVES - 1))];
				Sdatspace32A[i * 2] = (ushort)(65536.0F / freq);// / oneoveroctave; // TODO Cast added
			}
		}

		static float[] DoDFTProgressive32_backupbins = new float[FIXBINS];
		static int DoDFTProgressive32_last_place;
		public void DoDFTProgressive32(ref float[] outbins, float[] frequencies, int bins, float[] databuffer, int place_in_data_buffer, int size_of_data_buffer, float q, float speedup)
		{
			//static float backupbins[FIXBINS];
			int i;
			//static int last_place;

			//memset(outbins, 0, bins * sizeof(float));
			Array.Clear(outbins, 0, bins);

			goutbins = outbins;

			//memcpy(outbins, DoDFTProgressive32_backupbins, FIXBINS * 4);
			Array.Copy(DoDFTProgressive32_backupbins, outbins, FIXBINS);

			if (FIXBINS != bins)
			{
                Console.WriteLine("Error: Bins was reconfigured.  skippy requires a constant number of bins ({0} != {1}).\n", FIXBINS, bins);
				return;
			}

			if (Sdonefirstrun == 0)
			{
				SetupDFTProgressive32();
				Sdonefirstrun = 1;
			}

			UpdateBinsForDFT32(frequencies);

			for (i = DoDFTProgressive32_last_place; i != place_in_data_buffer; i = (i + 1) % size_of_data_buffer)
			{
				short ifr1 = (short)(((databuffer[i])) * 4095);
				HandleInt(ifr1);
				HandleInt(ifr1);
			}

			UpdateOutputBins32();

			DoDFTProgressive32_last_place = place_in_data_buffer;

			//memcpy(DoDFTProgressive32_backupbins, outbins, FIXBINS * 4);
			Array.Copy(outbins, DoDFTProgressive32_backupbins, FIXBINS);
		}
    }
}
