using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using System;
using System.Collections.Generic;
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

        /// <summary> What format to send the packets in. </summary>
        public SendMode Mode { get; private set; }

        /// <summary> The max length of individual packets for protocols that support splitting. </summary>
        public int MaxPacketLength { get; private set; }

        /// <summary> For protocols that support variable-length packets, determind whether all packets are the same size, remainder filled with <see cref="PaddingContent"/>. </summary>
        public bool UseConstantPacketLength { get; private set; }

        /// <summary> Whether this sender instance is enabled (can send packets). </summary>
        public bool Enabled { get; set; }

        /// <summary> Whether the LED matrix uses shorter wiring, reversing the direction of every other line. </summary>
        public bool ArrayIsZigZag { get; set; }

        /// <summary> Whether LEDs run left-to-right (false) or right-to-left (true). </summary>
        public bool MirrorOutput { get; set; }

        /// <summary> Whether the output should be rotated 180 degrees. </summary>
        public bool RotateOutput { get; set; }

        /// <summary> How many LEDs wide the matrix/strip is. </summary>
        public int MatrixSizeX { get; set; } = 1;

        /// <summary> How many LEDs tall the matrix is. </summary>
        public int MatrixSizeY { get; set; } = 1;

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

            ReadProtocol(ConfigTools.CheckString(options, "Protocol", "Raw", true));
            int Port = ConfigTools.CheckInt(options, "Port", 0, 65535, GetDefaultPort(this.Mode), true);
            if (Port < 1024) { Log.Warn("It is not recommended to use ports below 1024, as they are reserved. UDP sender is operating on port " + Port + "."); }
            string IP = ConfigTools.CheckString(options, "IP", "127.0.0.1", true);
            this.Destination = new IPEndPoint(IPAddress.Parse(IP), Port);
            this.FrontPadding = (uint)ConfigTools.CheckInt(options, "PaddingFront", 0, 1000, 0, true);
            this.BackPadding = (uint)ConfigTools.CheckInt(options, "PaddingBack", 0, 1000, 0, true);
            this.PaddingContent = (byte)ConfigTools.CheckInt(options, "PaddingContent", 0x00, 0xFF, 0x00, true);
            this.MaxPacketLength = ConfigTools.CheckInt(options, "MaxPacketLength", -1, 65535, -1, true);
            this.UseConstantPacketLength = ConfigTools.CheckBool(options, "ConstantPacketLength", false, true);
            this.ArrayIsZigZag = ConfigTools.CheckBool(options, "ZigZag", false, true);
            this.MirrorOutput = ConfigTools.CheckBool(options, "Mirror", false, true);
            this.RotateOutput = ConfigTools.CheckBool(options, "RotatedArray", false, true);
            this.MatrixSizeX = ConfigTools.CheckInt(options, "SizeX", 1, 100000, 1, true);
            this.MatrixSizeY = ConfigTools.CheckInt(options, "SizeY", 1, 100000, 1, true);
            this.Enabled = ConfigTools.CheckBool(options, "Enable", true, true);
            ReadLEDPattern(ConfigTools.CheckString(options, "LEDPattern", "RGB", true));

            ConfigTools.WarnAboutRemainder(options, typeof(IOutput));
        }

        /// <summary> Gets the default port for the given protocol. </summary>
        /// <param name="mode"> The protocol being used for sending. </param>
        private static ushort GetDefaultPort(SendMode mode)
        {
            return mode switch
            {
                SendMode.RAW => 7777,
                SendMode.TPM2NET => 65506,
                _ => 7777,
            };
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

        /// <summary> Sets the current protocol mode in <see cref="Mode"/> from the given config entry. </summary>
        /// <param name="protocolName"> The name of the protocol, case-insensitive. </param>
        private void ReadProtocol(string protocolName)
        {
            this.Mode = (protocolName.ToLowerInvariant()) switch
            {
                "raw" => SendMode.RAW,
                "tpm2.net" => SendMode.TPM2NET,
                _ => throw new Exception("Invalid protocol specified for UDP sender: \"" + protocolName + "\""),
            };
        }

        /// <summary> Sends the newest data from the visualizer to our recipient. </summary>
        public void Dispatch()
        {
            if (!this.Enabled) { return; }
            switch(this.Mode)
            {
                case SendMode.RAW: SendRaw(); break;
                case SendMode.TPM2NET: SendTPM2Net(); break;
                default: break;
            }
        }

        /// <summary>
        /// Sends raw data packet.
        /// Start: <see cref="FrontPadding"/> bytes filled with <see cref="PaddingContent"/>.
        /// Data: Data for each LED in sequence, in order specified by "LEDPattern" in config.
        /// End: <see cref="BackPadding"/> bytes filled with <see cref="PaddingContent"/>.
        /// Packets can be up to 65535 bytes long, but will get fragmented if over network's MTU (usually around 1400-1500B).
        /// </summary>
        private void SendRaw()
        {
            if (this.Source is not IDiscrete1D Src) { return; }

            byte[] Output = new byte[(Src.GetCountDiscrete() * this.LEDLength) + this.FrontPadding + this.BackPadding];
            uint[] SourceData = Src.GetDataDiscrete(); // The raw data from the visualizer.

            int Index;

            // Front Padding
            for (Index = 0; Index < this.FrontPadding; Index++) { Output[Index] = this.PaddingContent; }

            // Data Content
            for (int LED = 0; LED < Src.GetCountDiscrete(); LED++)
            {
                // Transform the index depending on LED matrix shape
                int InDataIndex = TransformIndex(this.MatrixSizeX, this.MatrixSizeY, LED, this.RotateOutput, this.MirrorOutput, this.ArrayIsZigZag);

                // Extract RGB
                byte Red = (byte)((SourceData[InDataIndex] >> 16) & 0xFF);
                byte Green = (byte)((SourceData[InDataIndex] >> 8) & 0xFF);
                byte Blue = (byte)(SourceData[InDataIndex] & 0xFF);

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
                        _ => 0,
                    };

                    Output[Index] = Insert;
                    Index++;
                }
            }

            // Back Padding
            for (; Index < Output.Length; Index++) { Output[Index] = this.PaddingContent; }

            this.Sender.Send(Output, Output.Length, this.Destination);
        }

        /// <summary>
        /// Sends data in TPM2.net format. See documentation: https://gist.github.com/jblang/89e24e2655be6c463c56
        /// Start: Header and packet number info
        /// Data: Data for each LED in sequence, in order specified by "LEDPattern" in config.
        /// End: 0x36
        /// Packets are divided to fit within <see cref="MaxPacketLength"/> bytes, and numbered accordingly.
        /// </summary>
        private void SendTPM2Net()
        {
            if (this.Source is not IDiscrete1D Src) { return; }

            int LEDCount = Src.GetCountDiscrete();
            uint[] SourceData = Src.GetDataDiscrete(); // The raw data from the visualizer.

            int LEDsPerPacket = 1490 / this.LEDLength; // 1490 is the maximum number of data bytes allowed by TPM2.net
            if (this.MaxPacketLength > 0) { LEDsPerPacket = this.MaxPacketLength / this.LEDLength; }

            if (LEDCount < LEDsPerPacket) { LEDsPerPacket = LEDCount; } // Only 1 packet needed.
            byte PacketQty = (byte)Math.Ceiling((decimal)LEDCount / LEDsPerPacket); // How many packets we'll need to send

            byte[] Output = new byte[(LEDsPerPacket * this.LEDLength) + 7]; // 6B header + data + 1B end.

            // Packet header
            Output[0] = 0x9C; // Specifies TPM2.net
            Output[1] = 0xDA; // Data frame
            Output[5] = PacketQty; // Number of packets for this frame

            int LEDIndex = 0; // The overall LED index (not reset per packet)
            for(int PacketNum = 0; PacketNum < PacketQty; PacketNum++)
            {
                Output[4] = (byte)(PacketNum + 1); // Packet numbers are 1-indexed.

                int DataIndex = 6;
                for(int LED = 0; LED < LEDsPerPacket && LEDIndex < LEDCount; LED++)
                {
                    // Transform the index depending on LED matrix shape
                    int InDataIndex = TransformIndex(this.MatrixSizeX, this.MatrixSizeY, LEDIndex, this.RotateOutput, this.MirrorOutput, this.ArrayIsZigZag);

                    // Extract RGB
                    byte Red = (byte)((SourceData[InDataIndex] >> 16) & 0xFF);
                    byte Green = (byte)((SourceData[InDataIndex] >> 8) & 0xFF);
                    byte Blue = (byte)(SourceData[InDataIndex] & 0xFF);

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
                            _ => 0,
                        };

                        Output[DataIndex] = Insert;
                        DataIndex++;
                    }

                    LEDIndex++;
                }

                // If we have empty space, and we should pad the remainder to match sizes. Fills remainder of last packet in constant-length mode.
                while (DataIndex < (Output.Length - 1) && this.UseConstantPacketLength) { Output[DataIndex++] = this.PaddingContent; }

                Output[DataIndex++] = 0x36; // Packet end byte
                Output[2] = (byte)(((DataIndex - 7) >> 8) & 0xFF); // Packet length
                Output[3] = (byte)((DataIndex - 7) & 0xFF); // Packet length

                this.Sender.Send(Output, DataIndex, this.Destination);
            }
        }

        /// <summary> Transforms a data array index to map physical LED matrix locations to data indeces. </summary>
        /// <param name="sizeX"> The width of the LED array. </param>
        /// <param name="sizeY"> The height of the LED array. </param>
        /// <param name="i"> The input index to transform. </param>
        /// <param name="rotate180"> Whether the LED array is rotated 180 degrees. </param>
        /// <param name="mirror"> Whether the output should be right-to-left (true) or left-to-right (false). </param>
        /// <param name="isZigZag"> Whether odd-numbered lines are flipped horizontally for matrices built with minimal wiring. </param>
        /// <returns> The index of the original data to pull from for the given LED index. </returns>
        private static int TransformIndex(int sizeX, int sizeY, int i, bool rotate180, bool mirror, bool isZigZag)
        {
            int X = i % sizeX;
            int Y = i / sizeX;

            if (rotate180)
            {
                Y = (sizeY - 1 - Y);
                i = (Y * sizeX) + X;
            }
            if ((mirror ^ rotate180) ^ (isZigZag && (Y & 1) == 1)) { i = (Y * sizeX) + (sizeX - X - 1); }
            return i;
        }

        private enum Channel : byte
        {
            Red = 0,
            Green = 1,
            Blue = 2,
            Yellow = 3
        }

        public enum SendMode
        {
            RAW,
            TPM2NET
        }
    }
}
