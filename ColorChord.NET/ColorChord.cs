using ColorChord.NET.Outputs;
using ColorChord.NET.Sources;
using ColorChord.NET.Visualizers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace ColorChord.NET
{
    public class ColorChord
    {

        public static void Main(string[] args)
        {
            NoteFinder.Start();

            ReadConfig();

            Linear Linear = new Linear(400, false);
            //if (DoLinear) { Linear.Start(); }
            Linear.Start();

            Cells Cells = new Cells(50);
            //if (!DoLinear) { Cells.Start(); }
            Cells.Start();

            //PacketUDP NetworkLin = new PacketUDP(Linear, "192.168.0.60", 7777, 1);
            //PacketUDP NetworkCel = new PacketUDP(Cells, "192.168.0.60", 7777, 1);
            DisplayOpenGL TestDisp = new DisplayOpenGL(Linear);

            //DisplayOpenGL TestDispCells = new DisplayOpenGL(Cells);

            
        }

        public static Dictionary<string, IVisualizer> Visualizers;
        public static Dictionary<string, IOutput> Outputs;
        //public static Dictionary<string, IController> Controllers;
        public static IAudioSource Source;

        public static void ReadConfig()
        {
            JObject JSON;
            using (StreamReader Reader = File.OpenText("config.json")) { JSON = JObject.Parse(Reader.ReadToEnd()); }
            Console.WriteLine("Config version is " + JSON["configVersion"]);
            Console.WriteLine("Reading and applying configuration file...");
            if (!JSON.ContainsKey("source") || !JSON["source"].HasValues) { Console.WriteLine("[WARN] Could not find valid \"source\" definition. No audio source will be configured."); }
            else
            {
                IAudioSource Source = CreateSource((string)JSON["source"]["type"], JSON["source"]);
                if (Source != null)
                {
                    ColorChord.Source = Source;
                    Console.WriteLine("[INF] Created audio source of type \"" + Source.GetType().FullName + "\".");
                    ColorChord.Source.Start();
                }
                else { Console.WriteLine("[ERR] Failed to create audio source. Check to make sure the type is spelled correctly."); }
            }
        }

        private static IAudioSource CreateSource(string typeName, JToken configEntry)
        {
            Type SourceType = Type.GetType("ColorChord.NET.Sources." + typeName);
            object Instance = SourceType == null ? null : Activator.CreateInstance(SourceType);
            if (Instance != null) { return (IAudioSource)Instance; }
            else
            {
                Console.WriteLine("[ERR] Failed to create instance of audio source \"" + typeName + "\".");
                return null;
            }
        }

    }
}
