# Latency Measurement Tools

In order to test the real-world latency of ColorChord.NET, I developed a simple hardware-based test:

ColorChord.NET is run on the computer, using a monitor-out display mode (OpenGL or D3D12). A wave file of a known frequency is generated (e.g. with `CreateBeepWAV.ps1`). A CdS light sensor is placed on the monitor over the area where ColorChord.NET will respond to this frequency. An oscilloscope captures both the audio output from the computer (to track the start of the sound), and the light sensor reading (to track the CC.NET response). The 2 signals are then compared to calculate the full system latency.

Because audio is being played by the computer itself, any latency added by WASAPI, the audio driver, and the audio hardware will delay the onset of the sound output. By using the same test setup as before, but this time inputting audio to the computer from an external source, and taking enough measurements, we can average the full system latency values of both experiments, and deduce roughly how much latency is added by WASAPI/driver/hardware, by dividing this difference by 2. This assumes the latency values are roughly equal for the out vs in path.

Because several steps along the way use independent buffers of varying sizes, the measured overall system latency varies significantly run-to-run. This is expected. By taking many samples and using the min, mean, and max values, as well as querying the size of buffers wherever possible, full-chain latency can be broken down granuarly. The goal of the scripts in this directory are to automate the process of taking many measurements (e.g. thousands), making this process easy and reproducible.

Notes:
- Ensure the audio output and input interfaces are operating at the same sample rate, and are physically connected to the same audio hardware.
- If using D3D12, ensure the swapchain is operating in "Hardware Composed: Independent Flip" to get lowest latency. This can be checked with PresentMon.
  - I found that with my desktop PC (AMD GPU), this seems to only be possible when a single monitor is connected. Further investigation is needed.