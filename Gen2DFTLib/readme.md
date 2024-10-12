## Gen2DFTLib
For ColorChord.NET, I developed a custom DFT algorithm ["Gen2DFT"](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/NoteFinder/Gen2NoteFinderDFT.cs) to replace the original ColorChord ["BaseDFT"](https://github.com/CaiB/ColorChord.NET/blob/master/ColorChord.NET/NoteFinder/BaseNoteFinderDFT.cs) one. My implementation provides cleaner output and has lower latency than a traditional DFT. It is able to accomplish this without requiring a window function, yet allows for a sliding window. It is also computationally efficient.

Other people have expressed interest in using the algorithm in their own projects, and as such I made it a standalone module that can be AOT compiled to a native DLL. This means that while it is written in C#, it is usable from any language that supports C-style DLL calls, and it also does not require .NET to be installed to use it.

I also co-authored a conference paper detailing the algorithm. If you use this algorithm in your research, we would highly appreciate a citation.
- Pre-publish version: [arXiv.org - Window Function-less DFT with Reduced Noise and Latency for Real-Time Music Analysis](https://arxiv.org/abs/2410.07982)
- TODO: Add more info once review by conference is complete

[`Gen2DFT.cs`](https://github.com/CaiB/ColorChord.NET/blob/master/Gen2DFTLib/Gen2DFT.cs) in this directory is responsible for translating the C# API into one that is usable from unmanaged code. All of the functions exported in the DLL are defined and documented in this file. Documentation is also exported to an XML file included with the DLL download for use by your IDE to provide inline documentation.

Pre-built Gen2DFTLib DLLs are available, simply download the `Gen2DFTLib-*.zip` file from the latest [release on GitHub](https://github.com/CaiB/ColorChord.NET/releases).

Generally, the intended usage is as follows (example code written in C, use the equivalent in your language):
```c
// May be compiled with:
// cl.exe Gen2DFTExample.c /FC /W3 /WX /Zi /link /opt:ref

// C clutter
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <assert.h>
#include <stdlib.h>
#include <malloc.h>
#define _USE_MATH_DEFINES
#include <math.h>

typedef INT32 (*Gen2DFT_Init_Func)(UINT32 octaveCount, UINT32 binsPerOctave, UINT32 sampleRate, FLOAT startFrequency, FLOAT loudnessCorrection);
typedef UINT32 (*Gen2DFT_GetBinCount_Func)(void);
typedef FLOAT* (*Gen2DFT_GetBinMagnitudes_Func)(void);
typedef void (*Gen2DFT_AddAudioData_Func)(INT16* newData, UINT32 count);
typedef void (*Gen2DFT_CalculateOutput_Func)(void);

void main(void)
{
    // Start by determining if the system you are executing on supports AVX2
    // Anything other than C/C++ should have a much more pleasant built-in way to handle this
    BOOL SupportsAVX2 = FALSE;
    INT32 CPUInfo[4];
    __cpuid(CPUInfo, 0);
    if (CPUInfo[0] >= 7)
    {
        __cpuid(CPUInfo, 7);
        SupportsAVX2 = (CPUInfo[1] >> 5) & 1;
    }

    // Load the DLL using whatever mechanism is appropriate for your language
    HMODULE Gen2DFTLibHandle = LoadLibraryW(SupportsAVX2 ? L".\\Gen2DFTLib_Win_x64_AVX2.dll" : L".\\Gen2DFTLib_Win_x64_Baseline.dll");
    assert(Gen2DFTLibHandle != NULL);

    // Load function pointers from the DLL
    Gen2DFT_Init_Func Gen2DFT_Init = (Gen2DFT_Init_Func)GetProcAddress(Gen2DFTLibHandle, "Gen2DFT_Init");
    assert(Gen2DFT_Init != NULL);
    Gen2DFT_GetBinCount_Func Gen2DFT_GetBinCount = (Gen2DFT_GetBinCount_Func)GetProcAddress(Gen2DFTLibHandle, "Gen2DFT_GetBinCount");
    assert(Gen2DFT_GetBinCount != NULL);
    Gen2DFT_GetBinMagnitudes_Func Gen2DFT_GetBinMagnitudes = (Gen2DFT_GetBinMagnitudes_Func)GetProcAddress(Gen2DFTLibHandle, "Gen2DFT_GetBinMagnitudes");
    assert(Gen2DFT_GetBinMagnitudes != NULL);
    Gen2DFT_AddAudioData_Func Gen2DFT_AddAudioData = (Gen2DFT_AddAudioData_Func)GetProcAddress(Gen2DFTLibHandle, "Gen2DFT_AddAudioData");
    assert(Gen2DFT_AddAudioData != NULL);
    Gen2DFT_CalculateOutput_Func Gen2DFT_CalculateOutput = (Gen2DFT_CalculateOutput_Func)GetProcAddress(Gen2DFTLibHandle, "Gen2DFT_CalculateOutput");
    assert(Gen2DFT_CalculateOutput != NULL);

    // Call the DLL's exported Gen2DFT_Init() function once
    const UINT32 OCTAVES = 3;
    const UINT32 BINS_PER_OCTAVE = 12;
    const UINT32 SAMPLE_RATE = 48000;
    INT32 InitResult = Gen2DFT_Init(OCTAVES, BINS_PER_OCTAVE, SAMPLE_RATE, 55.0, 0.0);
    assert(InitResult >= 0);

    // Get information about the output by calling other exported functions once
    UINT32 BinCount = Gen2DFT_GetBinCount();
    assert(BinCount == OCTAVES * BINS_PER_OCTAVE);
    FLOAT* OutputData = Gen2DFT_GetBinMagnitudes();

    // The rest of this code is expected to run in some sort of loop to process data
    // Generate and input sine wave to the algorithm as an example
    const INT32 INPUT_COUNT = 4800;
    INT16* InputData = malloc(sizeof(INT16) * INPUT_COUNT);
    for (INT32 i = 0; i < INPUT_COUNT; i++) { InputData[i] = (INT16)(sinf(155.56F * (FLOAT)M_PI * 2.0F * i / SAMPLE_RATE) * 32000.0F); }
    Gen2DFT_AddAudioData(InputData, INPUT_COUNT);

    // After you've added some audio, calculate and display the output data
    Gen2DFT_CalculateOutput();
    for (UINT32 o = 0; o < BinCount / BINS_PER_OCTAVE; o++)
    {
        for (UINT32 b = 0; b < BINS_PER_OCTAVE; b++) { printf("%.2f ", OutputData[(o * BINS_PER_OCTAVE) + b]); }
        printf("\n");
    }
}
```

The above example code outputs:
```
0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00
0.00 0.00 0.00 0.00 0.00 0.58 1.07 0.62 0.00 0.00 0.00 0.00
0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00
```