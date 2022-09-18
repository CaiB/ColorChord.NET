using System;

namespace ColorChord.NET.API;

/// <summary>Used on visualizers, outputs, and controllers to specify that they should be instantiated in a separate thread by ColorChord.NET. Not usable for sources or NoteFinders.</summary>
/// <remarks>This is useful if the class itself is not thread-safe, but also blocks. Note that all method calls other than the constructor should still be made thread-safe.</remarks>
[AttributeUsage(AttributeTargets.Class)]
public class ThreadedInstanceAttribute : Attribute { }
