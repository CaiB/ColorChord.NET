% This script is used to check exactly where (in frequency) the peak response of a bin is, using various DFT methods.
% Of special interest is the NC method, which actually uses 2 bins of different frequencies in its calculation.

% The output is a plot of frequency +/- the bin frequency (X) vs bin response strength (Y).
% This is calculated by sweeping input frequency around the bin frequency.

SampleRate = 48000;
WindowSize = 8192;
BinCenter = 440;

NCOffset = SampleRate / WindowSize;
NCBinCenterL = BinCenter - (NCOffset / 2);
NCBinCenterR = BinCenter + (NCOffset / 2);

PlotRange = 30; % Frequency +/- PlotRange, so actual range is 2x this
PlotPointCount = 1000;
InputFrequencies = (BinCenter - PlotRange):((PlotRange * 2) / PlotPointCount):(BinCenter + PlotRange);

t = [0:(1 / SampleRate):((WindowSize - 1) / SampleRate)];
Sin = sin(t .* (2 * pi * BinCenter));
Cos = cos(t .* (2 * pi * BinCenter));
NCSinL = sin(t .* (2 * pi * NCBinCenterL));
NCCosL = cos(t .* (2 * pi * NCBinCenterL));
NCSinR = sin(t .* (2 * pi * NCBinCenterR));
NCCosR = cos(t .* (2 * pi * NCBinCenterR));

Magnitudes = zeros(1, PlotPointCount);
NCMagnitudes = zeros(1, PlotPointCount);

for FreqIndex = 1:(PlotPointCount + 1)
    Freq = InputFrequencies(FreqIndex);
    InputSin = sin(t .* (2 * pi * Freq));

    SinProducts = sum(InputSin .* Sin);
    CosProducts = sum(InputSin .* Cos);
    Magnitudes(FreqIndex) = sqrt((SinProducts * SinProducts) + (CosProducts * CosProducts));

    NCSinProductsL = sum(InputSin .* NCSinL);
    NCCosProductsL = sum(InputSin .* NCCosL);
    NCSinProductsR = sum(InputSin .* NCSinR);
    NCCosProductsR = sum(InputSin .* NCCosR);
    NCMag = (NCSinProductsL * NCSinProductsR) + (NCCosProductsL * NCCosProductsR);
    NCMag = max(0, -NCMag);
    NCMagnitudes(FreqIndex) = sqrt(NCMag) * 1.7;
end

[PeakMag, PeakIndex] = max(Magnitudes);
[PeakNCMag, PeakNCIndex] = max(NCMagnitudes);

disp(sprintf('Normal Method peak was at ... %.2f [%.2f] %.2f ...', InputFrequencies(PeakIndex - 1), InputFrequencies(PeakIndex), InputFrequencies(PeakIndex + 1)));
disp(sprintf('    NC Method peak was at ... %.2f [%.2f] %.2f ...', InputFrequencies(PeakNCIndex - 1), InputFrequencies(PeakNCIndex), InputFrequencies(PeakNCIndex + 1)));

hold on;
figure(1);
plot(InputFrequencies, Magnitudes);
plot(InputFrequencies, NCMagnitudes);
xlim([BinCenter - PlotRange, BinCenter + PlotRange]);
%xline(BinCenter);
title("Bin Peak Location Test");
xlabel("Frequency, Hz");
legend("ColorChord", "ColorChord + NC"); %, "FFT", "NC FFT"
grid on;
hold off;
