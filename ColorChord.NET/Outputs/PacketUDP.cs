using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ColorChord.NET.Outputs
{
    public class PacketUDP : IOutput, IControllableAttr
    {
        /// <summary> Instance name, for identification and attaching controllers. </summary>
        public string Name { get; private init; }

        /// <summary> Number of empty bytes to leave at the front of the packet. </summary>
        [Controllable("PaddingFront")]
        [ConfigInt("PaddingFront", 0, 1000, 0)]
        public uint FrontPadding { get; set; }

        /// <summary> Number of empty bytes to leave at the end of the packet. </summary>
        [Controllable("PaddingBack")]
        [ConfigInt("PaddingBack", 0, 1000, 0)]
        public uint BackPadding { get; set; }

        /// <summary> The data to place into the padding bytes specified by <see cref="FrontPadding"/> and <see cref="BackPadding"/>. </summary>
        [Controllable("PaddingContent")]
        [ConfigInt("PaddingContent", 0x00, 0xFF, 0x00)]
        public byte PaddingContent { get; set; }

        [ConfigString("LEDPattern", "RGB")]
        private string LEDPatternFromConfig = "RGB";

        /// <summary> How many bytes a single LED takes up in the packet. </summary>
        public byte LEDLength { get; private set; } = 0;

        /// <summary> A mapping from the individual LED's content index to values in <see cref="Channel"/>. </summary>
        /// <remarks> Length = <see cref="LEDLength"/>. </remarks>
        public byte[] LEDValueMapping { get; private set; } = [];

        /// <summary> Whether the output has a yellow channel that requires processing colours differently. </summary>
        public bool UsesChannelY;

        [ConfigString("Protocol", "Raw")]
        private string ProtocolFromConfig = "raw";

        /// <summary> What format to send the packets in. </summary>
        public SendMode Mode { get; private set; }

        /// <summary> The max length of individual packets for protocols that support splitting. </summary>
        [ConfigInt("MaxPacketLength", -1, 65535, -1)]
        public int MaxPacketLength { get; private set; }

        /// <summary> For protocols that support variable-length packets, determind whether all packets are the same size, remainder filled with <see cref="PaddingContent"/>. </summary>
        [ConfigBool("ConstantPacketLength", false)]
        public bool UseConstantPacketLength { get; private set; }

        /// <summary> Whether this sender instance is enabled (can send packets). </summary>
        [Controllable(ConfigNames.ENABLE)]
        [ConfigBool(ConfigNames.ENABLE, true)]
        public bool Enabled { get; set; }

        /// <summary> Whether the LED matrix uses shorter wiring, reversing the direction of every other line. </summary>
        [ConfigBool("ZigZag", false)]
        public bool ArrayIsZigZag { get; set; }

        /// <summary> Whether LEDs run left-to-right (false) or right-to-left (true). </summary>
        [ConfigBool("Mirror", false)]
        public bool MirrorOutput { get; set; }

        /// <summary> Whether the output should be rotated 180 degrees. </summary>
        [ConfigBool("RotatedArray", false)]
        public bool RotateOutput { get; set; }

        /// <summary> How many LEDs wide the matrix/strip is. </summary>
        [ConfigInt("SizeX", 1, 100000, 1)]
        public int MatrixSizeX { get; set; } = 1;

        /// <summary> How many LEDs tall the matrix is. </summary>
        [ConfigInt("SizeY", 1, 100000, 1)]
        public int MatrixSizeY { get; set; } = 1;

        /// <summary> Where in the visualizer data we start reading data to output over the network. </summary>
        [Controllable("StartIndex")]
        [ConfigInt("StartIndex", 0, int.MaxValue, 0)]
        public int StartIndex = 0;

        /// <summary> Where in the visualizer data we stop reading data to output over the network. </summary>
        /// <remarks> -1 means read to the end of the data available. </remarks>
        [Controllable("EndIndex")]
        [ConfigInt("EndIndex", -1, int.MaxValue, -1)]
        public int EndIndex = -1;

        /// <summary> The port as specified in the config or by a controller. </summary>
        /// <remarks> If set to 0, the default port for the current <see cref="Mode"/> is used. </remarks>
        [Controllable("Port", 1)]
        [ConfigInt("Port", 0, 65535, 0)]
        private ushort PortFromConfig = 0;

        [Controllable("IP", 1)]
        [ConfigString("IP", "127.0.0.1")]
        private string IPFromConfig = "127.0.0.1";

        [Controllable("Universe")]
        [ConfigInt("Universe", 1, 63999, 1)]
        private ushort Universe = 1;

        // Note only the lower 16 bytes are used.
        [ConfigString("UUID", "9E917B13714044CFB46F7A8298692DE3")]
        private string UUIDFromConfig = "9E917B13714044CFB46F7A8298692DE3";

        private readonly byte[] E131Template;

        private byte E131Sequence = 0;

        /// <summary> Where the packets will be sent. </summary>
        private IPEndPoint Destination;

        private readonly IDiscrete1D Source;
        private readonly UdpClient Sender = new();
        private byte[] SendBuffer = [];

        public PacketUDP(string name, Dictionary<string, object> config)
        {
            this.Name = name;
            IVisualizer? SourceVisualizer = ColorChordAPI.Configurer.FindVisualizer(config) ?? throw new Exception($"{nameof(PacketUDP)} \"{name}\" could not find requested visualizer.");
            this.Source = SourceVisualizer as IDiscrete1D ?? throw new Exception($"{nameof(PacketUDP)} requires a visualizer of type {nameof(IDiscrete1D)}.");
            ColorChordAPI.Configurer.Configure(this, config);

            // Post-configuration setup
            this.Mode = ReadProtocol(this.ProtocolFromConfig);
            if (this.PortFromConfig == 0) { this.PortFromConfig = GetDefaultPort(this.Mode); }
            if (this.PortFromConfig < 1024) { Log.Warn($"It is not recommended to use ports below 1024, as they are reserved. {nameof(PacketUDP)} is operating on port {this.PortFromConfig}."); }
            this.Destination = new(IPAddress.Parse(this.IPFromConfig), this.PortFromConfig);
            ReadLEDPattern(this.LEDPatternFromConfig);

            this.E131Template =
            [
                // Root Layer
                0x00, 0x10, // Preamble Size
                0x00, 0x00, // Postamble Size
                0x41, 0x53, 0x43, 0x2D, 0x45, 0x31, 0x2E, 0x31, 0x37, 0x00, 0x00, 0x00, // ACN Packet Identifier
                0xFF, 0xFF, // Flags and Length (replaced)
                0x00, 0x00, 0x00, 0x04, // Vector
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // Unique Sender ID (CID) (replaced)

                // E1.31 Framing Layer
                0xFF, 0xFF, // Flags and Length (replaced)
                0x00, 0x00, 0x00, 0x02, // Vector
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // ^
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // |
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // |-- User Assigned Source Name (UTF8-encoded, null terminated) (replaced)
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // v
                0x64, // Priority
                0x00, 0x00, // Synchronization Address: none, act immediately
                0xFF, // Sequence number (replaced)
                0x00, // Options (replaced): Set bit 6 for termination, or bit 7 for preview-only. Otherwise 0.
                0xFF, 0xFF, // Universe (replaced)

                // DMP Layer
                0xFF, 0xFF, // Flags and Length (replaced)
                0x02, // Vector
                0xA1, // Address Type & Data Type
                0x00, 0x00, // Start Address
                0x00, 0x01, // Address Increment
                0xFF, 0xFF, // Property Value Count (LED data length + 1) (replaced)
                0x00 // Start code
                // Insert up to 512 bytes of data here
            ];

            byte[] UUID = ParseUUID(this.UUIDFromConfig);
            UUID.CopyTo(this.E131Template, 22);

            byte[] SenderName = ToNetworkString(this.Name, 64);
            SenderName.CopyTo(this.E131Template, 44);

            SourceVisualizer.AttachOutput(this);
        }

        public void Start() { }
        public void Stop() { }

        /// <summary>Takes a string, and converts it to a null-terminated UTF8 representation, within a length bound.</summary>
        /// <remarks>The array will be of length maxBytes, even if the string is shorter, and the remaining space will be filled with 0x00s.</remarks>
        /// <param name="input">The string to convert.</param>
        /// <param name="maxBytes">The maximum number of bytes, including the null terminator, that the output can be.</param>
        /// <returns>A UTF8-formatted string, safely truncated if necessary, with at least 1 null termination byte.</returns>
        /// <exception cref="ArgumentException">If maxBytes is < 1</exception>
        private static byte[] ToNetworkString(string input, int maxBytes)
        {
            if (maxBytes < 1) { throw new ArgumentException("maxBytes cannot be less than 1", nameof(maxBytes)); }

            byte[] Output = new byte[maxBytes];
            byte[] Converted = Encoding.UTF8.GetBytes(input);
            if (Converted.Length >= maxBytes)
            {
                StringBuilder Builder = new();
                int Bytes = 0;
                TextElementEnumerator Enumerator = StringInfo.GetTextElementEnumerator(input);
                while (Enumerator.MoveNext())
                {
                    string TextElement = Enumerator.GetTextElement();
                    Bytes += Encoding.UTF8.GetByteCount(TextElement);
                    if (Bytes < maxBytes) { Builder.Append(TextElement); }
                    else { break; }
                }
                Converted = Encoding.UTF8.GetBytes(Builder.ToString());
            }
            Array.Copy(Converted, Output, Converted.Length);
            if (Output[maxBytes - 1] != 0x00) { throw new Exception("An error occured while converting a string and the null termination was not added correctly."); }

            return Output;
        }

        /// <summary>Parses a hexadecimal string into a byte array.</summary>
        /// <param name="uuid">The hex string to parse. Must have length 32.</param>
        /// <returns>16-length byte array representation of the hex string.</returns>
        private static byte[] ParseUUID(string uuid)
        {
            if (uuid.Length != 32) { throw new Exception("UUID needs to be 32 characters long"); }
            return Convert.FromHexString(uuid);
        }

        /// <summary> Gets the default port for the given protocol. </summary>
        /// <param name="mode"> The protocol being used for sending. </param>
        private static ushort GetDefaultPort(SendMode mode) => mode switch
        {
            SendMode.RAW => 7777,
            SendMode.TPM2NET => 65506,
            SendMode.E131 => 5568,
            _ => 7777,
        };

        public void SettingWillChange(int controlID) { }

        /// <summary> Callback for when a controller changes settings. </summary>
        public void SettingChanged(int id)
        {
            if (id == 1) // IP or Port
            {
                if (this.PortFromConfig == 0) { this.PortFromConfig = GetDefaultPort(this.Mode); }
                this.Destination = new IPEndPoint(IPAddress.Parse(this.IPFromConfig), this.PortFromConfig);
            }
        }

        /// <summary> Sets the pattern length and content based on the given pattern descriptor string. </summary>
        /// <param name="pattern"> Valid characters are 'R', 'G', 'B', 'Y'. Other characters cause an exception. </param>
        private void ReadLEDPattern(string pattern)
        {
            byte[] NewMapping = new byte[pattern.Length];
            bool HasYellow = false;
            for (int i = 0; i < NewMapping.Length; i++)
            {
                switch(pattern[i])
                {
                    case 'R': case 'r': NewMapping[i] = (byte)Channel.Red; continue;
                    case 'G': case 'g': NewMapping[i] = (byte)Channel.Green; continue;
                    case 'B': case 'b': NewMapping[i] = (byte)Channel.Blue; continue;
                    case 'Y': case 'y': NewMapping[i] = (byte)Channel.Yellow; HasYellow = true; continue;
                    default: throw new FormatException($"Invalid character in 'LEDPattern' of {nameof(PacketUDP)} found, '{pattern[i]}'. Valid characters are R, G, B, Y.");
                }
            }
            this.UsesChannelY = HasYellow;
            this.LEDLength = (byte)pattern.Length;
            this.LEDValueMapping = new byte[this.LEDLength];
        }

        /// <summary> Reads the name of the protocol specified in the config, and translates it to a <see cref="SendMode"/>. </summary>
        /// <param name="protocolName"> The name of the protocol, case-insensitive. </param>
        private static SendMode ReadProtocol(string protocolName)
        {
            if (protocolName.Equals("Raw", StringComparison.InvariantCultureIgnoreCase)) { return SendMode.RAW; }
            if (protocolName.Equals("TPM2.NET", StringComparison.InvariantCultureIgnoreCase)) { return SendMode.TPM2NET; }
            if (protocolName.Equals("E1.31", StringComparison.InvariantCultureIgnoreCase)) { return SendMode.E131; }
            throw new Exception($"Invalid protocol specified for UDP sender: \"{protocolName}\"");
        }

        /// <summary> Sends the newest data from the visualizer to our recipient. </summary>
        public void Dispatch()
        {
            if (!this.Enabled) { return; }
            switch (this.Mode)
            {
                case SendMode.RAW: SendRaw(); break;
                case SendMode.TPM2NET: SendTPM2Net(); break;
                case SendMode.E131: SendE131(); break;
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
            int SourceLEDCount = this.Source.GetCountDiscrete();
            uint[] SourceData = this.Source.GetDataDiscrete();
            int ReadStart = this.StartIndex;
            int ReadEnd = this.EndIndex < 0 ? SourceLEDCount : Math.Min(this.EndIndex, SourceLEDCount);
            uint LEDCount = (uint)Math.Max(0, ReadEnd - ReadStart);
            uint ByteCount = (LEDCount * this.LEDLength) + this.FrontPadding + this.BackPadding;
            if (this.SendBuffer.Length != ByteCount) { this.SendBuffer = new byte[ByteCount]; }
            byte[] Output = this.SendBuffer;

            // Front Padding
            Output.AsSpan(0, (int)this.FrontPadding).Fill(this.PaddingContent);
            uint Index = this.FrontPadding;

            // Data Content
            for (int LED = ReadStart; LED < ReadEnd; LED++)
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
                    Output[Index++] = (Channel)this.LEDValueMapping[Component] switch
                    {
                        Channel.Red => Red,
                        Channel.Green => Green,
                        Channel.Blue => Blue,
                        Channel.Yellow => Yellow,
                        _ => 0,
                    };
                }
            }

            // Back Padding
            Output.AsSpan((int)Index, Output.Length - (int)Index).Fill(this.PaddingContent);

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
            int SourceLEDCount = this.Source.GetCountDiscrete();
            uint[] SourceData = this.Source.GetDataDiscrete();
            int ReadStart = this.StartIndex;
            int ReadEnd = this.EndIndex < 0 ? SourceLEDCount : Math.Min(this.EndIndex, SourceLEDCount);
            int LEDCount = Math.Max(0, ReadEnd - ReadStart);
            if (this.SendBuffer.Length != 1497) { this.SendBuffer = new byte[1497]; }
            byte[] Output = this.SendBuffer;

            int LEDsPerPacket = 1490 / this.LEDLength; // 1490 is the maximum number of data bytes allowed by TPM2.net
            if (this.MaxPacketLength > 0 && this.MaxPacketLength < 1490) { LEDsPerPacket = this.MaxPacketLength / this.LEDLength; }

            if (LEDCount < LEDsPerPacket) { LEDsPerPacket = LEDCount; } // Only 1 packet needed.
            int PacketQty = (LEDCount + LEDsPerPacket - 1) / LEDsPerPacket; // Ceiling int divide
            int PacketSize = (LEDsPerPacket * this.LEDLength) + 7;

            // Packet header
            Output[0] = 0x9C; // Specifies TPM2.net
            Output[1] = 0xDA; // Data frame
            Output[5] = (byte)PacketQty; // Number of packets for this frame

            int LEDIndex = ReadStart; // The overall LED index (not reset per packet)
            for (int PacketNum = 0; PacketNum < PacketQty; PacketNum++)
            {
                Output[4] = (byte)(PacketNum + 1); // Packet numbers are 1-indexed.

                int DataIndex = 6;
                for (int LED = 0; LED < LEDsPerPacket && LEDIndex < ReadEnd; LED++)
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
                        Output[DataIndex++] = (Channel)this.LEDValueMapping[Component] switch
                        {
                            Channel.Red => Red,
                            Channel.Green => Green,
                            Channel.Blue => Blue,
                            Channel.Yellow => Yellow,
                            _ => 0x00,
                        };
                    }

                    LEDIndex++;
                }

                // If we have empty space, and we should pad the remainder to match sizes. Fills remainder of last packet in constant-length mode.
                if (this.UseConstantPacketLength) { Output.AsSpan(DataIndex, PacketSize - DataIndex - 1).Fill(this.PaddingContent); }

                Output[DataIndex++] = 0x36; // Packet end byte
                BinaryPrimitives.WriteUInt16BigEndian(Output.AsSpan(2, 2), (ushort)(DataIndex - 7)); // Packet length
                if (this.UseConstantPacketLength) { Debug.Assert(DataIndex == PacketSize); }

                this.Sender.Send(Output, DataIndex, this.Destination);
            }
        }

        private void SendE131()
        {
            int SourceLEDCount = this.Source.GetCountDiscrete();
            uint[] SourceData = this.Source.GetDataDiscrete();
            int ReadStart = this.StartIndex;
            int ReadEnd = this.EndIndex < 0 ? SourceLEDCount : Math.Min(this.EndIndex, SourceLEDCount);
            int LEDCount = Math.Max(0, ReadEnd - ReadStart);
            int SourceLength = LEDCount * this.LEDLength;
            if (SourceLength > 512) { throw new Exception($"E1.31 can only handle packets with up to 512 bytes of content, you have {SourceLength} bytes of data to send."); }
            if (this.SendBuffer.Length < this.E131Template.Length + 512) { this.SendBuffer = new byte[this.E131Template.Length + 512]; }
            byte[] Output = this.SendBuffer;

            this.E131Template.CopyTo(Output);

            int DMPLength = SourceLength + 1 + 10; // data length, 1 for start code, 10 for all other DMP header data.
            int FrameLength = 77 + DMPLength; // 77 for frame header
            int RootLength = 22 + FrameLength; // 22 for root header (excluding size and before)

            ushort FlagsAndLengthRoot =  (ushort)(0x7000U | (RootLength & 0x0FFFU));
            ushort FlagsAndLengthFrame = (ushort)(0x7000U | (FrameLength & 0x0FFFU));
            ushort FlagsAndLengthDMP =   (ushort)(0x7000U | (DMPLength & 0x0FFFU));

            BinaryPrimitives.WriteUInt16BigEndian(Output.AsSpan(16, 2), FlagsAndLengthRoot);
            BinaryPrimitives.WriteUInt16BigEndian(Output.AsSpan(38, 2), FlagsAndLengthFrame);
            Output[111] = this.E131Sequence;
            BinaryPrimitives.WriteUInt16BigEndian(Output.AsSpan(113, 2), this.Universe);
            BinaryPrimitives.WriteUInt16BigEndian(Output.AsSpan(115, 2), FlagsAndLengthDMP);
            BinaryPrimitives.WriteUInt16BigEndian(Output.AsSpan(123, 2), (ushort)(SourceLength + 1));

            int Index = this.E131Template.Length;

            for (int LED = ReadStart; LED < ReadEnd; LED++)
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
                    Output[Index++] = (Channel)this.LEDValueMapping[Component] switch
                    {
                        Channel.Red => Red,
                        Channel.Green => Green,
                        Channel.Blue => Blue,
                        Channel.Yellow => Yellow,
                        _ => 0,
                    };
                }
            }

            this.Sender.Send(Output, SourceLength + this.E131Template.Length, this.Destination);
            this.E131Sequence++;
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
            TPM2NET,
            E131
        }
    }
}
