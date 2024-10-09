[![ColorChord.NET](https://www.colorchord.net/ccnettext.gif)](https://www.colorchord.net/)

**:warning: Photosensitivity Warning: This is a music visualizer tool, so the documentation, demos, and program have bright, coloured, potentially fast flashing lights.**

## See the [ColorChord.NET Website](https://www.colorchord.net) for documentation/install instructions.

- **[Click here for downloads](https://github.com/CaiB/ColorChord.NET/releases)**
- **[Information about the standalone Gen2DFT library usable from other software](https://github.com/CaiB/ColorChord.NET/tree/master/Gen2DFTLib/readme.md)**

### Info

My port and enhancement of [cnlohr's ColorChord 2](https://github.com/cnlohr/colorchord).

Uses [Vannatech/dorba's netcoreaudio](https://github.com/dorba/netcoreaudio) for WASAPI support.

Somewhat different from cnlohr's version, I divided components into 5 categories:
- Audio Sources: Pipes audio data from some location into the NoteFinder. (e.g. WASAPI Loopback)
- Note Finder: Turns raw audio data into note information.
- Visualizers: Takes note info from the NoteFinder, and turns it into a format that is outputtable via some method. (e.g. Linear)
- Outputs: Takes data from a visualizer, and actually displays/outputs it somewhere. (e.g. UDP packets)
- Controllers: Edits the behaviour of any of the above system components during runtime.

A single instance of the application supports a single audio source, any number of visualizers, each with its own set of (any number of) outputs. This allows for a single audio stream to be processed and displayed in almost any desired combination of ways.

Only some sources, visualizers, and outputs from the base version have been implemented, but some new additions are also available.

The system performs very well, requiring negligible CPU and RAM, especially if only network output is needed.

I try to maintain the same behaviour given the same inputs as cnlohr's version. If you notice an undocumented difference, please let me know.

# Development
I work in Visual Studio 2022, and auto-builds are done by AppVeyor.  
To prevent a commit from triggering an auto-build and release, prepend the commit message with `[NAB]`.

**Random other notes for myself:**

Compiling .so from C source on Linux:  
`gcc [Input File].c -shared -fpic -o [Output File].so`
