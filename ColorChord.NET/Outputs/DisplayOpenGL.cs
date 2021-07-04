using ColorChord.NET.Config;
using ColorChord.NET.Outputs.Display;
using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using OpenTK.Graphics.ES30;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ColorChord.NET.Outputs
{
    public class DisplayOpenGL : GameWindow, IOutput
    {
        public IVisualizer Source { get; private set; }

        public readonly string Name;

        /// <summary> The width of the window contents, in pixels. </summary>
        [ConfigInt("WindowWidth", 10, 4000, 1280)]
        public int Width
        {
            get => this.ClientSize.X;
            set => this.ClientRectangle = new Box2i(this.ClientRectangle.Min, new Vector2i(this.ClientRectangle.Min.X + value, this.ClientRectangle.Max.Y));
        }

        /// <summary> The height of the window contents, in pixels. </summary>
        [ConfigInt("WindowHeight", 10, 4000, 720)]
        public int Height
        {
            get => this.ClientSize.Y;
            set => this.ClientRectangle = new Box2i(this.ClientRectangle.Min, new Vector2i(this.ClientRectangle.Max.X, this.ClientRectangle.Min.Y + value));
        }

        private readonly IDisplayMode Display;
        private bool Loaded = false;

        public DisplayOpenGL(string name, Dictionary<string, object> config) : base(GameWindowSettings.Default, SetupNativeWindow())
        {
            this.Name = name;
            this.Title = "ColorChord.NET: " + this.Name;
            this.Source = Configurer.FindVisualizer(this, config);
            Configurer.Configure(this, config);

            if (config.ContainsKey("Modes")) // Make sure that everything else is configured before creating the modes!
            {
                Dictionary<string, object>[] ModeList = (Dictionary<string, object>[])config["Modes"];
                for (int i = 0; i < 1/*ModeList.Length*/; i++) // TODO: Add support for multiple modes.
                {
                    if (!ModeList[i].ContainsKey("Type")) { Log.Error("Mode at index " + i + " is missing \"Type\" specification."); continue; }
                    this.Display = CreateMode("ColorChord.NET.Outputs.Display." + ModeList[i]["Type"], ModeList[i]);
                    if (this.Display == null) { Log.Error("Failed to create display of type \"" + ModeList[i]["Type"] + "\" under \"" + this.Name + "\"."); }

                    // We already loaded, we want to make sure our display does as well.
                    if (this.Loaded) { this.Display?.Load(); }
                }
                if (ModeList.Length > 1) { Log.Warn("Config specifies multiple modes. This is not yet supported, so only the first one will be used."); }
                Log.Info("Finished reading display modes under \"" + this.Name + "\".");
            }

            this.Source.AttachOutput(this);
        }

        private static NativeWindowSettings SetupNativeWindow()
        {
            NativeWindowSettings Output = NativeWindowSettings.Default;
            Output.StartVisible = false;
            return Output;
        }

        public void Start()
        {
            this.IsVisible = true;
            Run();
        }
        public void Stop() { } // TODO: Stop

        private IDisplayMode CreateMode(string fullName, Dictionary<string, object> config)
        {
            Type ObjType = Type.GetType(fullName);
            if (!typeof(IDisplayMode).IsAssignableFrom(ObjType) || ObjType == null) { return null; } // Does not implement the right interface.

            bool IsConfigurable = typeof(IConfigurableAttr).IsAssignableFrom(ObjType);

            object Instance = null;
            try
            {
                if (IsConfigurable) { Instance = Activator.CreateInstance(ObjType, this, this.Source, config); }
                else { Instance = Activator.CreateInstance(ObjType, this, this.Source); }
            }
            catch (MissingMethodException exc)
            {
                Log.Error("Could not create an instance of \"" + fullName + "\".");
                Console.WriteLine(exc);
            }

            return Instance == null ? null : (IDisplayMode)Instance;
        }

        protected override void OnLoad()
        {
            this.VSync = VSyncMode.On;
            GL.DebugMessageCallback(DebugCallback, IntPtr.Zero);
            GL.ClearColor(0.2F, 0.2F, 0.2F, 1.0F);

            this.Display?.Load();

            base.OnLoad();
            this.Loaded = true;
        }

        protected override void OnRenderFrame(FrameEventArgs evt)
        {
            GL.Clear(ClearBufferMask.ColorBufferBit);

            this.Display?.Render();

            this.Context.SwapBuffers();
            base.OnRenderFrame(evt);
        }

        protected override void OnResize(ResizeEventArgs evt)
        {
            GL.Viewport(0, 0, this.Width, this.Height);
            this.Display?.Resize(this.Width, this.Height);
            base.OnResize(evt);
        }

        protected override void OnMaximized(MaximizedEventArgs evt) => OnResize(new ResizeEventArgs());
        protected override void OnMinimized(MinimizedEventArgs evt) => OnResize(new ResizeEventArgs());

        protected override void OnUnload()
        {
            this.Display?.Close();
            base.OnUnload();
        }

        protected override void OnClosed()
        {
            Environment.Exit(0);
            base.OnClosed();
        }

        protected void DebugCallback(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr message, IntPtr userParam)
        {
            Log.Warn("OpenGL Output: Type \"" + type + "\", Severity \"" + severity + "\", Message \"" + Marshal.PtrToStringAnsi(message) + "\".");
        }

        public void Dispatch() => this.Display?.Dispatch();
    }
}
