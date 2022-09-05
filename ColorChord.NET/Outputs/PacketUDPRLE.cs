using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace ColorChord.NET.Outputs
{
    public class PacketUDPRLE : IOutput
    {
        /// <summary> Instance name, for identification and attaching controllers. </summary>
        public string Name { get; private init; }

        [ConfigString("LEDPattern", "LRGB")]
        private readonly string LEDPatternFromConfig = "LRGB";

        /// <summary> How many bytes a single LED takes up in the packet. </summary>
        public byte LEDLength { get; private set; } = 0;

        /// <summary> A mapping from the individual LED's content index to values in <see cref="Channel"/>. </summary>
        /// <remarks> Length = <see cref="LEDLength"/>. </remarks>
        public byte[] LEDValueMapping { get; private set; } = Array.Empty<byte>();

        /// <summary> Whether the output has a yellow channel that requires processing colours differently. </summary>
        public bool UsesChannelY;

        /// <summary> Whether this sender instance is enabled (can send packets). </summary>
        [ConfigBool("Enable", true)]
        public bool Enabled { get; set; }

        [ConfigInt("Port", 1, 65535, 7777)]
        private readonly ushort PortFromConfig = 7777;

        [ConfigString("IP", "127.0.0.1")]
        private readonly string IPFromConfig = "127.0.0.1";

        /// <summary> Where the packets will be sent. </summary>
        private readonly IPEndPoint Destination;

        private readonly IVisualizer Source;
        private readonly UdpClient Sender = new();

        public PacketUDPRLE(string name, Dictionary<string, object> config)
        {
            this.Name = name;
            IVisualizer? Source = Configurer.FindVisualizer(this, config);
            if (Source == null) { throw new Exception($"{GetType().Name} \"{name}\" could not find requested visualizer."); }
            this.Source = Source;
            Configurer.Configure(this, config);

            if (this.PortFromConfig < 1024) { Log.Warn("It is not recommended to use ports below 1024, as they are reserved. UDP sender is operating on port " + this.PortFromConfig + "."); }
            this.Destination = new IPEndPoint(IPAddress.Parse(this.IPFromConfig), this.PortFromConfig);
            ReadLEDPattern(this.LEDPatternFromConfig);

            this.Source.AttachOutput(this);
        }

        public void Start() { }
        public void Stop() { }

        /// <summary> Sets the pattern length and content based on the given pattern descriptor string. </summary>
        /// <param name="pattern"> Valid characters are 'R', 'G', 'B', 'Y', 'L'. Other characters cause an exception. </param>
        private void ReadLEDPattern(string pattern)
        {
            this.LEDLength = (byte)pattern.Length;
            pattern = pattern.ToUpper();

            this.UsesChannelY = false;

            this.LEDValueMapping = new byte[this.LEDLength];
            for (byte i = 0; i < this.LEDValueMapping.Length; i++)
            {
                switch (pattern[i])
                {
                    case 'R': this.LEDValueMapping[i] = (byte)Channel.Red; continue;
                    case 'G': this.LEDValueMapping[i] = (byte)Channel.Green; continue;
                    case 'B': this.LEDValueMapping[i] = (byte)Channel.Blue; continue;
                    case 'Y': this.LEDValueMapping[i] = (byte)Channel.Yellow; this.UsesChannelY = true; continue;
                    case 'L': this.LEDValueMapping[i] = (byte)Channel.Length; continue;
                    default: throw new FormatException("Invalid character in UDP format string found, '" + pattern[i] + "'. Valid characters are R, G, B, Y, L.");
                }
            }
        }

        public void Dispatch()
        {
            if (!this.Enabled) { return; }
            if (this.Source is not IDiscrete1D Src) { return; }

            byte[] Output = new byte[ColorChord.NoteFinder!.NoteCount * this.LEDLength];
            uint[] SourceData = Src.GetDataDiscrete(); // The raw data from the visualizer.

            // Data Content
            uint PrevLED = SourceData[0];
            byte Count = 1;
            int Index = 0;
            for (int LED = 1; LED < Src.GetCountDiscrete(); LED++)
            {
                if (PrevLED == SourceData[LED] && Count != 0xFF)
                {
                    Count++;
                    if (LED != Src.GetCountDiscrete() - 1) { continue; } // Make sure we don't skip the last section
                }

                // Extract RGB
                byte Red = (byte)((PrevLED >> 16) & 0xFF);
                byte Green = (byte)((PrevLED >> 8) & 0xFF);
                byte Blue = (byte)(PrevLED & 0xFF);

                // Calculate other channels if needed
                byte Yellow = 0;
                if (this.UsesChannelY) // Calculate Yellow
                {
                    // Lower of the two: (Red / 2), Green
                    Yellow = Red / 2 > Green ? Green : (byte)(Red / 2);

                    Red = (Red - Yellow) > 0 ? (byte)(Red - Yellow) : (byte)0;
                    Green = (Green - Yellow) > 0 ? (byte)(Green - Yellow) : (byte)0;
                }

                // Copy each component to the output in the order specified in config.
                for (int Component = 0; Component < this.LEDLength; Component++)
                {
                    byte Insert = ((Channel)this.LEDValueMapping[Component]) switch
                    {
                        Channel.Red => Red,
                        Channel.Green => Green,
                        Channel.Blue => Blue,
                        Channel.Yellow => Yellow,
                        Channel.Length => Count,
                        _ => 0,
                    };

                    Output[Index++] = Insert;
                }

                Count = 0;
                PrevLED = SourceData[LED];
            }

            this.Sender.Send(Output, Index, this.Destination);
        }

        private enum Channel : byte
        {
            Red = 0,
            Green = 1,
            Blue = 2,
            Yellow = 3,
            Length = 255
        }
    }
}
