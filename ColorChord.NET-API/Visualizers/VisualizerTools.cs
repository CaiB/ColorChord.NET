using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace ColorChord.NET.API.Visualizers;

public static class VisualizerTools
{
    private const bool ALLOW_SIMD = true;

    public static float CCToHue(float note)
    {
        const float ONE_THIRD = 1F / 3F;
        const float TWO_THIRDS = 2F / 3F;
        note %= 1F;
        if (note < ONE_THIRD) { return (ONE_THIRD - note) * 180F; }
        else if (note < TWO_THIRDS) { return ((1F + ONE_THIRD) - note) * 360F; }
        else { return ((1F - note) * 540F) + 60F; }
    }

    /// <summary> Converts a ColorChord note value into an RGB colour. </summary>
    /// <param name="note"> The note value, between 0.0~1.0. Any values outside this range will get wrapped to within the range. </param>
    /// <param name="sat"> The HSV saturation, between 0.0~1.0. Any values outside this range will get clamped to the closest value within range. </param>
    /// <param name="value"> The HSV value, between 0.0~1.0. Any values outside this range will get clamped to the closest value within range. </param>
    /// <returns></returns>
    public static uint CCToRGB(float note, float sat, float value)
    {
        const float ONE_THIRD = 1F / 3F;
        const float TWO_THIRDS = 2F / 3F;
        if (Sse41.IsSupported && ALLOW_SIMD)
        {
            Vector128<float> SHIFT = Vector128.Create((1F / 6F), (4F / 3F), 0F, (5F / 3F));
            Vector128<float> SCALE = Vector128.Create(0.5F, 1F, 0F, 1.5F);
            Vector128<float> THRESHOLDS = Vector128.Create(ONE_THIRD, TWO_THIRDS, 10F, 10F);

            Vector128<float> Note = Vector128.Create(note);
            Vector128<float> NoteFrac = Sse.Subtract(Note, Sse41.RoundToNegativeInfinity(Note));
            Vector128<float> Hues = Sse.Subtract(SHIFT, Sse.Multiply(NoteFrac, SCALE));
            Vector128<float> Compares = Sse.CompareGreaterThanOrEqual(NoteFrac, THRESHOLDS);
            int Index = Sse.MoveMask(Compares);

            float Hue = Hues[Index & 0b11];
            return HSVToRGB(Hue, sat, value);
        }
        else
        {
            note -= MathF.Floor(note);
            float Hue;
            if (note <= ONE_THIRD) { Hue = (1F / 6F) - (note * 0.5F); }
            else if (note <= TWO_THIRDS) { Hue = (4F / 3F) - note; }
            else { Hue = (5F / 3F) - (note * 1.5F); }
            return HSVToRGB(Hue, sat, value);
        }
    }


    /// <summary> Converts a (hue, saturation, value) colour into a (red, green, blue) colour. </summary>
    /// <param name="hue"> The hue, between 0.0~1.0. Any values outside this range will get wrapped to within the range. </param>
    /// <param name="sat"> The saturation, between 0.0~1.0. Any values outside this range will get clamped to the closest value within range. </param>
    /// <param name="val"> The value, between 0.0~1.0. Any values outside this range will get clamped to the closest value within range. </param>
    /// <returns> The RGB colour, in the format 0x00RRGGBB. </returns>
    public static uint HSVToRGB(float hue, float sat, float val)
    {
        // Built from these equations on Wikipedia (where hue was in range 0~360):
        // {R, G, B} = {f(5.0), f(3.0), f(1.0)}
        // f(n) = val - val * sat * max(0.0, min(1.0, k(n), 4.0 - k(n)))
        // k(n) = (n + hue / 60.0) % 6.0
        if (Sse41.IsSupported && ALLOW_SIMD)
        {
            Vector128<float> SHIFT = Vector128.Create(5F / 6F, 3F / 6F, 1F / 6F, 0F);
            Vector128<float> OFFSET = Vector128.Create(4F / 6F); // TODO: Check if generating these from each other would be faster, i.e. put this in slot 3 and then shuffle? Or bump others to put in slot 0 and broadcast
            Vector128<float> ZERO = Vector128<float>.Zero;
            Vector128<float> ONE = Vector128.Create(1F);
            Vector128<float> SIX = Vector128.Create(6F);
            Vector128<float> BYTE_SCALE = Vector128.Create(255F);

            Vector128<float> Hue = Vector128.Create(hue);
            Vector128<float> SatVal = Sse.Min(ONE, Sse.Max(ZERO, Vector128.Create(sat, val, 0F, 0F)));
            Vector128<float> Sat = Sse2.Shuffle(SatVal.AsInt32(), 0b00000000).AsSingle(); // Could be done with VBROADCASTSS, but this requires AVX2. Instead, duplicate the values beforehand and MOVDDUP? Maybe with MOVHLPS for the second one?
            Vector128<float> Val = Sse2.Shuffle(SatVal.AsInt32(), 0b01010101).AsSingle();
            Vector128<float> Chroma = Sse.Multiply(Val, Sat);

            Vector128<float> ShiftedHue = Sse.Add(Hue, SHIFT);
            Vector128<float> ShiftedHueFrac = Sse.Subtract(ShiftedHue, Sse41.RoundToNegativeInfinity(ShiftedHue));
            Vector128<float> MinShiftedHue = Sse.Min(ShiftedHueFrac, Sse.Subtract(OFFSET, ShiftedHueFrac));
            Vector128<float> ClampedHue = Sse.Min(ONE, Sse.Max(ZERO, Sse.Multiply(SIX, MinShiftedHue)));

            Vector128<float> RGBFloats = Sse.Subtract(Val, Sse.Multiply(Chroma, ClampedHue));
            Vector128<int> RGBScaled = Sse2.ConvertToVector128Int32(Sse41.RoundToNearestInteger(Sse.Multiply(RGBFloats, BYTE_SCALE)));
            Vector128<int> RGBOrdered = Sse2.Shuffle(RGBScaled, 0b00000110); // TODO: Might be able to re-order above ops to remove need for this?
            Vector128<short> RGBCompressed = Sse41.PackUnsignedSaturate(RGBOrdered, RGBOrdered).AsInt16();
            uint Result = Sse2.PackUnsignedSaturate(RGBCompressed, RGBCompressed).AsUInt32()[0] & 0xFFFFFF;
            return Result;
        }
        else
        {
            sat = MathF.Min(1F, MathF.Max(0F, sat));
            val = MathF.Min(1F, MathF.Max(0F, val));
            float Chroma = val * sat;

            float HueShiftedR = (hue + (5F / 6F));
            float HueShiftedG = (hue + (3F / 6F));
            float HueShiftedB = (hue + (1F / 6F));
            float HueShiftedFracR = HueShiftedR - MathF.Floor(HueShiftedR);
            float HueShiftedFracG = HueShiftedG - MathF.Floor(HueShiftedG);
            float HueShiftedFracB = HueShiftedB - MathF.Floor(HueShiftedB);
            float MinHueR = MathF.Min(HueShiftedFracR, (4F / 6F) - HueShiftedFracR) * 6F;
            float MinHueG = MathF.Min(HueShiftedFracG, (4F / 6F) - HueShiftedFracG) * 6F;
            float MinHueB = MathF.Min(HueShiftedFracB, (4F / 6F) - HueShiftedFracB) * 6F;
            float FloatR = val - (Chroma * MathF.Min(1F, MathF.Max(0F, MinHueR)));
            float FloatG = val - (Chroma * MathF.Min(1F, MathF.Max(0F, MinHueG)));
            float FloatB = val - (Chroma * MathF.Min(1F, MathF.Max(0F, MinHueB)));
            byte ByteR = (byte)MathF.Round(FloatR * 255F);
            byte ByteG = (byte)MathF.Round(FloatG * 255F);
            byte ByteB = (byte)MathF.Round(FloatB * 255F);
            return (uint)((ByteR << 16) | (ByteG << 8) | ByteB);
        }
    }

    /// <summary>Given a RGB value, returns the corresponding HSV value.</summary>
    /// <param name="rgb">The RGB values, in range 0.0~1.0.</param>
    /// <returns>The HSV value, in ranges (0.0~360.0, 0.0~1.0, 0.0~1.0).</returns>
    public static Vector3 RGBToHSV(Vector3 rgb)
    {
        Vector3 Result = new();
        float Min = MathF.Min(MathF.Min(rgb.X, rgb.Y), rgb.Z);
        float Max = MathF.Max(MathF.Max(rgb.X, rgb.Y), rgb.Z);
        Result.Z = Max;

        float Delta = Max - Min;
        if (Delta < 0.00001F) { return Result; }

        if (Max > 0F) { Result.Y = Delta / Max; }
        else { return Result; }

        if (rgb.X >= Max) { Result.X = (rgb.Y - rgb.Z) / Delta; }
        else if (rgb.Y >= Max) { Result.X = 2F + ((rgb.Z - rgb.X) / Delta); }
        else { Result.X = 4F + ((rgb.X - rgb.Y) / Delta); }
        
        Result.X = ((Result.X * 60F) + 360F) % 360F;
        return Result;
    }

}
