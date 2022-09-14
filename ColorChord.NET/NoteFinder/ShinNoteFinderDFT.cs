using ColorChord.NET.API;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
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
        /// <remarks> This must be divisible by 2 ^ <see cref="OctaveCount"/>. </remarks>
        public ushort WindowSize = 8192;

        /// <summary> The sample rate of the incoming audio signal, and our reference waveforms. </summary>
        public uint SampleRate = 48000;

        /// <summary> The total number of bins over all octaves. </summary>
        public ushort BinCount => (ushort)(this.OctaveCount * this.BinsPerOctave);

        /// <summary> Gets the index of the first bin in the topmost octave. </summary>
        private ushort StartOfTopOctave => (ushort)((this.OctaveCount - 1) * this.BinsPerOctave);

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

        /// <summary> The buffers for lower octaves, with downsampled audio. </summary>
        /// <remarks>
        /// Organized in order of descending octaves. Each step's buffer size is half of the previous.
        /// i.e. Length[x] = <see cref="WindowSize"/> / (2 ^ x + 1)
        /// </remarks>
        private float[][] SmallerAudioBuffers;

        /// <summary> The product of (sin reference) * (input signal) at each sample, for each bin. </summary>
        /// <remarks> Dimensions = [<see cref="BinCount"/>, <see cref="WindowSize"/>]. </remarks>
        private float[,] SinProducts;

        /// <summary> The product of (cos reference) * (input signal) at each sample, for each bin. </summary>
        /// <remarks> Dimensions = [<see cref="BinCount"/>, <see cref="WindowSize"/>]. </remarks>
        private float[,] CosProducts;

        /// <summary> The sin and cos products at for each bin, summed over the entire window. </summary>
        /// <remarks> Length = <see cref="BinCount"/>. </remarks>
        private float[] PrevSinSum, PrevCosSum;

        /// <summary> The magnitudes of summed sin and cos products, for each bin. </summary>
        /// <remarks> Length = <see cref="BinCount"/>. </remarks>
        public float[] Magnitudes;

        /// <summary> Stores the current cycle count, which is used in determining which lower octaves need to be updated. </summary>
        private uint CycleCount = 0;

        public ShinNoteFinderDFT()
        {
            Log.Info("Starting ShinNoteFinder DFT module");
        }

        /// <summary> Fills <see cref="BinFrequencies"/> with the frequency (in Hz) of each bin. </summary>
        /// <remarks> Depends on <see cref="BinCount"/>, <see cref="BinsPerOctave"/>. </remarks>
        /// <param name="baseFrequency"> The frequency of the lowest bin. </param>
        public void CalculateFrequencies(float baseFrequency)
        {
            this.BinFrequencies = new float[this.BinCount];
            for (ushort Bin = 0; Bin < this.BinCount; Bin++)
            {
                this.BinFrequencies[Bin] = (float)(baseFrequency * Math.Pow(2, ((float)Bin / this.BinsPerOctave)));
            }
        }

        /// <summary> Fills the reference waveform tables with data needed to do DFT calculations. </summary>
        /// <remarks> This needs to be done whenever any of <see cref="SampleRate"/>, <see cref="BinsPerOctave"/>, or <see cref="WindowSize"/> change. </remarks>
        public void FillReferenceTables()
        {
            this.SinTable = new float[BinsPerOctave, WindowSize];
            this.CosTable = new float[BinsPerOctave, WindowSize];
            float Coefficient = MathF.PI * 2 / this.SampleRate;
            for (ushort Bin = 0; Bin < this.BinsPerOctave; Bin++)
            {
                for (ushort Sample = 0; Sample < this.WindowSize; Sample++)
                {
                    this.SinTable[Bin, Sample] = MathF.Sin(Sample * this.BinFrequencies[Bin + this.StartOfTopOctave] * Coefficient);
                    this.CosTable[Bin, Sample] = MathF.Cos(Sample * this.BinFrequencies[Bin + this.StartOfTopOctave] * Coefficient);
                }
            }
        }

        /// <summary> Sets up and clears the storage for input audio data, and internal calculated data. </summary>
        /// <remarks> This can be called if you want to reset the NoteFinder state (e.g. during a period of silence). </remarks>
        public void PrepareSampleStorage()
        {
            this.HeadLocation = 0;
            this.CycleCount = 0;
            this.AudioBuffer = new float[this.WindowSize];
            this.SmallerAudioBuffers = new float[this.OctaveCount - 1][];
            for (int i = 0; i < this.OctaveCount - 1; i++) { this.SmallerAudioBuffers[i] = new float[this.WindowSize >> (i + 1)]; }
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
            this.CycleCount++;

            if (doMagCalc) { CalculateMagnitudes(); }
        }

        /// <summary> Adds all samples in an array. </summary>
        /// <param name="samples"> The samples to add to the buffer. </param>
        public void AddSamples(float[] samples)
        {
            if (samples == null || samples.Length == 0) { return; }

            for (uint i = 0; i < samples.Length; i++)
            {
                AddSample(samples[i], false); // Don't bother doing the final calculations except for the last sample.
            }
            AddSample(samples[samples.Length - 1], true);
        }

        /// <summary> Calculates sin and cos products at the current buffer head location. </summary>
        private void CalculateProducts()
        {
            // Calculate the top octave.
            for (ushort Bin = this.StartOfTopOctave; Bin < this.BinCount; Bin++)
            {
                // Remove the previous data from the sum.
                this.PrevSinSum[Bin] -= this.SinProducts[Bin, this.HeadLocation];
                this.PrevCosSum[Bin] -= this.CosProducts[Bin, this.HeadLocation];

                // Calculate new data.
                ushort TableSlot = (ushort)(Bin % this.BinsPerOctave);
                this.SinProducts[Bin, this.HeadLocation] = this.SinTable[TableSlot, this.HeadLocation] * this.AudioBuffer[this.HeadLocation];
                this.CosProducts[Bin, this.HeadLocation] = this.CosTable[TableSlot, this.HeadLocation] * this.AudioBuffer[this.HeadLocation];

                // Add the new data to the sum.
                this.PrevSinSum[Bin] += this.SinProducts[Bin, this.HeadLocation];
                this.PrevCosSum[Bin] += this.CosProducts[Bin, this.HeadLocation];
            }

            uint NextCycle = this.CycleCount + 1;

            // Calculate the other octaves if needed.
            // [Down] is how many octaves down we are from the top. This makes subsequent math nicer.
            // E.g. in the case of 5 octaves, [Down] values would be: Lowest Octave-> [4 3 2 1 x] <-Highest Octave
            for (byte Down = 1; Down < this.OctaveCount; Down++)
            {
                // If this octave needs to be calculated this cycle
                if (((this.CycleCount >> (Down - 1)) & 0b1) == 0b1 &&
                    ((      NextCycle >> (Down - 1)) & 0b1) == 0b0) // If this is the last cycle where the corresponding bit has a 1
                {
                    uint HeadLocationCondensed = this.HeadLocation >> Down; // Buffer head location in this new buffer to match with the main buffer location.

                    // TODO: Use a better averaging method
                    if (Down > 1) // If we are averaging samples from another condensed buffer
                    {
                        this.SmallerAudioBuffers[Down - 1][HeadLocationCondensed] =
                            this.SmallerAudioBuffers[Down - 2][HeadLocationCondensed << 1] +
                            this.SmallerAudioBuffers[Down - 2][(HeadLocationCondensed << 1) + 1];
                    }
                    else // If we are averaging samples from the main buffer
                    {
                        this.SmallerAudioBuffers[0][HeadLocationCondensed] =
                            this.AudioBuffer[this.HeadLocation] +
                            this.AudioBuffer[this.HeadLocation - 1];
                    }

                    ushort BinOffset = (ushort)((this.OctaveCount - (Down + 1)) * this.BinsPerOctave); // The bottom bin in this octave
                    for (ushort Bin = BinOffset; Bin < BinOffset + this.BinsPerOctave; Bin++)
                    {
                        // Remove the previous data from the sum.
                        this.PrevSinSum[Bin] -= this.SinProducts[Bin, HeadLocationCondensed];
                        this.PrevCosSum[Bin] -= this.CosProducts[Bin, HeadLocationCondensed];

                        // Calculate new data.
                        ushort TableSlot = (ushort)(Bin % this.BinsPerOctave);
                        this.SinProducts[Bin, HeadLocationCondensed] = this.SinTable[TableSlot, HeadLocationCondensed] * this.SmallerAudioBuffers[Down - 1][HeadLocationCondensed];
                        this.CosProducts[Bin, HeadLocationCondensed] = this.CosTable[TableSlot, HeadLocationCondensed] * this.SmallerAudioBuffers[Down - 1][HeadLocationCondensed];

                        // Add the new data to the sum.
                        this.PrevSinSum[Bin] += this.SinProducts[Bin, HeadLocationCondensed];
                        this.PrevCosSum[Bin] += this.CosProducts[Bin, HeadLocationCondensed];
                    }
                }
            }
        }

        /// <summary> Updates <see cref="Magnitudes"/> with the current magnitude of sin and cos sum data. </summary>
        private void CalculateMagnitudes()
        {
            for (ushort Bin = 0; Bin < this.BinCount; Bin++)
            {
                this.Magnitudes[Bin] = MathF.Sqrt((this.PrevSinSum[Bin] * this.PrevSinSum[Bin]) + (this.PrevCosSum[Bin] * this.PrevCosSum[Bin]));
            }
        }

        //public float[] GetBins() => this.Magnitudes;

        // TODO: Remove this or move elsewhere, only for testing
        public void SaveData()
        {
            using (StreamWriter Writer = new("testdata.csv"))
            {
                for (uint i = 0; i < this.AudioBuffer.Length; i++)
                {
                    string Line = string.Join(',',
                        this.AudioBuffer[i],
                        i < this.SmallerAudioBuffers[0].Length ? this.SmallerAudioBuffers[0][i] : 0,
                        i < this.SmallerAudioBuffers[1].Length ? this.SmallerAudioBuffers[1][i] : 0,
                        i < this.SmallerAudioBuffers[2].Length ? this.SmallerAudioBuffers[2][i] : 0,
                        i < this.SmallerAudioBuffers[3].Length ? this.SmallerAudioBuffers[3][i] : 0);
                    Writer.WriteLine(Line);
                }
                Writer.Flush();
            }
        }
    }
}
