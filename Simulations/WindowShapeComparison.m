% This script is used to compare various window shapes and their effects on both the traditional ColorChord algorithm, as well as the NC-based one.

% The output is a plot of bin frequencies (X) vs strength of response to an input (Y).

SampleRate = 48000;
WindowSize = 4096;
SignalFreq = 440;

PlotRange = 100; % Frequency +/- PlotRange, so actual range is 2x this
PlotPointCount = 1000;
BinFrequencies = (SignalFreq - PlotRange):((PlotRange * 2) / PlotPointCount):(SignalFreq + PlotRange);
NCOffset = SampleRate / WindowSize;
t = [0:(1 / SampleRate):((WindowSize - 1) / SampleRate)];

Magnitudes = zeros(3, PlotPointCount);
NCMagnitudes = zeros(3, PlotPointCount);

SegmentedRectWindow = ones(1, WindowSize);
WindowSizeQuart = WindowSize / 4;
WindowSizeHalf = WindowSize / 2;
WindowSizeThrQt = WindowSize * 3 / 4;
SegmentedRectWindow(1, 1 : WindowSizeQuart) = (ones(1, WindowSizeQuart) * 0.25);
SegmentedRectWindow(1, WindowSizeQuart + 1 : WindowSizeHalf) = (ones(1, WindowSizeQuart) * 0.50);
SegmentedRectWindow(1, WindowSizeHalf + 1 : WindowSizeThrQt) = (ones(1, WindowSizeQuart) * 0.75);

RegularInputSignal = sin(t .* (2 * pi * SignalFreq));
TriangularInputSignal = RegularInputSignal .* (0 : (1 / (WindowSize - 1)) : 1) * 2;
SegmentedRectInputSignal = RegularInputSignal .* SegmentedRectWindow * 2;

for FreqIndex = 1:(PlotPointCount + 1)
    BinCenter = BinFrequencies(FreqIndex);
    NCBinCenterL = BinCenter - (NCOffset / 2);
    NCBinCenterR = BinCenter + (NCOffset / 2);

    Sin = sin(t .* (2 * pi * BinCenter));
    Cos = cos(t .* (2 * pi * BinCenter));
    NCSinL = sin(t .* (2 * pi * NCBinCenterL));
    NCCosL = cos(t .* (2 * pi * NCBinCenterL));
    NCSinR = sin(t .* (2 * pi * NCBinCenterR));
    NCCosR = cos(t .* (2 * pi * NCBinCenterR));

    SinProducts = sum(RegularInputSignal .* Sin);
    CosProducts = sum(RegularInputSignal .* Cos);
    Magnitudes(1, FreqIndex) = sqrt((SinProducts * SinProducts) + (CosProducts * CosProducts));

    SinProducts = sum(TriangularInputSignal .* Sin);
    CosProducts = sum(TriangularInputSignal .* Cos);
    Magnitudes(2, FreqIndex) = sqrt((SinProducts * SinProducts) + (CosProducts * CosProducts));

    SinProducts = sum(SegmentedRectInputSignal .* Sin);
    CosProducts = sum(SegmentedRectInputSignal .* Cos);
    Magnitudes(3, FreqIndex) = sqrt((SinProducts * SinProducts) + (CosProducts * CosProducts));

    NCSinProductsL = sum(RegularInputSignal .* NCSinL);
    NCCosProductsL = sum(RegularInputSignal .* NCCosL);
    NCSinProductsR = sum(RegularInputSignal .* NCSinR);
    NCCosProductsR = sum(RegularInputSignal .* NCCosR);
    NCMag = (NCSinProductsL * NCSinProductsR) + (NCCosProductsL * NCCosProductsR);
    NCMag = max(0, -NCMag);
    NCMagnitudes(1, FreqIndex) = sqrt(NCMag) * 1.6;

    NCSinProductsL = sum(TriangularInputSignal .* NCSinL);
    NCCosProductsL = sum(TriangularInputSignal .* NCCosL);
    NCSinProductsR = sum(TriangularInputSignal .* NCSinR);
    NCCosProductsR = sum(TriangularInputSignal .* NCCosR);
    NCMag = (NCSinProductsL * NCSinProductsR) + (NCCosProductsL * NCCosProductsR);
    NCMag = max(0, -NCMag);
    NCMagnitudes(2, FreqIndex) = sqrt(NCMag) * 1.6;

    NCSinProductsL = sum(SegmentedRectInputSignal .* NCSinL);
    NCCosProductsL = sum(SegmentedRectInputSignal .* NCCosL);
    NCSinProductsR = sum(SegmentedRectInputSignal .* NCSinR);
    NCCosProductsR = sum(SegmentedRectInputSignal .* NCCosR);
    NCMag = (NCSinProductsL * NCSinProductsR) + (NCCosProductsL * NCCosProductsR);
    NCMag = max(0, -NCMag);
    NCMagnitudes(3, FreqIndex) = sqrt(NCMag) * 1.6;
end

MyColours = {'#C33', '#C73', '#CC3', '#3C7', '#37C', '#33C'};

close all;
hold on;
figure(1);
colororder(MyColours);
plot(BinFrequencies, Magnitudes(1, :), 'LineWidth', 2.0);
plot(BinFrequencies, Magnitudes(2, :), 'LineWidth', 2.0);
plot(BinFrequencies, Magnitudes(3, :), 'LineWidth', 2.0);
plot(BinFrequencies, NCMagnitudes(1, :), 'LineWidth', 2.0);
plot(BinFrequencies, NCMagnitudes(2, :), 'LineWidth', 2.0);
plot(BinFrequencies, NCMagnitudes(3, :), 'LineWidth', 2.0);
xlim([max(SignalFreq - PlotRange, 0), SignalFreq + PlotRange]);
xline(SignalFreq);
title("Bin Response Strength to Single Input Signal with Various Window Shapes");
xlabel("Bin Frequency, Hz");
legend("ColorChord Rectangular", "ColorChord Triangle", "ColorChord Segmented Rect", "NC Rectangular", "NC Triangular", "NC Segmented Rect");
grid on;
hold off;
