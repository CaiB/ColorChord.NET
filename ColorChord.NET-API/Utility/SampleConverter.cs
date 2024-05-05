using System.Diagnostics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;
using System.Runtime.InteropServices;

namespace ColorChord.NET.API.Utility;

public static class SampleConverter
{
    /// <summary> Takes a buffer of <see cref="short"/> audio frames, and mixes all channels down to mono into a <see cref="short"/> buffer. </summary>
    /// <param name="channelCount"> The number of audio channels in the input buffer, any number 1 or above is supported. </param>
    /// <param name="inputFramesAvailable"> How many frames of audio to process from the input buffer. </param>
    /// <param name="inputBuffer"> The buffer to read data from. Length must be at least inputFramesAvailable * channelCount. </param>
    /// <param name="outputBuffer"> The buffer to write the processed data to. Capacity must be at least inputFramesAvailable. </param>
    public static unsafe void ShortToShortMixdown(int channelCount, uint inputFramesAvailable, Span<float> inputBuffer, short[] outputBuffer)
    {
        fixed (float* BufferPtr = inputBuffer) { ShortToShortMixdown(channelCount, inputFramesAvailable, (byte*)BufferPtr, outputBuffer); }
    }

    /// <summary> Takes a buffer of <see cref="short"/> audio frames, and mixes all channels down to mono into a <see cref="short"/> buffer. </summary>
    /// <param name="channelCount"> The number of audio channels in the input buffer, any number 1 or above is supported. </param>
    /// <param name="inputFramesAvailable"> How many frames of audio to process from the input buffer. </param>
    /// <param name="inputBuffer"> The buffer to read data from. Length must be at least inputFramesAvailable * channelCount * sizeof(short). </param>
    /// <param name="outputBuffer"> The buffer to write the processed data to. Capacity must be at least inputFramesAvailable. </param>
    public static unsafe void ShortToShortMixdown(int channelCount, uint inputFramesAvailable, byte* inputBuffer, short[] outputBuffer)
    {
        IntPtr EndOfBuffer;

        if (channelCount == 1) { Marshal.Copy((IntPtr)inputBuffer, outputBuffer, 0, (int)inputFramesAvailable); }
        else if (channelCount == 2)
        {
            const int BYTES_PER_FRAME = 4;
            EndOfBuffer = (nint)(inputBuffer + (inputFramesAvailable * BYTES_PER_FRAME));
            uint Frame = 0;
            if (Avx2.IsSupported)
            {
                Vector256<short> Ones = Vector256.Create((short)1);
                uint EndFrame = inputFramesAvailable - 16;
                while (Frame <= EndFrame)
                {
                    Debug.Assert((IntPtr)inputBuffer + ((Frame + 8) * BYTES_PER_FRAME) < EndOfBuffer, "Reading out of bounds");
                    Vector256<short> StereoData1 = Vector256.Load((short*)(inputBuffer + (Frame * BYTES_PER_FRAME)));
                    Vector256<short> StereoData2 = Vector256.Load((short*)(inputBuffer + ((Frame + 8) * BYTES_PER_FRAME)));
                    Vector256<int> Lower = Avx2.ShiftRightArithmetic(Avx2.MultiplyAddAdjacent(StereoData1, Ones), 1);
                    Vector256<int> Upper = Avx2.ShiftRightArithmetic(Avx2.MultiplyAddAdjacent(StereoData2, Ones), 1);
                    Vector256<short> Combined = Avx2.PackSignedSaturate(Lower, Upper);
                    Vector256<short> Output = Avx2.Permute4x64(Combined.AsInt64(), 0b11011000).AsInt16();
                    Output.StoreUnsafe(ref outputBuffer[Frame]);
                    Frame += 16;
                }
            }
            while (Frame < inputFramesAvailable)
            {
                Debug.Assert((nint)(((short*)inputBuffer) + (Frame * 2) + 1) < EndOfBuffer, "Reading out of bounds");
                short Left = *(((short*)inputBuffer) + (Frame * 2));
                short Right = *(((short*)inputBuffer) + (Frame * 2) + 1);
                outputBuffer[Frame] = (short)((Left + Right) / 2);
                Frame++;
            }
        }
        else
        {
            EndOfBuffer = (nint)(inputBuffer + (inputFramesAvailable * sizeof(short) * channelCount));
            for (uint Frame = 0; Frame < inputFramesAvailable; Frame++)
            {
                int Mixdown = 0;
                for (int Channel = 0; Channel < channelCount; Channel++)
                {
                    Debug.Assert((nint)(((short*)inputBuffer) + (Frame * channelCount) + Channel) < EndOfBuffer, "Reading out of bounds");
                    Mixdown += *(((short*)inputBuffer) + (Frame * channelCount) + Channel);
                }
                outputBuffer[Frame] = (short)(Mixdown / channelCount);
            }
        }
    }

    /// <summary> Takes a buffer of <see cref="float"/> audio frames, and mixes all channels down to mono into a <see cref="short"/> buffer. </summary>
    /// <param name="channelCount"> The number of audio channels in the input buffer, any number 1 or above is supported. </param>
    /// <param name="inputFramesAvailable"> How many frames of audio to process from the input buffer. </param>
    /// <param name="inputBuffer"> The buffer to read data from. Length must be at least inputFramesAvailable * channelCount. </param>
    /// <param name="outputBuffer"> The buffer to write the processed data to. Capacity must be at least inputFramesAvailable. </param>
    public static unsafe void FloatToShortMixdown(int channelCount, uint inputFramesAvailable, Span<float> inputBuffer, short[] outputBuffer)
    {
        fixed (float* BufferPtr = inputBuffer) { FloatToShortMixdown(channelCount, inputFramesAvailable, (byte*)BufferPtr, outputBuffer); }
    }

    /// <summary> Takes a buffer of <see cref="float"/> audio frames, and mixes all channels down to mono into a <see cref="short"/> buffer. </summary>
    /// <param name="channelCount"> The number of audio channels in the input buffer, any number 1 or above is supported. </param>
    /// <param name="inputFramesAvailable"> How many frames of audio to process from the input buffer. </param>
    /// <param name="inputBuffer"> The buffer to read data from. Length must be at least inputFramesAvailable * channelCount * sizeof(float). </param>
    /// <param name="outputBuffer"> The buffer to write the processed data to. Capacity must be at least inputFramesAvailable. </param>
    public static unsafe void FloatToShortMixdown(int channelCount, uint inputFramesAvailable, byte* inputBuffer, short[] outputBuffer)
    {
        const float SCALE_FACTOR = 32767F;
        const float SCALE_FACTOR_2CH = 16383F;
        IntPtr EndOfBuffer;

        if (channelCount == 1)
        {
            const int BYTES_PER_FRAME = 4;
            EndOfBuffer = (nint)(inputBuffer + (inputFramesAvailable * BYTES_PER_FRAME));
            uint Frame = 0;
            if (Avx2.IsSupported)
            {
                Vector256<float> Scale = Vector256.Create(SCALE_FACTOR);
                uint EndFrame = inputFramesAvailable - 16;
                while (Frame <= EndFrame)
                {
                    Debug.Assert((nint)inputBuffer + (Frame * BYTES_PER_FRAME) < EndOfBuffer, "Reading out of bounds");
                    Vector256<float> MonoData1 = Vector256.Load((float*)(inputBuffer + (Frame * BYTES_PER_FRAME)));
                    Vector256<float> MonoData2 = Vector256.Load((float*)(inputBuffer + ((Frame + 8) * BYTES_PER_FRAME)));
                    Vector256<int> IntMonoData1 = Avx.ConvertToVector256Int32(Avx.Multiply(MonoData1, Scale));
                    Vector256<int> IntMonoData2 = Avx.ConvertToVector256Int32(Avx.Multiply(MonoData2, Scale));
                    Vector256<short> PackedData = Avx2.PackSignedSaturate(IntMonoData1, IntMonoData2);
                    Vector256<short> Output = Avx2.Permute4x64(PackedData.AsInt64(), 0b11011000).AsInt16();
                    Output.StoreUnsafe(ref outputBuffer[Frame]);
                    Frame += 16;
                }
            }
            while (Frame < inputFramesAvailable)
            {
                Debug.Assert((nint)(((float*)inputBuffer) + Frame) < EndOfBuffer, "Reading out of bounds");
                float Sample = *(((float*)inputBuffer) + Frame);
                outputBuffer[Frame] = (short)(Sample * SCALE_FACTOR);
                Frame++;
            }
        }
        else if (channelCount == 2)
        {
            const int BYTES_PER_FRAME = 8;
            EndOfBuffer = (nint)(inputBuffer + (inputFramesAvailable * BYTES_PER_FRAME));
            uint Frame = 0;
            if (Avx2.IsSupported)
            {
                Vector256<float> Scale = Vector256.Create(SCALE_FACTOR_2CH);
                uint EndFrame = inputFramesAvailable - 16;
                while (Frame <= EndFrame)
                {
                    Debug.Assert((nint)(inputBuffer + ((Frame + 12) * BYTES_PER_FRAME)) < EndOfBuffer, "Reading out of bounds");
                    Vector256<float> StereoData1 = Vector256.Load((float*)(inputBuffer + (Frame * BYTES_PER_FRAME)));
                    Vector256<float> StereoData2 = Vector256.Load((float*)(inputBuffer + ((Frame + 4) * BYTES_PER_FRAME)));
                    Vector256<float> StereoData3 = Vector256.Load((float*)(inputBuffer + ((Frame + 8) * BYTES_PER_FRAME)));
                    Vector256<float> StereoData4 = Vector256.Load((float*)(inputBuffer + ((Frame + 12) * BYTES_PER_FRAME)));
                    Vector256<float> MonoData1 = Avx2.Permute4x64(Avx.HorizontalAdd(StereoData1, StereoData2).AsInt64(), 0b11011000).AsSingle();
                    Vector256<float> MonoData2 = Avx2.Permute4x64(Avx.HorizontalAdd(StereoData3, StereoData4).AsInt64(), 0b11011000).AsSingle(); // TODO: There's probably a way to do this without 3 permutes
                    Vector256<int> IntMonoData1 = Avx.ConvertToVector256Int32(Avx.Multiply(MonoData1, Scale));
                    Vector256<int> IntMonoData2 = Avx.ConvertToVector256Int32(Avx.Multiply(MonoData2, Scale));
                    Vector256<short> PackedData = Avx2.PackSignedSaturate(IntMonoData1, IntMonoData2);
                    Vector256<short> Output = Avx2.Permute4x64(PackedData.AsInt64(), 0b11011000).AsInt16();
                    Output.StoreUnsafe(ref outputBuffer[Frame]);
                    Frame += 16;
                }
            }
            while (Frame < inputFramesAvailable)
            {
                Debug.Assert((nint)(((float*)inputBuffer) + (Frame * 2) + 1) < EndOfBuffer, "Reading out of bounds");
                float Left = *(((float*)inputBuffer) + (Frame * 2));
                float Right = *(((float*)inputBuffer) + (Frame * 2) + 1);
                outputBuffer[Frame] = (short)((Left + Right) * SCALE_FACTOR_2CH);
                Frame++;
            }
        }
        else
        {
            EndOfBuffer = (nint)(inputBuffer + (inputFramesAvailable * sizeof(float) * channelCount));
            float ScaleFactor = SCALE_FACTOR / channelCount;
            for (uint Frame = 0; Frame < inputFramesAvailable; Frame++)
            {
                float Mixdown = 0F;
                for (int Channel = 0; Channel < channelCount; Channel++)
                {
                    Debug.Assert((nint)(((float*)inputBuffer) + (Frame * channelCount) + Channel) < EndOfBuffer, "Reading out of bounds");
                    Mixdown += *(((float*)inputBuffer) + (Frame * channelCount) + Channel);
                }
                outputBuffer[Frame] = (short)(Mixdown * ScaleFactor);
            }
        }
    }
}
