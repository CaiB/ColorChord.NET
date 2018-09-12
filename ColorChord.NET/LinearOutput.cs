using System;
using System.Net;
using System.Net.Sockets;

namespace ColorChord.NET
{
    public static class LinearOutput
    {
        private static bool did_init;
        private static int total_leds = 50;
        private static bool is_loop = false;
        private static float light_siding = 1.0F;
        private static float[] last_led_pos = new float[50];
        private static float[] last_led_pos_filter = new float[50];
        private static float[] last_led_amp = new float[50];
        private static bool steady_bright = false;
        private static float led_floor = 0.1F;
        private static float led_limit = 1.0F; //Maximum brightness
        private static float satamp = 1.6F;
        private static int lastadvance;

        private static byte[] OutLEDs = new byte[50 * 3];

        public static void Update()
        {
            //Step 1: Calculate the quantity of all the LEDs we'll want.
            int totbins = NoteFinder.NotePeakCount;//nf->dists;
            int i, j;
            float[] binvals = new float[totbins];
            float[] binvalsQ = new float[totbins];
            float[] binpos = new float[totbins];
            float totalbinval = 0;

            for (i = 0; i < totbins; i++)
            {
                binpos[i] = NoteFinder.NotePositions[i] / NoteFinder.FreqBinCount;
                binvals[i] = (float)Math.Pow(NoteFinder.NoteAmplitudes2[i], light_siding);
                binvalsQ[i] = (float)Math.Pow(NoteFinder.NoteAmplitudes[i], light_siding);
                totalbinval += binvals[i];
            }

            float newtotal = 0;

            for (i = 0; i < totbins; i++)
            {
                binvals[i] -= led_floor * totalbinval;
                if (binvals[i] / totalbinval < 0) { binvals[i] = binvalsQ[i] = 0; }
                newtotal += binvals[i];
            }
            totalbinval = newtotal;

            float[] rledpos = new float[total_leds];
            float[] rledamp = new float[total_leds];
            float[] rledampQ = new float[total_leds];
            int rbinout = 0;

            for (i = 0; i < totbins; i++)
            {
                int nrleds = (int)((binvals[i] / totalbinval) * total_leds);
                for (j = 0; j < nrleds && rbinout < total_leds; j++)
                {
                    rledpos[rbinout] = binpos[i];
                    rledamp[rbinout] = binvals[i];
                    rledampQ[rbinout] = binvalsQ[i];
                    rbinout++;
                }
            }

            if (rbinout == 0)
            {
                rledpos[0] = 0;
                rledamp[0] = 0;
                rledampQ[0] = 0;
                rbinout++;
            }

            for (; rbinout < total_leds; rbinout++)
            {
                rledpos[rbinout] = rledpos[rbinout - 1];
                rledamp[rbinout] = rledamp[rbinout - 1];
                rledampQ[rbinout] = rledampQ[rbinout - 1];
            }

            //Now we have to minimize "advance".
            int minadvance = 0;

            if (is_loop)
            {
                float mindiff = 1e20F;

                //Uncomment this for a rotationally continuous surface.
                for (i = 0; i < total_leds; i++)
                {
                    float diff = 0;
                    diff = 0;
                    for (j = 0; j < total_leds; j++)
                    {
                        int r = (j + i) % total_leds;
                        float rd = lindiff(last_led_pos_filter[j], rledpos[r]);
                        diff += rd;//*rd;
                    }

                    int advancediff = (lastadvance - i);
                    if (advancediff < 0) advancediff *= -1;
                    if (advancediff > total_leds / 2) advancediff = total_leds - advancediff;

                    float ad = (float)advancediff / (float)total_leds;
                    diff += ad * ad;// * led->total_leds;

                    if (diff < mindiff)
                    {
                        mindiff = diff;
                        minadvance = i;
                    }
                }

            }
            lastadvance = minadvance;

            //Advance the LEDs to this position when outputting the values.
            for (i = 0; i < total_leds; i++)
            {
                int ia = (i + minadvance + total_leds) % total_leds;
                float sat = rledamp[ia] * satamp;
                float satQ = rledampQ[ia] * satamp;
                if (satQ > 1) satQ = 1;
                last_led_pos[i] = rledpos[ia];
                last_led_amp[i] = sat;
                float sendsat = (steady_bright ? sat : satQ);
                if (sendsat > 1) sendsat = 1;

                if (sendsat > led_limit) sendsat = led_limit;

                uint r = CCtoHEX(last_led_pos[i], 1.0F, sendsat);

                OutLEDs[i * 3 + 0] = (byte)((r >> 16) & 0xff);
                OutLEDs[i * 3 + 1] = (byte)((r >> 8) & 0xff);
                OutLEDs[i * 3 + 2] = (byte)((r) & 0xff);
            }


            if (is_loop)
            {
                for (i = 0; i < total_leds; i++)
                {
                    last_led_pos_filter[i] = last_led_pos_filter[i] * .9F + last_led_pos[i] * .1F;
                }
            }
        }

        private static uint CCtoHEX(float note, float sat, float value)
        {
            float hue = 0.0F;
            note = ((note % 1.0F) * 12) - 3;
            if (note < 0) { note += 12; }
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
                hue = (12F - note) * 45F + 60F;
            }
            return HsvToRgb(hue, sat, value);
        }
        
        private static uint HsvToRgb(double h, double S, double V) // TODO: Copy-pasted, this looks like it could use some optimization.
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
        private static int Clamp(int i)
        {
            if (i < 0) return 0;
            if (i > 255) return 255;
            return i;
        }

        private static UdpClient Sender = new UdpClient();
        private static IPEndPoint Destination = new IPEndPoint(IPAddress.Parse("192.168.0.102"), 7777);

        public static void Send()
        {
            if (!Program.OutputEnabled || Program.LastUpdate < DateTime.UtcNow.AddSeconds(-5)) { return; } // Don't output if we haven't had data in 5 sec.
            byte[] Output = new byte[151];
            for (int i = 1; i < 151; i++) { Output[i] = OutLEDs[i - 1]; }
            Sender.Send(Output, Output.Length, Destination);
        }

        public static void SendBlack()
        {
            byte[] Output = new byte[151];
            Sender.Send(Output, Output.Length, Destination);
        }

        private static float lindiff(float a, float b)  //Find the minimum change around a wheel.
        {
            float diff = a - b;
            if (diff < 0) { diff *= -1; }

            float otherdiff = (a < b) ? (a + 1) : (a - 1);
            otherdiff -= b;
            if (otherdiff < 0) { otherdiff *= -1; }

            if (diff < otherdiff) { return diff; }
            else { return otherdiff; }
        }
    }
}
