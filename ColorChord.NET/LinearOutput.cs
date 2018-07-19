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

                OutLEDs[i * 3 + 0] = (byte)(r & 0xff);
                OutLEDs[i * 3 + 1] = (byte)((r >> 8) & 0xff);
                OutLEDs[i * 3 + 2] = (byte)((r >> 16) & 0xff);
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
            note = note % 1.0F;
            note *= 12;
            if (note < 4)
            {
                //Needs to be YELLOW->RED
                hue = (4 - note) / 24.0F;
            }
            else if (note < 8)
            {
                //            [4]  [8]
                //Needs to be RED->BLUE
                hue = (4 - note) / 12.0F;
            }
            else
            {
                //             [8] [12]
                //Needs to be BLUE->YELLOW
                hue = (12 - note) / 8.0F + 1.0F/ 6.0F;
            }
            return HSVtoHEX(hue, sat, value);
        }

        private static uint HSVtoHEX(float hue, float sat, float value)
        {

            float pr = 0;
            float pg = 0;
            float pb = 0;

            short ora = 0;
            short og = 0;
            short ob = 0;

            float ro = hue * 6 % 6.0F;

            float avg = 0;

            ro = ro + 6 + 1 % 6; //Hue was 60* off...

            if (ro < 1) //yellow->red
            {
                pr = 1;
                pg = 1.0F - ro;
            }
            else if (ro < 2)
            {
                pr = 1;
                pb = ro - 1.0F;
            }
            else if (ro < 3)
            {
                pr = 3.0F - ro;
                pb = 1;
            }
            else if (ro < 4)
            {
                pb = 1;
                pg = ro - 3;
            }
            else if (ro < 5)
            {
                pb = 5 - ro;
                pg = 1;
            }
            else
            {
                pg = 1;
                pr = ro - 5;
            }

            //Actually, above math is backwards, oops!
            pr *= value;
            pg *= value;
            pb *= value;

            avg += pr;
            avg += pg;
            avg += pb;

            pr = pr * sat + avg * (1.0F - sat);
            pg = pg * sat + avg * (1.0F - sat);
            pb = pb * sat + avg * (1.0F - sat);

            ora = (short)(pr * 255);
            og = (short)(pb * 255);
            ob = (short)(pg * 255);

            if (ora < 0) ora = 0;
            if (ora > 255) ora = 255;
            if (og < 0) og = 0;
            if (og > 255) og = 255;
            if (ob < 0) ob = 0;
            if (ob > 255) ob = 255;

            return (uint)((ob << 16) | (og << 8) | ora);
        }

        private static UdpClient Sender = new UdpClient();
        private static IPEndPoint Destination = new IPEndPoint(IPAddress.Parse("192.168.0.110"), 7777);

        public static void Send()
        {
            byte[] Output = new byte[151];
            for (int i = 1; i < 151; i++) { Output[i] = OutLEDs[i - 1]; }
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
