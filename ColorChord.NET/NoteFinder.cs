using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ColorChord.NET
{
    public static class NoteFinder
    {
        public static float[] AudioBuffer = new float[8192]; // TODO: Make buffer size adjustable or auto-set based on sample rate (might be too short for super-high rates)
        public static int AudioBufferHead = 0; // Where in the buffer we are reading, as it is filled circularly.
        public static DateTime LastDataAdd;

        public static uint ShortestPeriod { get; private set; } = 100; // The speed at which the note finder needs to run, set by the fastest visualizer.

        private static bool KeepGoing = true;
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

        public static void Start()
        {
            KeepGoing = true;
            ProcessThread = new Thread(DoProcessing);
            ProcessThread.Start();
        }

        public static void Stop()
        {
            KeepGoing = false;
            ProcessThread.Join();
        }

        private static void DoProcessing()
        {
            Stopwatch Timer = new Stopwatch();
            while (KeepGoing)
            {
                Timer.Restart();
                RunNoteFinder();
                int WaitTime = (int)(ShortestPeriod - (Timer.ElapsedMilliseconds));
                if (WaitTime > 0) { Thread.Sleep(WaitTime); }
            }
        }

        #region ColorChord Magic

        public const int FreqBinCount = 24;
        const int Octaves = 5;
        const float DFT_Q = 20;
        const float DFT_Speedup = 1000;
        const float DFT_IIR = 0.75F;
        const float Amplify = 2;
        const float Slope = 0.1F;
        const int FilterIterations = 2;
        const float FilterStrength = 0.5F;
        const float DefaultSigma = 1.4F;
        const int DecomposeIterations = 1000;
        const float CompressCoefficient = 1;
        const float CompressExponent = 0.5F;
        const float NoteJumpability = 1.8F;
        const float NoteAttachFreqIIR = 0.3F;
        const float NoteAttachAmpIIR = 0.35F;
        const float NoteAttachAmpIIR2 = 0.25F;
        const float NoteCombineDistance = 0.5F;
        const float NoteMinAmplitude = 0.001F;
        const float NoteNewMinDistributionValue = 0.02F;
        const float NoteOutChop = 0.05F;
        const int BaseHz = 55;

        static int Freqs = FreqBinCount * Octaves;
        public static int NotePeakCount = FreqBinCount / 2;
        static int MaxDists = FreqBinCount / 2;
        public static float[] NotePositions = new float[NotePeakCount];
        public static float[] NoteAmplitudes = new float[NotePeakCount];
        static float[] NoteAmplitudesOut = new float[NotePeakCount];
        public static float[] NoteAmplitudes2 = new float[NotePeakCount];
        static byte[] PeakToDistMap = new byte[NotePeakCount];
        static int[] EnduringNoteID = new int[NotePeakCount];
        static byte[] NoteFounds = new byte[NotePeakCount];
        static float[] Frequencies = new float[Freqs];
        static float[] OutBins = new float[Freqs];
        static float[] FoldedBins = new float[FreqBinCount];
        static NoteDists[] Dists = new NoteDists[MaxDists];

        public static float[] RunNoteFinder()
        {
            float[] DFTBins = new float[Freqs];

            DoDFTProgressive32(DFTBins, Frequencies, Freqs, AudioBuffer, AudioBufferHead, AudioBuffer.Length, DFT_Q, DFT_Speedup);

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

            //First try to find any close peaks.
            NoteFounds = new byte[NotePeakCount];
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

            //Combine like-notes.
            for (int i = 0; i < NotePeakCount; i++)
            {
                //		printf( "%f %f %d\n", nf->note_amplitudes[i], nf->note_positions[i], nf->enduring_note_id[i] );
                for (int j = 0; j < NotePeakCount; j++)
                {
                    if (i == j) continue;
                    if (fabsloop(NotePositions[i], NotePositions[j], FreqBinCount) < NoteCombineDistance &&
                        NoteAmplitudes[i] > 0.0 &&
                        NoteAmplitudes[j] > 0.0)
                    {
                        int a;
                        int b;
                        if (NoteAmplitudes[i] > NoteAmplitudes[j])
                        {
                            a = i;
                            b = j;
                        }
                        else
                        {
                            b = i;
                            a = j;
                        }
                        float newp = avgloop(NotePositions[a], NoteAmplitudes[a], NotePositions[b], NoteAmplitudes[b], FreqBinCount);

                        //Combine B into A.
                        NoteAmplitudes[a] += NoteAmplitudes[b];
                        NotePositions[a] = newp;
                        NoteAmplitudes[b] = 0;
                        NotePositions[b] = -100;
                        EnduringNoteID[b] = 0;
                    }
                }
            }

            //Assign dead or decayed notes to new  peaks.
            for (int i = 0; i < NotePeakCount; i++)
            {
                if (NoteAmplitudes[i] < NoteMinAmplitude)
                {
                    EnduringNoteID[i] = 0;

                    //Find a new peak for this note.
                    for (int j = 0; j < DistsCount; j++)
                    {
                        if (Dists[j].taken == 0 && Dists[j].amp > NoteNewMinDistributionValue)
                        {
                            EnduringNoteID[i] = CurrentNoteID++;
                            Dists[j].taken = 1;
                            NoteAmplitudes[i] = Dists[j].amp;//min_note_amplitude + dists[j].amp * note_attach_amp_iir; //TODO: Should this jump?
                            NotePositions[i] = Dists[j].mean;
                            NoteFounds[i] = 1;
                        }
                    }
                }
            }

            //Any remaining notes that could not find a peak good enough must be decayed to oblivion.
            for (int i = 0; i < NotePeakCount; i++)
            {
                if (NoteFounds[i] == 0)
                {
                    NoteAmplitudes[i] = NoteAmplitudes[i] * (1.0F - NoteAttachAmpIIR);
                }

                NoteAmplitudes2[i] = NoteAmplitudes2[i] * (1.0F - NoteAttachAmpIIR2) + NoteAmplitudes[i] * NoteAttachAmpIIR2;

                if (NoteAmplitudes2[i] < NoteMinAmplitude)
                {
                    NoteAmplitudes[i] = 0;
                    NoteAmplitudes2[i] = 0;
                }
            }

            for (int i = 0; i < NotePeakCount; i++)
            {
                NoteAmplitudesOut[i] = NoteAmplitudes[i] - NoteOutChop;
                if (NoteAmplitudesOut[i] < 0)
                {
                    NoteAmplitudesOut[i] = 0;
                }
            }

            return NoteAmplitudesOut;
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
                return ((v > 0) ? -1 : ((v < 0) ? 1 : 0));
            }
        };

        [DllImport("ColorChordLib.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DoDFTProgressive32([In, Out] float[] OutBins, [In, Out] float[] Frequencies, int Bins, float[] DataBuffer, int DataBufferLoc, int DataBufferSize, float Q, float Speedup);
        #endregion
    }
}
