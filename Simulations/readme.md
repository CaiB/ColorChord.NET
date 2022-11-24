# ColorChord.NET Simulations
These simulations are written for MATLAB to test various aspects of the ColorChord.NET system.

**ShinNoteFinder DFT**:
- **BinPeakLocation.m:** used to check exactly where (in frequency) the peak response of a bin is, comparing traditional ColorChord algorithm and NC-based one.
- **BinPeakWidth.m:** used to check how wide the first-order response curve is at different window sizes.
- **BinPeakWidthVsSampleRate.m:** used to check how NC peak width responds to different sample rates.
- **WindowSizeRegression.m:** used to find a mathematical approximation of the bin width at a given window size that the algorithm will use to optimize overlap.
- **WindowSizeOptimization.m:** used to check how well bin overlap works across an entire octave, and test out formulas for calculating the best window size for the best overall response curve.
- **WindowShapeComparison.m:** used to compare different window functions and their effects on both the traditional ColorChord DFT and the NC-enhanced one.