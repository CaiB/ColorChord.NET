using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;

namespace ColorChord.NET.Outputs
{
    public class PacketUDP : IOutput
    {
        private IVisualizer Source;
        private readonly UdpClient Sender = new UdpClient();
        private IPEndPoint Destination;

        /// <summary> Instance name, for identification and attaching controllers. </summary>
        private readonly string Name;

        /// <summary> Number of empty bytes to leave at the front of the packet. </summary>
        public uint FrontPadding { get; set; }

        /// <summary> Number of empty bytes to leave at the end of the packet. </summary>
        public uint BackPadding { get; set; }

        /// <summary> The data to place into the padding bytes specified by <see cref="FrontPadding"/> and <see cref="BackPadding"/>. </summary>
        public byte PaddingContent { get; set; }
        
        /// <summary> How many bytes a single LED takes up in the packet. </summary>
        public byte LEDLength { get; private set; }

        /// <summary> A mapping from the individual LED's content index to values in <see cref="Channel"/>. </summary>
        /// <remarks> Length = <see cref="LEDLength"/>. </remarks>
        public byte[] LEDValueMapping { get; private set; }

        /// <summary> Whether the output has a yellow channel that requires processing colours differently. </summary>
        public bool UsesChannelY;

        /// <summary> Whether this sender instance is enabled (can send packets). </summary>
        public bool Enabled { get; set; }

        public PacketUDP(string name)
        {
            this.Name = name;
        }

        public void Start() { }
        public void Stop() { }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for PacketUDP \"" + this.Name + "\".");

            if (!options.ContainsKey("VisualizerName") || !ColorChord.VisualizerInsts.ContainsKey((string)options["VisualizerName"])) { Log.Error("Tried to create PacketUDP with missing or invalid visualizer."); return; }
            this.Source = ColorChord.VisualizerInsts[(string)options["VisualizerName"]];
            this.Source.AttachOutput(this);

            int Port = ConfigTools.CheckInt(options, "Port", 0, 65535, 7777, true);
            string IP = ConfigTools.CheckString(options, "IP", "127.0.0.1", true);
            this.Destination = new IPEndPoint(IPAddress.Parse(IP), Port);
            this.FrontPadding = (uint)ConfigTools.CheckInt(options, "PaddingFront", 0, 1000, 0, true);
            this.BackPadding = (uint)ConfigTools.CheckInt(options, "PaddingBack", 0, 1000, 0, true);
            this.PaddingContent = (byte)ConfigTools.CheckInt(options, "PaddingContent", 0x00, 0xFF, 0x00, true);
            this.Enabled = ConfigTools.CheckBool(options, "Enable", true, true);
            ReadLEDPattern(ConfigTools.CheckString(options, "LEDPattern", "RGB", true));

            ConfigTools.WarnAboutRemainder(options, typeof(IOutput));
        }

        /// <summary> Sets the pattern length and content based on the given pattern descriptor string. </summary>
        /// <param name="pattern"> Valid characters are 'R', 'G', 'B', 'Y'. Other characters cause an exception. </param>
        private void ReadLEDPattern(string pattern)
        {
            this.LEDLength = (byte)pattern.Length;
            pattern = pattern.ToUpper();

            this.UsesChannelY = false;

            this.LEDValueMapping = new byte[this.LEDLength];
            for (byte i = 0; i < this.LEDValueMapping.Length; i++)
            {
                switch(pattern[i])
                {
                    case 'R': this.LEDValueMapping[i] = (byte)Channel.Red; continue;
                    case 'G': this.LEDValueMapping[i] = (byte)Channel.Green; continue;
                    case 'B': this.LEDValueMapping[i] = (byte)Channel.Blue; continue;
                    case 'Y': this.LEDValueMapping[i] = (byte)Channel.Yellow; this.UsesChannelY = true; continue;
                    default: throw new FormatException("Invalid character in UDP format string found, '" + pattern[i] + "'. Valid characters are R, G, B, Y.");
                }
            }
        }

        public void Dispatch()
        {
            byte[] Output;
            if (this.Source is IDiscrete1D Source1D && this.Enabled)
            {
                Output = new byte[(Source1D.GetCountDiscrete() * this.LEDLength) + this.FrontPadding + this.BackPadding];
                uint[] SourceData = Source1D.GetDataDiscrete(); // The raw data from the visualizer.
                byte[] LEDData = null; // The values re-formatted to [R,G,B,Y][R,G,B,Y]...
                const byte STRIDE = 4;

                LEDData = new byte[Source1D.GetCountDiscrete() * STRIDE];

                int i;
                for (i = 0; i < this.FrontPadding; i++) { Output[i] = this.PaddingContent; } // Front padding
                for (int LED = 0; LED < Source1D.GetCountDiscrete(); LED++) // LED Data
                {
                    // Copy RGB
                    LEDData[(LED * STRIDE) + (byte)Channel.Red] = (byte)((SourceData[LED] >> 16) & 0xFF);
                    LEDData[(LED * STRIDE) + (byte)Channel.Green] = (byte)((SourceData[LED] >> 8) & 0xFF);
                    LEDData[(LED * STRIDE) + (byte)Channel.Blue] = (byte)(SourceData[LED] & 0xFF);

                    // Calculate other channels if needed
                    if (this.UsesChannelY) // Add Y
                    {
                        byte Red = LEDData[(LED * STRIDE) + (byte)Channel.Red];
                        byte Green = LEDData[(LED * STRIDE) + (byte)Channel.Green];

                        byte Yellow = Red / 2 > Green ? Green : (byte)(Red / 2); // Yellow = Lower of the two: (Red / 2), Green

                        LEDData[(LED * STRIDE) + (byte)Channel.Yellow] = Yellow;
                        LEDData[(LED * STRIDE) + (byte)Channel.Red] = (Red - Yellow) > 0 ? (byte)(Red - Yellow) : (byte)0;
                        LEDData[(LED * STRIDE) + (byte)Channel.Green] = (Green - Yellow) > 0 ? (byte)(Green - Yellow) : (byte)0;
                    }

                    // Copy data to output as needed.
                    for (int b = 0; b < this.LEDLength; b++)
                    {
                        Output[i] = LEDData[(LED * STRIDE) + this.LEDValueMapping[b]];
                        i++;
                    }
                }
                for (; i < Output.Length; i++) { Output[i] = this.PaddingContent; } // Back padding
            }
            else { return; }
            this.Sender.Send(Output, Output.Length, this.Destination);
        }

        private enum Channel : byte
        {
            Red = 0,
            Green = 1,
            Blue = 2,
            Yellow = 3
        }
    }
}
