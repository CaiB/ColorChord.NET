# ColorChord.NET
My WIP port of [CNLohr's ColorChord2](https://github.com/cnlohr/colorchord) to C#.NET, allowing for more flexibility and extendability. Also uses new WASAPI Loopback methods for getting audio on Windows, which appears to work more reliably.

**[You can find binary downloads here.](https://github.com/CaiB/ColorChord.NET/releases)**

Uses [Vannatech/dorba's netcoreaudio](https://github.com/dorba/netcoreaudio) for WASAPI support.

Somewhat different from Charles' version, I divided components into 4 categories, centered around the `NoteFinder`:
- Audio Sources: Pipes audio data from some location into the NoteFinder. (e.g. WASAPI Loopback)
- Visualizers: Takes note info from the NoteFinder, and turns it into a format that is outputtable via some method. (e.g. Linear)
- Outputs: Takes data from a visualizer, and actually displays/outputs it somewhere. (e.g. UDP packets)
- Controllers: Edits the behaviour of any of the above system components during runtime.

A single instance of the application supports a single audio source, any number of visualizers, each with its own set of (any number of) outputs. This allows for a single audio stream to be processed and displayed in almost any desired combination of ways.

Only some sources, visualizers, and outputs have been implemented.

The core math is done in a C library, `ColorChordLib` due to complexity.

The system performs very well, requiring negligible CPU and RAM, especially if only network output is needed.

# Configuration
Configuration is done through the `config.json` file. There is a sample config provided at the root of the repo, but you'll want to customize it to suit your needs. Below is a list of supported options:

- You can choose a different configuration file by running the program with the command line option `config <YourFile.json>`.

_* indicates an uncertain description. I don't fully understand the methodology of some of the visualizers that Charles included in ColorChord, so some of these are guesses. Play with the vaules until you get something nice :)_

## Sources
**Only one source can be defined at once currently.**
### [WASAPILoopback](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Sources/WASAPILoopback.cs)
Gets data out of the Windows Audio Session API. Supports input and output devices (e.g. microphones or the system speaker output, etc)
| Name | Type | Default | Range | Description |
|---|---|---|---|---|
| `device` | `string` | `"default"` | `"default"`, `"defaultTracking"`, Device IDs | If `"default"`, then the default device at the time of startup will be used. If `"defaultTracking"`, the default device will be used, and will keep up with changes to the default, switching as the system does (not yet implemented). If a device ID is sepcified, that device is used, but if it is not found, then behaviour reverts to `"default"`. |
| `useInput` | `bool` | `false` | | Determines whether to choose the default capture device (e.g. microphone), or default render device (e.g. speakers) when choosing a device. Only useful if the default device is selected in `device` (above).
| `printDeviceInfo` | `bool` | `true` | | If `true`, outputs currently connected devices and their IDs at startup, to help you find a device. |

**Regarding Device IDs:**  
Device IDs are unique for each device on the system, vary between different computers, and only change if drivers are updated/changed. Removal and re-attachment of a USB device will not change the ID. They are not readily visible to the user, but other software using WASAPI will have access to the same IDs. Use `printDeviceInfo` (above) to find the ID for your preferred device. Output format is:
> [`Index`] "`Device Name`" = "`Device ID`"

## Visualizers

You may add as many visualizers as you desire, even multiple of the same type. All visualizer instances must have at least these 2 string properties:
* `type`: The name of the visualizer to use. Must match the titles below.
* `name`: A unique identifier used to attach outputs and controllers.
### [Cells](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Visualizers/Cells.cs)
A 1D output with cells appearing and decaying in a scattered pattern.
| Name | Type | Default | Range | Description |
|---|---|---|---|---|
| `ledCount` | `int` | 50 | 1~100000 | The number of discrete data points to output. |
| `frameRate` | `int` | 60 | 0~1000 | The number of data frames to attempt to calculate per second. Determines how fast the data is outputted. |
| `ledFloor` | `float` | 0.1 | 0~1 | *The minimum intensity of an LED, before it is output as black instead. |
| `lightSiding` | `float` | 1.9 | 0~100 | *Not sure. |
| `saturationAmplifier` | `float` | 2 | 0~100 | *Multiplier for colour saturation before conversion to RGB and output. |
| `qtyAmp` | `float` | 20 | 0~100 | *Not sure. |
| `steadyBright` | `bool` | false | | *Not sure. |
| `timeBased` | `bool` | false | | *Whether lights get added from the left side creating a time-dependent decay pattern, or are added randomly. |
| `snakey` | `bool` | false | | *Not sure. |
| `enable` | `bool` | true | | Whether to use this visualizer. |

### [Linear](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Visualizers/Linear.cs)
A 1D output with contiguous blocks of colour, size corresponding to relative note volume, and inter-frame continuity.
| Name | Type | Default | Range | Description |
|---|---|---|---|---|
| `ledCount` | `int` | 50 | 1~100000 | The number of discrete data points to output. |
| `lightSiding` | `float` | 1.0 | 0~100 | *Not sure. |
| `ledFloor` | `float` | 0.1 | 0~1 | *The minimum intensity of an LED, before it is output as black instead. |
| `frameRate` | `int` | 60 | 0~1000 | The number of data frames to attempt to calculate per second. Determines how fast the data is outputted. |
| `isCircular` | `bool` | false | | Whether to treat the output as a circle, allowing wrap-around, or as a line with hard ends. |
| `steadyBright` | `bool` | false | | *Not sure. |
| `ledLimit` | `float` | 1.0 | 0~1 | *The maximum LED brightness. |
| `saturationAmplifier` | `float` | 1.6 | 0~100 | *Multiplier for colour saturation before conversion to RGB and output. |
| `enable` | `bool` | true | | Whether to use this visualizer. |

## Outputs
You may add as many outputs as you desire, even multiple of the same type, and any combination of compatible outputs can be added to a single visualizer. All output instances must have at least these 3 string properties:
* `type`: The name of the output to use. Must match the titles below.
* `name`: A unique identifier used to attach controllers.
* `visualizerName`: The `name` property of the visualizer instance to attach to.

### [DisplayOpenGL](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Outputs/DisplayOpenGL.cs)
Currently supports 1D inputs only. Acts like a strip of LEDs, displaying a horizontal line of rectangles.
| Name | Type | Default | Range | Description |
|---|---|---|---|---|
| `paddingLeft` | `float` | 0 | 0~2 | Amount of blank space to leave on the left side of the window. 1 corresponds to half of the window. |
| `paddingRight` | `float` | 0 | 0~2 | Amount of blank space to leave on the right side of the window. 1 corresponds to half of the window. |
| `paddingTop` | `float` | 0 | 0~2 | Amount of blank space to leave on the top of the window. 1 corresponds to half of the window. |
| `paddingBottom` | `float` | 0 | 0~2 | Amount of blank space to leave on the bottom of the window. 1 corresponds to half of the window. |
| `windowHeight` | `int` | 100 | 10~4000 | The height of the window, in pixels. |
| `windowWidth` | `int` | 1280 | 10~4000 | The width of the window, in pixels. |

### [PacketUDP](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/Outputs/PacketUDP.cs)
Currently supports 1D inputs only. Packs the data for each LED in sequence into a UDP packet, then sends it to a given IP.

| Name | Type | Default | Range | Description |
|---|---|---|---|---|
| `ip` | `string` | 127.0.0.1 | Valid IPs | The IP to send the packets to. |
| `port` | `int` | 7777 | 0~65535 | The port to send the packets to. |
| `paddingFront` | `int` | 0 | 0~1000 | Blank bytes to append to the front of the packet. (Charles' output seemed to always append a single blank byte, so this is just to maintain compatibility) |
| `paddingBack` | `int` | 0 | 0~1000 | Blank bytes to append to the back of the packet. |
| `enable` | `bool` | true | | Whether to use this output.

- Can only output up to 21,835 RGB LEDs due to 65,535 byte packet size limit.

## Controllers
Not yet implemented.