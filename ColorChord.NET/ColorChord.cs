using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ColorChord.NET
{
    public static class ColorChord
    {
        const int FreqBinCount = 24;
        const int Octaves = 5;
        const float DFT_Q = 20;
        const float DFT_Speedup = 1000;
        const float DFT_IIR = 0.6F;
        const float Amplify = 2;
        const float Slope = 0;
        const int FilterIterations = 2;
        const float FilterStrength = 0.5F;
        const float DefaultSigma = 1.4F;
        const int DecomposeIterations = 1000;

        public static void RunNoteFinder(float[] Stream, int Head, int Size)
        {
            int NotePeakCount = FreqBinCount / 2;
            int Freqs = FreqBinCount * Octaves;
            int MaxDists = FreqBinCount / 2;
            float[] DFTBins = new float[Freqs];
            float[] Frequencies = new float[Freqs];
            float[] OutBins = new float[Freqs];

            DoDFTProgressive32(ref DFTBins, ref Frequencies, Freqs, ref Stream, Head, Size, DFT_Q, DFT_Speedup);

            for (int i = 0; i < Freqs; i++)
            {
                OutBins[i] = (OutBins[i] * DFT_IIR + (DFTBins[i] * (1.0F - DFT_IIR) * Amplify * (1.0F + Slope * i)));
            }

            //Taper the first and last octaves.
            for (int i = 0; i < FreqBinCount; i++)
            {
                OutBins[i] *= (i + 1.0F) / FreqBinCount;
            }

            for (int i = 0; i < FreqBinCount; i++)
            {
                OutBins[Freqs - i - 1] *= (i + 1.0F) / FreqBinCount;
            }

            //Combine the bins into folded bins.
            float[] FoldedBins = new float[FreqBinCount];
            for (int i = 0; i < FreqBinCount; i++)
            {
                float amp = 0;
                for (int j = 0; j < Octaves; j++)
                {
                    amp += OutBins[i + j * FreqBinCount];
                }
                FoldedBins[i] = amp;
            }

            //This is here to reduce the number of false-positive hits.  It helps remove peaks that are meaningless.
            FilterFoldedBinsBlob(FoldedBins, FreqBinCount, FilterStrength, FilterIterations);

            NoteDists[] Dists = new NoteDists[MaxDists];
            for (int i = 0; i < MaxDists; i++)
            {
                Dists[i].taken = 0;
            }
            int DistsCount = DecomposeHistogram(FoldedBins, FreqBinCount, ref Dists, MaxDists, DefaultSigma, DecomposeIterations);


        }

        private static void FilterFoldedBinsBlob(float[] folded, int bins, float strength, int iter)
        {
            float[] tmp = new float[bins];
            int i, j;
            for (j = 0; j < iter; j++)
            {
                Array.Copy(folded, tmp, tmp.Length);
                for (i = 0; i < bins; i++)
                {
                    float right = tmp[(i + bins + 1) % bins];
                    float left = tmp[(i + bins - 1) % bins];
                    folded[i] = folded[i] * (1.0F - strength) + (left + right) * strength * 0.5F;
                }
            }
        }

        private static int DecomposeHistogram(float[] histogram, int bins, ref NoteDists[] out_dists, int max_dists, float default_sigma, int iterations)
        {
            //Step 1: Find the actual peaks.

            int i;
            int peak = 0;
            for (i = 0; i < bins; i++)
            {
                float offset = 0;
                float prev = histogram[(i - 1 + bins) % bins];
                float now = histogram[i];

                float next = histogram[(i + 1) % bins];

                if (prev > now || next > now) continue;
                if (prev == now && next == now) continue;

                //i is at a peak... 
                float totaldiff = ((now - prev) + (now - next));
                float porpdiffP = (now - prev) / totaldiff; //close to 0 = closer to this side... 0.5 = in the middle ... 1.0 away.
                float porpdiffN = (now - next) / totaldiff;

                if (porpdiffP < porpdiffN)
                {
                    //Closer to prev.
                    offset = -(0.5F - porpdiffP);
                }
                else
                {
                    offset = (0.5F - porpdiffN);
                }

                out_dists[peak].mean = i + offset;

                //XXX XXX TODO Examine difference or relationship of "this" and "totaldiff"
                out_dists[peak].amp = now * 4;
                //powf( totaldiff, .8) * 10;//powf( totaldiff, .5 )*4; //
                out_dists[peak].sigma = default_sigma;

                peak++;

            }

            for (i = peak; i < max_dists; i++)
            {
                out_dists[i].mean = -1;
                out_dists[i].amp = 0;
                out_dists[i].sigma = default_sigma;
            }

            return peak;
        }

        private struct NoteDists
        {
            public float amp;   //Amplitude of normal distribution
            public float mean;  //Mean of normal distribution
            public float sigma; //Sigma of normal distribution
            public byte taken; //Is distribution associated with any notes?
        };

        [DllImport("ColorChordLib.dll")]
        private static extern void DoDFTProgressive32(ref float[] OutBins, ref float[] Frequencies, int Bins, ref float[] DataBuffer, int DataBufferLoc, int DataBufferSize, float Q, float Speedup);
    }
}
