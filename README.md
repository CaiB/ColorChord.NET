# ColorChord.NET

## Check out the [ColorChord.NET Website](https://www.colorchord.net) for documentation.

My port of [CNLohr's ColorChord 2](https://github.com/cnlohr/colorchord) to C#.NET, allowing for more flexibility and extendability. Also includes significantly more documentation.

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

# Development
I work in Visual Studio 2019, and auto-builds are done by AppVeyor.  
To prevent a commit from triggering an auto-build and release, prepend the commit message with `[NAB]`.

**Random other notes for myself:**

Compiling .so from C source on Linux:  
`gcc [Input File].c -shared -fpic -o [Output File].so`