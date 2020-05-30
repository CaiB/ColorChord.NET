using System;
using System.Collections.Generic;
using System.Text;

namespace ColorChord.NET.NoteFinder
{
    /// <summary> My own note finder implementation. </summary>
    public class ShinNoteFinderDFT
    {
        /// <summary> The number of octaves we will analyze. </summary>
        public byte OctaveCount = 5;

        /// <summary> The number of frequency bins of data we will output per octave. </summary>
        public byte BinsPerOctave = 24;

        /// <summary> How long our sample window is. </summary>
        public ushort WindowSize = 8192;

        /// <summary> The sample rate of the incoming audio signal, and our reference waveforms. </summary>
        public uint SampleRate = 48000;

        /// <summary> The total number of bins over all octaves. </summary>
        public ushort BinCount => (ushort)(this.OctaveCount * this.BinsPerOctave);

        /// <summary> The frequencies of all bins, in Hz. </summary>
        /// <remarks> Length = <see cref="BinCount"/>. </remarks>
        private float[] BinFrequencies;

        /// <summary> The reference sine waveforms for each bin. </summary>
        /// <remarks> Dimensions = [<see cref="BinsPerOctave"/>, <see cref="WindowSize"/>]. </remarks>
        private float[,] SinTable;

        /// <summary> The reference cosine waveforms for each bin. </summary>
        /// <remarks> Dimensions = [<see cref="BinsPerOctave"/>, <see cref="WindowSize"/>]. </remarks>
        private float[,] CosTable;

        /// <summary> Where in the circular buffers we are currently writing to/calculating for. </summary>
        private uint HeadLocation;

        /// <summary> The circular buffer containing input audio data. Length <see cref="WindowSize"/>. </summary>
        private float[] AudioBuffer;

        /// <summary> The product of (sin reference) * (input signal) at each sample, for each bin. </summary>
        /// <remarks> Dimensions = [<see cref="BinCount"/>, <see cref="WindowSize"/>]. </remarks>
        private float[,] SinProducts;

        /// <summary> The product of (cos reference) * (input signal) at each sample, for each bin. </summary>
        /// <remarks> Dimensions = [<see cref="BinCount"/>, <see cref="WindowSize"/>]. </remarks>
        private float[,] CosProducts;

        private float[] PrevSinSum, PrevCosSum;

        /// <summary> The magnitudes of summed sin and cos products, for each bin. </summary>
        /// <remarks> Length = <see cref="BinCount"/>. </remarks>
        private float[] Magnitudes;

        public ShinNoteFinderDFT()
        {
            
        }

        /// <summary> Fills <see cref="BinFrequencies"/> with the frequency (in Hz) of each bin. </summary>
        /// <remarks> Depends on <see cref="BinCount"/>, <see cref="BinsPerOctave"/>. </remarks>
        /// <param name="baseFrequency"> The frequency of the lowest bin. </param>
        public void CalculateFrequencies(float baseFrequency)
        {
            this.BinFrequencies = new float[this.BinCount];
            this.BinFrequencies[0] = baseFrequency;
            for (ushort Bin = 1; Bin < this.BinCount; Bin++) { this.BinFrequencies[Bin] = (float)(this.BinFrequencies[Bin - 1] * Math.Pow(2, (1F / this.BinsPerOctave))); }
        }

        /// <summary> Fills the reference waveform tables with data needed to do DFT calculations. </summary>
        /// <remarks> This needs to be done whenever any of <see cref="SampleRate"/>, <see cref="BinsPerOctave"/>, or <see cref="WindowSize"/> change. </remarks>
        public void FillReferenceTables()
        {
            this.SinTable = new float[BinsPerOctave, WindowSize];
            this.CosTable = new float[BinsPerOctave, WindowSize];
            float Coefficient = (float)(Math.PI * 2 / this.SampleRate);
            for (byte Bin = 0; Bin < this.BinsPerOctave; Bin++)
            {
                for (ushort Sample = 0; Sample < this.WindowSize; Sample++)
                {
                    this.SinTable[Bin, Sample] = (float)Math.Sin(Sample * this.BinFrequencies[Bin] * Coefficient);
                    this.CosTable[Bin, Sample] = (float)Math.Cos(Sample * this.BinFrequencies[Bin] * Coefficient);
                }
            }
        }

        /// <summary> Sets up and clears the storage for input audio data, and internal calculated data. </summary>
        /// <remarks> This can be called if you want to reset the NoteFinder state (e.g. during a period of silence). </remarks>
        public void PrepareSampleStorage()
        {
            this.HeadLocation = 0;
            this.AudioBuffer = new float[this.WindowSize];
            this.SinProducts = new float[this.BinCount, this.WindowSize];
            this.CosProducts = new float[this.BinCount, this.WindowSize];
            this.PrevSinSum = new float[this.BinCount];
            this.PrevCosSum = new float[this.BinCount];
            this.Magnitudes = new float[this.BinCount];
        }

        /// <summary> Adds a single new audio sample. </summary>
        /// <param name="sample"> The sample to add to the buffer. </param>
        /// <param name="doMagCalc"> Whether to do the final magnitude calculation or not. This is only needed on the last sample being added in a batch, or for all samples added individually. </param>
        public void AddSample(float sample, bool doMagCalc = true)
        {
            this.AudioBuffer[this.HeadLocation] = sample;
            CalculateProducts();
            this.HeadLocation = (ushort)((this.HeadLocation + 1) % this.WindowSize);
            if (doMagCalc) { CalculateMagnitudes(); }
        }

        /// <summary> Adds all samples in an array. </summary>
        /// <param name="samples"> The samples to add to the buffer. </param>
        public void AddSamples(float[] samples)
        {
            for (uint i = 0; i < samples.Length; i++)
            {
                AddSample(samples[i], (i == samples.Length - 1));
            }
        }

        /// <summary> Calculates sin and cos products at the current buffer head location. </summary>
        private void CalculateProducts()
        {
            for (byte Bin = 0; Bin < this.BinsPerOctave; Bin++) // TODO: This needs to be calculated for all octaves.
            {
                // Remove the previous data from the sum.
                this.PrevSinSum[Bin] -= this.SinProducts[Bin, this.HeadLocation];
                this.PrevCosSum[Bin] -= this.CosProducts[Bin, this.HeadLocation];

                // Calculate new data.
                this.SinProducts[Bin, this.HeadLocation] = this.SinTable[Bin, this.HeadLocation] * this.AudioBuffer[this.HeadLocation];
                this.CosProducts[Bin, this.HeadLocation] = this.CosTable[Bin, this.HeadLocation] * this.AudioBuffer[this.HeadLocation];

                // Add the new data to the sum.
                this.PrevSinSum[Bin] += this.SinProducts[Bin, this.HeadLocation];
                this.PrevCosSum[Bin] += this.CosProducts[Bin, this.HeadLocation];
            }
        }

        /// <summary> Updates <see cref="Magnitudes"/> with the current magnitude of sin and cos sum data. </summary>
        private void CalculateMagnitudes()
        {
            for (ushort Bin = 0; Bin < this.BinCount; Bin++)
            {
                this.Magnitudes[Bin] = (float)Math.Sqrt((this.PrevSinSum[Bin] * this.PrevSinSum[Bin]) + (this.PrevCosSum[Bin] * this.PrevCosSum[Bin]));
            }
        }

        public float[] GetBins()
        {
            return this.Magnitudes;
        }
    }
}
