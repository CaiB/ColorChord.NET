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
        const float CompressCoefficient = 1;
        const float CompressExponent = 0.5F;
        const float NoteJumpability = 1.8F;
        const float NoteAttachFreqIIR = 0.3F;
        const float NoteAttachAmpIIR = 0.35F;

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

            //Compress/normalize dist_amps
            float total_dist = 0;

            for (int i = 0; i < DistsCount; i++)
            {
                total_dist += Dists[i].amp;
            }
            float muxer = (float)(CompressCoefficient / Math.Pow(total_dist * CompressCoefficient, CompressExponent));
            total_dist = muxer;
            for (int i = 0; i < DistsCount; i++)
            {
                Dists[i].amp *= total_dist;
            }

            Array.Sort(Dists);

            //We now have the positions and amplitudes of the normal distributions that comprise our spectrum.  IN SORTED ORDER!
            //dists_count = # of distributions
            //dists[].amp = amplitudes of the normal distributions
            //dists[].mean = positions of the normal distributions

            //We need to use this in a filtered manner to obtain the "note" peaks
            //note_peaks = total number of peaks.
            //note_positions[] = position of the note on the scale.
            //note_amplitudes[] = amplitudes of these note peaks.
            byte[] NoteFounds = new byte[NotePeakCount];

            //First try to find any close peaks.
            float[] NotePositions = new float[NotePeakCount];
            float[] NoteAmplitudes = new float[NotePeakCount];
            byte[] PeakToDistMap = new byte[NotePeakCount];
            int[] EnduringNoteID = new int[NotePeakCount];
            int CurrentNoteID = 1;
            for (int i = 0; i < NotePeakCount; i++)
            {
                for (byte j = 0; j < DistsCount; j++)
                {
                    if (Dists[j].taken == 0 && NoteFounds[i] == 0 && fabsloop(NotePositions[i], Dists[j].mean, FreqBinCount) < NoteJumpability && Dists[j].amp > 0.00001) //0.00001 for stability.
                    {
                        //Attach ourselves to this bin.
                        PeakToDistMap[i] = j;
                        Dists[j].taken = 1;
                        if (EnduringNoteID[i] == 0)
                            EnduringNoteID[i] = CurrentNoteID++;
                        NoteFounds[i] = 1;

                        NotePositions[i] = avgloop(NotePositions[i], (1.0F - NoteAttachFreqIIR), Dists[j].mean, NoteAttachFreqIIR, FreqBinCount);

                        //I guess you can't IIR this like normal.
                        ////note_positions[i] * (1.-note_attach_freq_iir) + dists[j].mean * note_attach_freq_iir;

                        NoteAmplitudes[i] = NoteAmplitudes[i] * (1.0F - NoteAttachAmpIIR) + Dists[j].amp * NoteAttachAmpIIR;
                        //XXX TODO: Consider: Always boost power, never reduce?
                        //					if( dists[i].amp > note_amplitudes[i] )
                        //						note_amplitudes[i] = dists[i].amp;
                    }
                }
            }

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

        //Take the absolute distance between two points on a torus.
        private static float fabsloop(float a, float b, float modl)
        {
            float fa = Math.Abs(a - b);
            fa = fa % modl;
            if (fa > modl / 2.0) { fa = modl - fa; }
            return fa;
        }

        //Get the weighted - average of two points on a torus.
        private static float avgloop(float pta, float ampa, float ptb, float ampb, float modl)
        {
            float amptot = ampa + ampb;

            //Determine if it should go linearly, or around the edge.
            if (Math.Abs(pta - ptb) > modl / 2.0)
            { //Loop around the outside.
                if (pta < ptb) { pta += modl; }
                else { ptb += modl; }
            }
            float modmid = (pta * ampa + ptb * ampb) / amptot;
            return modmid % modl;
        }

        private struct NoteDists : IComparable<NoteDists>
        {
            public float amp;   //Amplitude of normal distribution
            public float mean;  //Mean of normal distribution
            public float sigma; //Sigma of normal distribution
            public byte taken; //Is distribution associated with any notes?

            public int CompareTo(NoteDists other)
            {
                float v = this.amp - other.amp;
	            return ((0.0f < v) ? 1 : 0) - ((v < 0.0f) ? 1 : 0);
            }
        };

        [DllImport("ColorChordLib.dll")]
        private static extern void DoDFTProgressive32(ref float[] OutBins, ref float[] Frequencies, int Bins, ref float[] DataBuffer, int DataBufferLoc, int DataBufferSize, float Q, float Speedup);
    }
}
