using System.Diagnostics;

namespace ColorChord.NET.API.NoteFinder;

public static class LoudnessCorrection
{
    /// <summary> Gets a factor to multiply the output amplitude by to correct for percieved loudness. </summary>
    /// <remarks> Output is based on ISO 226:2023 data, and assumes a fixed loudness, and that the item being multiplied is sqrt(dB level). </remarks>
    /// <param name="frequency"> The frequency of the bin to get correction factor for </param>
    /// <param name="correctionAmount"> The strength, between 0.0~1.0 to apply the correction, applied to logarithmic amplitude. Generally, 0.25~0.33 yields good results. </param>
    /// <returns> Scaling factor between 0.0~1.0 for the given frequency </returns>
    public static float GetLoudnessCorrection(float frequency, float correctionAmount)
    {
        const float LOUDNESS = 50F; // in phon
        ReadOnlySpan<float> Frequencies = new float[] { 20F, 25F, 31.5F, 40F, 50F, 63F, 80F, 100F, 125F, 160F, 200F, 250F, 315F, 400F, 500F, 630F, 800F, 1000F, 1250F, 1600F, 2000F, 2500F, 3150F, 4000F, 5000F, 6300F, 8000F, 10000F, 12500F };
        ReadOnlySpan<float> af = new float[] { 0.635F, 0.602F, 0.569F, 0.537F, 0.509F, 0.482F, 0.456F, 0.433F, 0.412F, 0.391F, 0.373F, 0.357F, 0.343F, 0.33F, 0.32F, 0.311F, 0.303F, 0.3F, 0.295F, 0.292F, 0.29F, 0.29F, 0.289F, 0.289F, 0.289F, 0.293F, 0.303F, 0.323F, 0.354F };
        ReadOnlySpan<float> LU = new float[] { -31.5F, -27.2F, -23.1F, -19.3F, -16.1F, -13.1F, -10.4F, -8.2F, -6.3F, -4.6F, -3.2F, -2.1F, -1.2F, -0.5F, 0F, 0.4F, 0.5F, 0F, -2.7F, -4.2F, -1.2F, 1.4F, 2.3F, 1F, -2.3F, -7.2F, -11.2F, -10.9F, -3.5F };
        ReadOnlySpan<float> Tf = new float[] { 78.1F, 68.7F, 59.5F, 51.1F, 44F, 37.5F, 31.5F, 26.5F, 22.1F, 17.9F, 14.4F, 11.4F, 8.6F, 6.2F, 4.4F, 3F, 2.2F, 2.4F, 3.5F, 1.7F, -1.3F, -4.2F, -6F, -5.4F, -1.5F, 6F, 12.6F, 13.9F, 12.3F };

        int IndexLower = 0, IndexUpper = 0;
        if (frequency < Frequencies[^1])
        {
            for (int i = 0; i < Frequencies.Length; i++)
            {
                if (Frequencies[i] > frequency)
                {
                    IndexUpper = i;
                    IndexLower = Math.Max(0, i - 1);
                    break;
                }
            }
        }
        else
        {
            IndexUpper = Frequencies.Length - 1;
            IndexLower = Frequencies.Length - 1;
        }
        float dBAtLower = (10F / af[IndexLower] * MathF.Log10((MathF.Pow(4E-10F, 0.3F - af[IndexLower]) * (MathF.Pow(10F, 0.03F * LOUDNESS) - MathF.Pow(10F, 0.072F))) + MathF.Pow(10F, af[IndexLower] * (Tf[IndexLower] + LU[IndexLower]) / 10F))) - LU[IndexLower];
        float dBAtUpper = (10F / af[IndexUpper] * MathF.Log10((MathF.Pow(4E-10F, 0.3F - af[IndexUpper]) * (MathF.Pow(10F, 0.03F * LOUDNESS) - MathF.Pow(10F, 0.072F))) + MathF.Pow(10F, af[IndexUpper] * (Tf[IndexUpper] + LU[IndexUpper]) / 10F))) - LU[IndexUpper];
        // This just uses linear interpolation, and incorrectly assumes dB and frequency scale linearly. However, the error caused by these bad assumptions should be minimal, and I can't be bothered to implement a more complex correct version.
        // Remember, this is a visualization, not a scientific tool :)
        float InnerPosition = (IndexUpper == IndexLower) ? 0F : (frequency - Frequencies[IndexLower]) / (Frequencies[IndexUpper] - Frequencies[IndexLower]);
        Debug.Assert(InnerPosition <= 1F);
        Debug.Assert(InnerPosition >= 0F);
        float dBAtFreq = (dBAtUpper * InnerPosition) + (dBAtLower * (1F - InnerPosition));
        // Since the amplitude has its sqrt taken a second time, dB = (20 * log_10(amplitude)) * 2
        // As such, to get a scaling factor we need to divide the exponent by 20 * 2 instead of just 20
        return MathF.Pow(10F, (LOUDNESS - dBAtFreq) * correctionAmount / 40F);
    }
}
