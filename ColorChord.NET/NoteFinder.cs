using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET
{
    public static class NoteFinder
    {

        public static float[] AudioBuffer = new float[8096]; // TODO: Make buffer size adjustable or auto-set based on sample rate
        public static int AudioBufferHead = 0; // Where in the buffer we are reading, as it is filled circularly.
        public static DateTime LastDataAdd;

        public static void SetSampleRate(int sampleRate)
        {

        }

    }
}
