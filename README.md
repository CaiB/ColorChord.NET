# ColorChord.NET
My port of [CNLohr's ColorChord2](https://github.com/cnlohr/colorchord) to C#.NET, allowing for more flexibility and extendability. Also includes significantly more documentation, see below for explanations of all modes & config options.

**[You can find binary downloads here.](https://github.com/CaiB/ColorChord.NET/releases)**

**:warning: This is a music visualizer tool, and both this documentation page and the program have bright, coloured, potentially fast flashing lights. If you are epileptic, I recommend avoiding this project and page.**

Uses [Vannatech/dorba's netcoreaudio](https://github.com/dorba/netcoreaudio) for WASAPI support.

Somewhat different from Charles' version, I divided components into 5 categories:
- Audio Sources: Pipes audio data from some location into the NoteFinder. (e.g. WASAPI Loopback)
- Note Finder: Turns raw audio data into note information. Currently not replaceable.
- Visualizers: Takes note info from the NoteFinder, and turns it into a format that is outputtable via some method. (e.g. Linear)
- Outputs: Takes data from a visualizer, and actually displays/outputs it somewhere. (e.g. UDP packets)
- Controllers: Edits the behaviour of any of the above system components during runtime.

A single instance of the application supports a single audio source, any number of visualizers, each with its own set of (any number of) outputs. This allows for a single audio stream to be processed and displayed in almost any desired combination of ways.

Only some sources, visualizers, and outputs from the base version have been implemented, but some new additions are also available.

The core math is done in a C library, `ColorChordLib` due to complexity.

The system performs very well, requiring negligible CPU and RAM, especially if only network output is needed.

I try to maintain the same behaviour given the same inputs as CNLohr's version. If you notice an undocumented difference, please let me know.

# Configuration
Configuration is done through the `config.json` file. There is a [sample config provided](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/sample-config.json), but you'll want to customize it to suit your needs. Find the supported options for each module below.

- If a config file is not found, a default `config.json` will be created for you when you run the program. (The default config has the same content as the sample linked above)
- If an option is not specified in the configuration file, the default value is used.
- If an unrecognized option or invalid value is specified, a warning is output to the console. Always check for these after modifying the config in case you made a spelling mistake.
- You can choose a different configuration file by running the program with the command line option `config <YourFile.json>`.
- Range specifies the set of input values that _can_ be used. Extreme values may not make any sense in practice though, so make small changes from the defaults to start. Range is just specified to prevent completely invalid input.

_* indicates an uncertain description. I don't fully understand the methodology of some of the visualizers that Charles included in ColorChord, so some of these are guesses. Play with the values until you get something nice :)_

### Moving a config file from CNLohr's ColorChord
- The parameters for almost everything are backwards-compatible with cnlohr's version, so you can usually copy the **values**. If there is a difference, it should be noted in the relevant section below.
- Note that the config file structure and parameter names are quite different, so a simple copy-paste of the entire file will not work.
- If you want to replicate an existing setup, start with the default `config.json` file, and copy parameters over to my format one-by-one, finding the appropriate place to put them.

# Sources
**Only one source can be defined at once currently.**
### [WASAPILoopback](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Sources/WASAPILoopback.cs)
Gets data out of the Windows Audio Session API. Supports input and output devices (e.g. microphones or the system speaker output, etc)
<details>
<summary>View Configuration Table</summary>

| Name | Type | Default | Range | Description |
|---|---|---|---|---|
| `device` | `string` | `"default"` | `"default"`, ~~`"defaultTracking"`~~, Device IDs | If `"default"`, then the default device at the time of startup will be used. If `"defaultTracking"`, the default device will be used, and will keep up with changes to the default, switching as the system does (not yet implemented). If a device ID is sepcified, that device is used, but if it is not found, then behaviour reverts to `"default"`. |
| `useInput` | `bool` | `false` | | Determines whether to choose the default capture device (e.g. microphone), or default render device (e.g. speakers) when choosing a device. Only useful if the default device is selected in `device` (above).
| `printDeviceInfo` | `bool` | `true` | | If `true`, outputs currently connected devices and their IDs at startup, to help you find a device. |
</details>

**Regarding Device IDs:**  
Device IDs are unique for each device on the system, vary between different computers, and only change if drivers are updated/changed. Removal and re-attachment of a USB device will not change the ID. They are not readily visible to the user, but other software using WASAPI will have access to the same IDs. Use `printDeviceInfo` (above) to find the ID for your preferred device. Output format is:
> [`Index`] "`Device Name`" = "`Device ID`"

(Index is not used, it is just present to make the list easier to read)

# [NoteFinder](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/NoteFinder/BaseNoteFinder.cs)
There is always a single instance of the NoteFinder running. All sources and visualizers connect to the NoteFinder.

The NoteFinder uses a complex, lengthy algorithm to turn sound data into note information. The options below are mostly listed in the order used.

<details>
<summary>View Configuration Table</summary>

| Name | cnlohr Name | Type | Default | Range | Description |
|---|---|---|---|---|---|
| `StartFreq` | `base_hz` | `float` | 65.4064 | 0.0-20000.0 | The minimum frequency analyzed. (in Hz) :information_source: See note below. |
| `DFTIIR` | `dft_iir` | `float` | 0.65 | 0.0~1.0 | Determines how much the previous frame's DFT data is used in the next frame. Smooths out rapid changes from frame-to-frame, but can cause delay if too strong. | 
| `DFTAmplify` | `amplify` | `float` | 2.0 | 0.0~10000.0 | Determines how much the raw DFT data is amplified before being used. |
| `DFTSlope` | `slope` | `float` | 0.1 | 0.0~10000.0 | The slope of the extra frequency-dependent amplification done to raw DFT data. Positive values increase sensitivity at higher frequencies. |
| `OctaveFilterIterations` | `filter_iter` | `int` | 2 | 0~10000 | How often to run the octave data filter. This smoothes out each bin with adjacent ones. | 
| `OctaveFilterStrength` | `filter_strength` | `float` | 0.5 | 0.0~1.0 | How strong the octave data filter is. Higher values mean each bin is more aggresively averaged with adjacent bins. Higher values mean less glitchy, but also less clear note peaks. |
| `NoteInfluenceDist` | `note_jumpability` | `float` | 1.8 | 0.0~100.0 | How close a note needs to be to a distribution peak in order to be merged. |
| `NoteAttachFreqIIR` | `note_attach_freq_iir` | `float` | 0.3 | 0.0~1.0 | How strongly the note merging filter affects the note frequency. Stronger filter means notes take longer to shift positions to move together. |
| `NoteAttachAmpIIR` | `note_attach_amp_iir` | `float` | 0.35 | 0.0~1.0 | How strongly the note merging filter affects the note amplitude. Stronger filter means notes take longer to merge fully in amplitude. |
| `NoteAttachAmpIIR2` | `note_attach_amp_iir2` | `float` | 0.25 | 0.0~1.0 | This filter is applied to notes between cycles in order to smooth their amplitudes over time. |
| `NoteCombineDistance` | `note_combine_distance` | `float` | 0.5 | 0.0~100.0 | How close two existing notes need to be in order to get combined into a single note. |
| `NoteOutputChop` | `note_out_chop` | `float` | 0.05 | 0.0~100.0 | Notes below this value get zeroed. Increase if low-amplitude notes are causing noise in output. |
</details>

> :information_source: The default configuration of `StartFreq` is different than cnlohr's implementation. If you want behaviour to match with his default configurations, change `StartFreq` to `55.0`.

# Visualizers

You may add as many visualizers as you desire, even multiple of the same type. All visualizer instances must have at least these 2 string properties:
* `Type`: The name of the visualizer to use. Must match the titles below.
* `Name`: A unique identifier used to attach outputs and controllers.
## [Cells](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Visualizers/Cells.cs)
![Example](Docs/Images/Output-Display-Cells.gif)
(Output shown: `DisplayOpenGL:BlockStrip`, 30 blocks, `TimeBased`=true)  

Supported data output modes: `Discrete 1D`  
A 1D output with cells appearing and decaying in a scattered pattern.
<details>
<summary>View Configuration Table</summary>

| Name | cnlohr Name | Type | Default | Range | Description |
|---|---|---|---|---|---|
| `LEDCount` | `leds` | `int` | 50 | 1~100000 | The number of discrete data points to output. |
| `FrameRate` | | `int` | 60 | 0~1000 | The number of data frames to attempt to calculate per second. Determines how fast the data is outputted. |
| `LEDFloor` | `led_floor` | `float` | 0.1 | 0.0~1.0 | *The minimum intensity of an LED, before it is output as black instead. |
| `LightSiding` | `light_siding` | `float` | 1.9 | 0.0~100.0 | *Not sure. |
| `SaturationAmplifier` | `satamp` | `float` | 2.0 | 0.0~100.0 | *Multiplier for colour saturation before conversion to RGB and output. |
| `QtyAmp` | `qtyamp` | `float` | 20 | 0.0~100.0 | *Not sure. |
| `SteadyBright` | `seady_bright` or `steady_bright` | `bool` | false | | *Not sure. |
| `TimeBased` | `bool` | `timebased` | false | | *Whether lights get added from the left side creating a time-dependent decay pattern, or are added randomly. |
| `Snakey` | `snakey` | `bool` | false | | *Not sure. |
| `Enable` | | `bool` | true | | Whether to use this visualizer. |
</details>

## [Linear](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Visualizers/Linear.cs)
![Example](Docs/Images/Output-Display-LinearSmooth.gif)
(Output shown: `DisplayOpenGL:SmoothStrip`, continuous mode)  
Supported data output modes: `Discrete 1D`, `Continuous 1D`  
A 1D output with contiguous blocks of colour, size corresponding to relative note volume, and with inter-frame continuity.
<details>
<summary>View Configuration Table</summary>

| Name | cnlohr Name | Type | Default | Range | Description |
|---|---|---|---|---|---|
| `LEDCount` | `leds` | `int` | 50 | 1~100000 | The number of discrete data points to output. Set this to a low value like 24 if only continuous output is used to save CPU time. |
| `LightSiding` | `light_siding` | `float` | 1.0 | 0.0~100.0 | Exponent used to convert raw note amplitudes to strength. |
| `LEDFloor` | `led_floor` | `float` | 0.1 | 0.0~1.0 | The minimum relative amplitude of a note required to consider it for output. |
| `FrameRate` | | `int` | 60 | 0~1000 | The number of data frames to attempt to calculate per second. Determines how fast the data is output. |
| `IsCircular` | `is_loop` | `bool` | false | | Whether to treat the output as a circle, allowing wrap-around, or as a line with defined ends. :information_source: See below note. |
| `SteadyBright` | `steady_bright` | `bool` | false | | Applies inter-frame smoothing to the LED brightnesses to prevent fast flickering. |
| `LEDLimit` | `led_limit` | `float` | 1.0 | 0.0~1.0 | The maximum LED brightness. Caps all LEDs at this value, but does not affect values below this threshold. |
| `SaturationAmplifier` | `satamp` | `float` | 1.6 | 0.0~100.0 | Multiplier for colour saturation before conversion to RGB and output. |
| `Enable` | | `bool` | true | | Whether to use this visualizer. |
</details>

> :information_source: `"IsCircular": true` in continuous mode does not match the behaviour of base ColorChord, as it uses a different, custom algorithm for positioning. However, discrete mode should match the base version. `"IsCircular": false` should match base ColorChord in both continuous and discrete mode.

# Outputs
You may add as many outputs as you desire, even multiple of the same type, and any combination of compatible outputs can be added to a single visualizer. All output instances must have at least these 3 string properties:
* `Type`: The name of the output to use. Must match the titles below.
* `Name`: A unique identifier used to attach controllers.
* `VisualizerName`: The `Name` property of the visualizer instance to attach to.

## [DisplayOpenGL](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Outputs/DisplayOpenGL.cs)
Supported input modes: Depends on display mode.  
Behaviour depends on the display mode chosen, but uses OpenGL to render graphics to a single window on the screen.

> Currently only one display mode is supported at a time.

<details>
<summary>View Configuration Table</summary>

| Name | Type | Default | Range | Description |
|---|---|---|---|---|
| `WindowWidth` | `int` | 1280 | 10~4000 | The width of the window, in pixels. |
| `WindowHeight` | `int` | 720 | 10~4000 | The height of the window, in pixels. |
| `Mode` | `object array` | | | The mode(s) to use. See the subsection below.
</details>

<details>
<summary>Display Modes</summary>

Every display mode is required to have a `"type"` configured, matching one of the headings below.

### [BlockStrip](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Outputs/Display/BlockStrip.cs)
![Example](Docs/Images/Output-Display-LinearBlock.gif)
(Visualizer shown: `Linear`, 15 blocks)  
Supported input modes: `Discrete 1D`  
Number of blocks displayed adjusts to match the attached visualizer.  
> No additional configuration is available.

### [SmoothStrip](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Outputs/Display/SmoothStrip.cs)
![Example](Docs/Images/Output-Display-LinearSmooth.gif)
(Visualizer shown: `Linear`)  
Supported input modes: `Continuous 1D`  
> No additional configuration is available.

### [SmoothCircle](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Outputs/Display/SmoothCircle.cs)
Supported input modes: `Continuous 1D`
| Name | Type | Default | Range | Description |
|---|---|---|---|---|
| `IsInfinity` | `bool` | false | | `false` just renders the ring, `true` also renders a decaying persistence effect, appearing to go off to infinity. |
</details>

## [PacketUDP](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Outputs/PacketUDP.cs)
Supported input modes: `Discrete 1D`  
Number of LEDs sent adjusts to match the attached visualizer.  
Packs the data for each LED in sequence into a UDP packet, then sends it to a given IP.
<details>
<summary>View Configuration Table</summary>

| Name | cnlohr Name | Type | Default | Range | Description |
|---|---|---|---|---|---|
| `IP` | `address` | `string` | 127.0.0.1 | Valid IPs | The IP to send the packets to. |
| `Port` | `port` | `int` | 7777 | 0~65535 | The port to send the packets to. |
| `PaddingFront` | `skipfirst` | `int` | 0 | 0~1000 | Blank bytes to append to the front of the packet. |
| `PaddingBack` | | `int` | 0 | 0~1000 | Blank bytes to append to the back of the packet. |
| `Enable` | | `bool` | true | | Whether to use this output. |
</details>

> Packets have 65,535 byte size limit. This means no more than 21,835 RGB LEDs can be output at once.

# Controllers
Not yet implemented.

# Development
I work in Visual Studio 2019, and auto-builds are done by AppVeyor.  
To prevent a commit from triggering an auto-build and release, prepend the commit message with `[NAB]`.

**Random other notes for myself:**

Compiling .so from C source on Linux:  
`gcc [Input File].c -shared -fpic -o [Output File].so`