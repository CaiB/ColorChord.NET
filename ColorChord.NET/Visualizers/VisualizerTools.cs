using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET.Visualizers
{
    public static class VisualizerTools
    {

        public static uint CCtoHEX(float note, float sat, float value)
        {
            float hue;
            //note = ((note % 1.0F) * 12) - 3;
            //if (note < 0) { note += 12; }
            note = (note % 1F) * 12F;
            if (note < 4)
            {
                //Needs to be YELLOW->RED
                hue = (4F - note) * 15F;
            }
            else if (note < 8)
            {
                //            [4]  [8]
                //Needs to be RED->BLUE
                hue = (4F - note) * 30F;
            }
            else
            {
                //             [8] [12]
                //Needs to be BLUE->YELLOW
                hue = ((12F - note) * 45F) + 60F;
            }
            return HsvToRgb(hue, sat, value);
        }

        public static uint HsvToRgb(double h, double S, double V) // TODO: Copy-pasted, this looks like it could use some optimization.
        {
            double H = h;
            while (H < 0) { H += 360; };
            while (H >= 360) { H -= 360; };
            double R, G, B;
            if (V <= 0)
            { R = G = B = 0; }
            else if (S <= 0)
            {
                R = G = B = V;
            }
            else
            {
                double hf = H / 60.0;
                int i = (int)Math.Floor(hf);
                double f = hf - i;
                double pv = V * (1 - S);
                double qv = V * (1 - S * f);
                double tv = V * (1 - S * (1 - f));
                switch (i)
                {
                    // Red is the dominant color
                    case 0:
                        R = V;
                        G = tv;
                        B = pv;
                        break;

                    // Green is the dominant color
                    case 1:
                        R = qv;
                        G = V;
                        B = pv;
                        break;
                    case 2:
                        R = pv;
                        G = V;
                        B = tv;
                        break;

                    // Blue is the dominant color
                    case 3:
                        R = pv;
                        G = qv;
                        B = V;
                        break;
                    case 4:
                        R = tv;
                        G = pv;
                        B = V;
                        break;

                    // Red is the dominant color
                    case 5:
                        R = V;
                        G = pv;
                        B = qv;
                        break;

                    // Just in case we overshoot on our math by a little, we put these here. Since its a switch it won't slow us down at all to put these here.
                    case 6:
                        R = V;
                        G = tv;
                        B = pv;
                        break;
                    case -1:
                        R = V;
                        G = pv;
                        B = qv;
                        break;

                    // The color is not defined, we should throw an error.
                    default:
                        //LFATAL("i Value error in Pixel conversion, Value is %d", i);
                        R = G = B = V; // Just pretend its black/white
                        break;
                }
            }
            byte r = (byte)Clamp((int)(R * 255.0));
            byte g = (byte)Clamp((int)(G * 255.0));
            byte b = (byte)Clamp((int)(B * 255.0));
            return (uint)((r << 16) | (g << 8) | b);
        }

        /// <summary>
        /// Clamp a value to 0-255
        /// </summary>
        public static int Clamp(int i)
        {
            if (i < 0) return 0;
            if (i > 255) return 255;
            return i;
        }

    }
}
