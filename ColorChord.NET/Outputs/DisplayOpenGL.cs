using ColorChord.NET.Outputs.Display;
using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.ES30;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ColorChord.NET.Outputs
{
    public class DisplayOpenGL : GameWindow, IOutput
    {
        public IVisualizer Source { get; private set; }

        public readonly string Name;

        /// <summary> The width of the window, in pixels. </summary>
        public int WindowWidth
        {
            get => this.ClientSize.Width;
            set => this.ClientSize = new Size(value, this.Size.Height);
        }

        /// <summary> The height of the window, in pixels. </summary>
        public int WindowHeight
        {
            get => this.ClientSize.Height;
            set => this.ClientSize = new Size(this.Size.Width, value);
        }

        private IDisplayMode Display;
        private bool Loaded = false;

        public DisplayOpenGL(string name) : base(1280, 720, GraphicsMode.Default, "ColorChord.NET: " + name)
        {
            this.Name = name;
        }

        public void Start() { Run(60D); }
        public void Stop() { } // TODO: Stop

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for DisplayOpenGL \"" + this.Name + "\".");
            if (!options.ContainsKey("visualizerName") || !ColorChord.VisualizerInsts.ContainsKey((string)options["visualizerName"])) { Log.Error("Tried to create DisplayOpenGL with missing or invalid visualizer."); return; }
            this.Source = ColorChord.VisualizerInsts[(string)options["visualizerName"]];
            this.Source.AttachOutput(this);

            this.WindowWidth = ConfigTools.CheckInt(options, "windowWidth", 10, 4000, 1280, true);
            this.WindowHeight = ConfigTools.CheckInt(options, "windowHeight", 10, 4000, 720, true);

            if (options.ContainsKey("modes")) // Make sure that everything else is configured before creating the modes!
            {
                Dictionary<string, object>[] ModeList = (Dictionary<string, object>[])options["modes"];
                for (int i = 0; i < 1/*ModeList.Length*/; i++) // TODO: Add support for multiple modes.
                {
                    if (!ModeList[i].ContainsKey("type")) { Log.Error("Mode at index " + i + " is missing \"type\" specification."); continue; }
                    this.Display = CreateMode("ColorChord.NET.Outputs.Display." + ModeList[i]["type"], ModeList[i]);
                    if (this.Display == null) { Log.Error("Failed to create display of type \"" + ModeList[i]["type"] + "\" under \"" + this.Name + "\"."); }

                    // We already loaded, we want to make sure our display does as well.
                    if (this.Loaded) { this.Display?.Load(); }
                }
                if (ModeList.Length > 1) { Log.Warn("Config specifies multiple modes. This is not yet supported, so only the first one will be used."); }
                Log.Info("Finished reading display modes under \"" + this.Name + "\".");
            }

            ConfigTools.WarnAboutRemainder(options, typeof(IOutput));
        }

        private IDisplayMode CreateMode(string fullName, Dictionary<string, object> config)
        {
            Type ObjType = Type.GetType(fullName);
            if (!typeof(IDisplayMode).IsAssignableFrom(ObjType)) { return null; } // Does not implement the right interface.
            object Instance = ObjType == null ? null : Activator.CreateInstance(ObjType, this, this.Source);
            if (Instance != null)
            {
                IDisplayMode Instance2 = (IDisplayMode)Instance;
                if (Instance2 is IConfigurable InstanceForConfig) { InstanceForConfig.ApplyConfig(config); }
                else { Log.Warn("Display mode \"" + fullName + "\" does not support configuration."); }
                return Instance2;
            }
            return null;
        }

        protected override void OnLoad(EventArgs evt)
        {
            this.VSync = VSyncMode.On;
            GL.DebugMessageCallback(DebugCallback, IntPtr.Zero);
            GL.ClearColor(0.2F, 0.2F, 0.2F, 1.0F);

            this.Display?.Load();

            base.OnLoad(evt);
            this.Loaded = true;
        }

        protected override void OnRenderFrame(FrameEventArgs evt)
        {
            GL.Clear(ClearBufferMask.ColorBufferBit);

            this.Display?.Render();

            this.Context.SwapBuffers();
            base.OnRenderFrame(evt);
        }

        protected override void OnResize(EventArgs evt)
        {
            GL.Viewport(0, 0, this.Width, this.Height);
            this.Display?.Resize(this.Width, this.Height);
            base.OnResize(evt);
        }

        protected override void OnUnload(EventArgs evt)
        {
            this.Display?.Close();
            base.OnUnload(evt);
        }

        protected override void OnClosed(EventArgs e)
        {
            Environment.Exit(0);
            base.OnClosed(e);
        }

        protected void DebugCallback(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr message, IntPtr userParam)
        {
            Log.Warn("OpenGL Output: Type \"" + type + "\", Severity \"" + severity + "\", Message \"" + Marshal.PtrToStringAnsi(message) + "\".");
        }

        public void Dispatch() => this.Display?.Dispatch();

    }
}
