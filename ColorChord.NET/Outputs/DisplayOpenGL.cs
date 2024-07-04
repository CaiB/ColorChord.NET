using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Outputs.Display;
using ColorChord.NET.API.Utility;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.Config;
using OpenTK.Graphics.ES30;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ColorChord.NET.Outputs
{
    public class DisplayOpenGL : GameWindow, IOutput, IControllableAttr, IThreadedInstance
    {
        private static readonly DebugProc DebugCallbackRef = DebugCallback; // Needed to prevent GC

        public NoteFinderCommon NoteFinder { get; private init; }
        public IVisualizer Source { get; private set; }

        public string Name { get; private init; }

        /// <summary> The width of the window contents, in pixels. </summary>
        [Controllable("WindowWidth")] // TODO: Check whether this changing causes any thread safety problems
        [ConfigInt("WindowWidth", 10, 4000, 1280)]
        public int Width
        {
            get => this.ClientSize.X;
            set => this.ClientRectangle = new Box2i(this.ClientRectangle.Min, new Vector2i(this.ClientRectangle.Min.X + value, this.ClientRectangle.Max.Y));
        }

        /// <summary> The height of the window contents, in pixels. </summary>
        [Controllable("WindowHeight")]
        [ConfigInt("WindowHeight", 10, 4000, 720)]
        public int Height
        {
            get => this.ClientSize.Y;
            set => this.ClientRectangle = new Box2i(this.ClientRectangle.Min, new Vector2i(this.ClientRectangle.Max.X, this.ClientRectangle.Min.Y + value));
        }

        [Controllable(ConfigNames.ENABLE)]
        [ConfigBool(ConfigNames.ENABLE, true)]
        public bool Enabled { get; set; }

        [Controllable("UseVSync", 1)]
        [ConfigBool("UseVSync", true)]
        public bool UseVSync { get; set; } = true;

        private int DefaultWidth, DefaultHeight;

        private readonly IDisplayMode? Display;
        private bool Stopping = false;
        
        public DisplayOpenGL(string name, Dictionary<string, object> config) : base(GameWindowSettings.Default, SetupNativeWindow())
        {
            this.Name = name;
            this.Title = "ColorChord.NET: " + this.Name;
            IVisualizer? Visualizer = Configurer.FindVisualizer(config) ?? throw new InvalidOperationException($"{GetType().Name} cannot find visualizer to attach to");
            this.Source = Visualizer;

            Configurer.Configure(this, config);
            this.NoteFinder = Configurer.FindNoteFinder(config) ?? this.Source.NoteFinder ?? throw new Exception($"{nameof(DisplayOpenGL)} {this.Name} could not find NoteFinder to get data from.");
            this.DefaultWidth = this.Width;
            this.DefaultHeight = this.Height;

            if (config.TryGetValue("Modes", out object? ModesObj)) // Make sure that everything else is configured before creating the modes!
            {
                Dictionary<string, object>[] ModeList = (Dictionary<string, object>[])ModesObj;
                for (int i = 0; i < 1/*ModeList.Length*/; i++) // TODO: Add support for multiple modes.
                {
                    if (!ModeList[i].TryGetValue(ConfigNames.TYPE, out object? TypeObj)) { Log.Error($"Mode at index {i} is missing \"{ConfigNames.TYPE}\" specification."); continue; }
                    this.Display = CreateMode("ColorChord.NET.Outputs.Display." + TypeObj, ModeList[i]);
                    if (this.Display == null) { Log.Error($"Failed to create display of type \"{TypeObj}\" under \"{this.Name}\"."); }
                }
                if (ModeList.Length > 1) { Log.Warn("Config specifies multiple modes. This is not yet supported, so only the first one will be used."); }
                Log.Info($"Finished reading display modes under \"{this.Name}\".");
            }

            this.Source.AttachOutput(this);
        }

        private static NativeWindowSettings SetupNativeWindow()
        {
            NativeWindowSettings Output = NativeWindowSettings.Default;
            Output.StartVisible = false;
            return Output;
        }

        public void InstThreadPostInit()
        {
            if (!this.Enabled) { return; }
            this.IsVisible = true;
            Run();
        }

        public void Start() { }

        public void Stop() => this.Stopping = true;

        private IDisplayMode? CreateMode(string fullName, Dictionary<string, object> config)
        {
            Type? ObjType = Type.GetType(fullName);
            if (ObjType == null) { Log.Error($"Cannot find display mode type {fullName}!"); return null; }
            if (!typeof(IDisplayMode).IsAssignableFrom(ObjType)) { Log.Error($"Requested display mode {fullName} is not a valid display mode (must be IDisplayMode)."); return null; }

            bool IsConfigurable = typeof(IConfigurableAttr).IsAssignableFrom(ObjType);

            object? Instance = null;
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

        public void SettingWillChange(int controlID) { }
        public void SettingChanged(int controlID)
        {
            if (controlID == 1) { this.VSync = this.UseVSync ? VSyncMode.On : VSyncMode.Off; }
        }

        protected override void OnLoad()
        {
            this.VSync = this.UseVSync ? VSyncMode.On : VSyncMode.Off;
            //GL.DebugMessageCallback(DebugCallbackRef, IntPtr.Zero);
            GL.ClearColor(0.2F, 0.2F, 0.2F, 1.0F);

            this.Display?.Load();

            base.OnLoad();
        }

        protected override void OnRenderFrame(FrameEventArgs evt)
        {
            if (this.Stopping) { this.Display?.Close(); return; }
            this.NoteFinder.UpdateOutputs();
            GL.Clear(ClearBufferMask.ColorBufferBit);

            this.Display?.Render();

            this.Context.SwapBuffers();
            base.OnRenderFrame(evt);
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            if (this.Stopping) { this.Display?.Close(); return; }
            base.OnUpdateFrame(args);
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            if (e.Key == Keys.F11 || e.Key == Keys.F) { ToggleFullscreen(); }
            if (e.Key == Keys.Escape && this.WindowState == WindowState.Fullscreen) { ToggleFullscreen(); }
            base.OnKeyDown(e);
        }

        private void ToggleFullscreen()
        {
            if (this.WindowState != WindowState.Fullscreen) { this.WindowState = WindowState.Fullscreen; }
            else
            {
                this.WindowState = WindowState.Normal;
                this.Width = this.DefaultWidth;
                this.Height = this.DefaultHeight;
            }
            this.VSync = VSyncMode.On;
            OnResize(new(this.Width, this.Height));
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
            Stop();
            base.OnUnload();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            ColorChord.Stop();
        }

        protected static void DebugCallback(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr message, IntPtr userParam)
        {
            Log.Warn("OpenGL Output: Type \"" + type + "\", Severity \"" + severity + "\", Message \"" + Marshal.PtrToStringAnsi(message) + "\".");
        }

        public void Dispatch() => this.Display?.Dispatch();
    }
}
