# ColorChord.NET
My WIP port of [CNLohr's ColorChord2](https://github.com/cnlohr/colorchord) to C#.NET, allowing for more flexibility and extendability. Also uses new WASAPI Loopback methods for getting audio on Windows, which appears to work more reliably.

Somewhat different from Charles' version, I divided components into 4 classes, centered around the `NoteFinder`:
- Audio Sources: Pipes audio data from some location into the NoteFinder.
- Visualizers: Takes data from the NoteFinder, and turns it into a format that is outputtable via some method.
- Outputs: Takes data from a visualizer, and actually displays/outputs it somewhere.
- Controllers: Edits the behaviour of any of the above system components during runtime.

A single instance of the application supports a single audio source, any number of visualizers, each with any number of outputs. This allows for a single audio stream to be processed and displayed in any desired combination of ways.

Only some sources, visualizers, and outputs have been implemented.

The core math is done in a C library, `ColorChordLib` due to complexity.

The system performs very well, requiring negligible CPU and RAM with network outputs.
