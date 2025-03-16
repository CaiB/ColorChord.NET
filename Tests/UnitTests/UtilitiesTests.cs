using ColorChord.NET.API.Visualizers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;

namespace ColorChord.NET.Tests.UnitTests;

[TestClass]
public class UtilitiesTests
{
    [TestMethod]
    [DataRow(0xFF0000,   0F, 1.00F, 1.00F)]
    [DataRow(0x00FF00, 120F, 1.00F, 1.00F)]
    [DataRow(0x0000FF, 240F, 1.00F, 1.00F)]
    [DataRow(0xCC33AA, 313F, 0.75F, 0.80F)]
    [DataRow(0x000001, 240F, 1.00F, 0.01F)]
    [DataRow(0x000010, 240F, 1.00F, 0.06F)]
    [DataRow(0x111111,   0F, 0.00F, 0.07F)]
    [DataRow(0x123456, 210F, 0.79F, 0.34F)]
    [DataRow(0x555655, 120F, 0.01F, 0.34F)]
    [DataRow(0x84A73F,  80F, 0.62F, 0.65F)]
    [DataRow(0xF49A1D,  35F, 0.88F, 0.96F)]
    [DataRow(0x68AE5B, 111F, 0.48F, 0.68F)]
    [DataRow(0xDD60B1, 321F, 0.57F, 0.87F)]
    [DataRow(0x527D98, 203F, 0.46F, 0.60F)]
    [DataRow(0x811426, 350F, 0.85F, 0.51F)]
    public void TestRGBToHSV(int rgbIn, float expectedH, float expectedS, float expectedV)
    {
        const float TOLERANCE_H = 1F;
        const float TOLERANCE_SV = 0.01F;
        Vector3 Output = VisualizerTools.RGBToHSV(new((byte)(rgbIn >> 16) / 255F, (byte)(rgbIn >> 8) / 255F, (byte)rgbIn / 255F));
        Assert.AreEqual(expectedH, Output.X, TOLERANCE_H,         $"Hue output was out of range for input 0x{rgbIn:X6}, expected {expectedH} but result was {Output.X}");
        Assert.AreEqual(expectedS, Output.Y, TOLERANCE_SV, $"Saturation output was out of range for input 0x{rgbIn:X6}, expected {expectedS} but result was {Output.Y}");
        Assert.AreEqual(expectedV, Output.Z, TOLERANCE_SV,      $"Value output was out of range for input 0x{rgbIn:X6}, expected {expectedV} but result was {Output.Z}");
    }

    [TestMethod]
    [DataRow(  0F, 1.00F, 1.00F, 0xFF0000)]
    [DataRow(120F, 1.00F, 1.00F, 0x00FF00)]
    [DataRow(240F, 1.00F, 1.00F, 0x0000FF)]
    [DataRow(313F, 0.75F, 0.80F, 0xCC33AA)]
    [DataRow(240F, 1.00F, 0.01F, 0x000001)]
    [DataRow(240F, 1.00F, 0.06F, 0x000010)]
    [DataRow(  0F, 0.00F, 0.07F, 0x111111)]
    [DataRow(210F, 0.79F, 0.34F, 0x123456)]
    [DataRow(120F, 0.01F, 0.34F, 0x555655)]
    [DataRow( 80F, 0.62F, 0.65F, 0x84A73F)]
    [DataRow( 35F, 0.88F, 0.96F, 0xF49A1D)]
    [DataRow(111F, 0.48F, 0.68F, 0x68AE5B)]
    [DataRow(321F, 0.57F, 0.87F, 0xDD60B1)]
    [DataRow(203F, 0.46F, 0.60F, 0x527D98)]
    [DataRow(350F, 0.85F, 0.51F, 0x811426)]
    public void TestHSVToRGB(float h, float s, float v, int expectedRGB)
    {
        const float TOLERANCE = 2F;
        uint Output = VisualizerTools.HSVToRGB(h / 360F, s, v);
        byte ExpectedR = (byte)(expectedRGB >> 16);
        byte ExpectedG = (byte)(expectedRGB >> 8);
        byte ExpectedB = (byte)expectedRGB;
        byte ResultR = (byte)(Output >> 16);
        byte ResultG = (byte)(Output >> 8);
        byte ResultB = (byte)Output;
        Assert.AreEqual(ExpectedR, ResultR, TOLERANCE,   $"Red output was out of range for input (H={h}, S={s}, V={v}), expected 0x{ExpectedR:X2} but result was 0x{ResultR:X2}");
        Assert.AreEqual(ExpectedG, ResultG, TOLERANCE, $"Green output was out of range for input (H={h}, S={s}, V={v}), expected 0x{ExpectedG:X2} but result was 0x{ResultG:X2}");
        Assert.AreEqual(ExpectedB, ResultB, TOLERANCE,  $"Blue output was out of range for input (H={h}, S={s}, V={v}), expected 0x{ExpectedB:X2} but result was 0x{ResultB:X2}");
    }

    [TestMethod]
    [DataRow( 600F, 1.00F, 1.00F, 0x0000FF)]
    [DataRow(-360F, 1.00F, 1.00F, 0xFF0000)]
    [DataRow(-720F, 9.99F, 9.99F, 0xFF0000)]
    [DataRow(1800F, 1.00F, 1.00F, 0xFF0000)]
    [DataRow( 120F, 4.57F, 3.69F, 0x00FF00)]
    [DataRow(  30F, 2.00F, 2.00F, 0xFF8000)]
    [DataRow(  45F, 2.00F, 2.00F, 0xFFBF00)]
    public void TestHSVToRGB_OutOfRange(float h, float s, float v, int expectedRGB)
    {
        const float TOLERANCE = 2F;
        uint Output = VisualizerTools.HSVToRGB(h / 360F, s, v);
        byte ExpectedR = (byte)(expectedRGB >> 16);
        byte ExpectedG = (byte)(expectedRGB >> 8);
        byte ExpectedB = (byte)expectedRGB;
        byte ResultR = (byte)(Output >> 16);
        byte ResultG = (byte)(Output >> 8);
        byte ResultB = (byte)Output;
        Assert.AreEqual(ExpectedR, ResultR, TOLERANCE,   $"Red output was out of range for input (H={h}, S={s}, V={v}), expected 0x{ExpectedR:X2} but result was 0x{ResultR:X2}");
        Assert.AreEqual(ExpectedG, ResultG, TOLERANCE, $"Green output was out of range for input (H={h}, S={s}, V={v}), expected 0x{ExpectedG:X2} but result was 0x{ResultG:X2}");
        Assert.AreEqual(ExpectedB, ResultB, TOLERANCE,  $"Blue output was out of range for input (H={h}, S={s}, V={v}), expected 0x{ExpectedB:X2} but result was 0x{ResultB:X2}");
    }

    [TestMethod]
    [DataRow(0.000F, 1.00F, 1.00F, 0xFFFF00)]
    [DataRow(0.167F, 1.00F, 1.00F, 0xFF7F00)]
    [DataRow(0.333F, 1.00F, 1.00F, 0xFF0000)]
    [DataRow(0.500F, 1.00F, 1.00F, 0xFF00FF)]
    [DataRow(0.667F, 1.00F, 1.00F, 0x0000FF)]
    [DataRow(0.833F, 1.00F, 1.00F, 0x00FF7F)]
    [DataRow(1.000F, 1.00F, 1.00F, 0xFFFF00)]
    [DataRow(10.50F, 10.0F, 10.0F, 0xFF00FF)]
    [DataRow(0.333F, 0.50F, 1.00F, 0xFF7F7F)]
    [DataRow(0.667F, 0.00F, 1.00F, 0xFFFFFF)]
    [DataRow(0.667F, 1.00F, 0.00F, 0x000000)]
    [DataRow(0.667F, 0.10F, 0.10F, 0x17171A)]
    [DataRow(0.428F, 0.31F, 0.80F, 0xCC8DB1)]
    [DataRow(0.708F, 0.70F, 0.37F, 0x1C355E)]
    [DataRow(0.689F, 0.76F, 0.73F, 0x2D49BA)]
    [DataRow(0.375F, 0.93F, 0.35F, 0x59061B)]
    [DataRow(0.057F, 0.59F, 0.96F, 0xF5DD64)]
    public void TestCCToRGB(float note, float sat, float val, int expectedRGB)
    {
        const float TOLERANCE = 2F;
        uint Output = VisualizerTools.CCToRGB(note, sat, val);
        byte ExpectedR = (byte)(expectedRGB >> 16);
        byte ExpectedG = (byte)(expectedRGB >> 8);
        byte ExpectedB = (byte)expectedRGB;
        byte ResultR = (byte)(Output >> 16);
        byte ResultG = (byte)(Output >> 8);
        byte ResultB = (byte)Output;
        Assert.AreEqual(ExpectedR, ResultR, TOLERANCE,   $"Red output was out of range for input (N={note}, S={sat}, V={val}), expected 0x{ExpectedR:X2} but result was 0x{ResultR:X2}");
        Assert.AreEqual(ExpectedG, ResultG, TOLERANCE, $"Green output was out of range for input (N={note}, S={sat}, V={val}), expected 0x{ExpectedG:X2} but result was 0x{ResultG:X2}");
        Assert.AreEqual(ExpectedB, ResultB, TOLERANCE,  $"Blue output was out of range for input (N={note}, S={sat}, V={val}), expected 0x{ExpectedB:X2} but result was 0x{ResultB:X2}");
    }
}
