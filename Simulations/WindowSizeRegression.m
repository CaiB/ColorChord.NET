% This script determines a function to get the optimal window size, given the bin count.

SampleRate = 48000;
BinCenter = 440;
MaxWindowSize = 6144;
PlotPointCount = 15000;

WindowSizes = 1:MaxWindowSize;
t = 0:(1 / SampleRate):((MaxWindowSize - 1) / SampleRate);

PlotRange = BinCenter;
InputFrequencies = (BinCenter - PlotRange):((PlotRange * 2) / PlotPointCount):(BinCenter + PlotRange);
CenterFreqIndex = floor(PlotPointCount / 2);

ResponseWidths = zeros(1, MaxWindowSize);

for WindowSizeInd = 1:length(WindowSizes)
    WindowSize = WindowSizes(WindowSizeInd);
    NCOffset = SampleRate / WindowSize;
    NCBinCenterL = BinCenter - (NCOffset / 2);
    NCBinCenterR = BinCenter + (NCOffset / 2);

    NCSinL = sin(t(1:WindowSize) .* (2 * pi * NCBinCenterL));
    NCCosL = cos(t(1:WindowSize) .* (2 * pi * NCBinCenterL));
    NCSinR = sin(t(1:WindowSize) .* (2 * pi * NCBinCenterR));
    NCCosR = cos(t(1:WindowSize) .* (2 * pi * NCBinCenterR));

    % Sweep up from center until we hit the first 0
    UpperZero = 0;
    for FreqIndex = CenterFreqIndex : 1 : (PlotPointCount + 1)
        Freq = InputFrequencies(FreqIndex);
        InputSin = sin(t(1:WindowSize) .* (2 * pi * Freq));

        NCSinProductsL = sum(InputSin .* NCSinL);
        NCCosProductsL = sum(InputSin .* NCCosL);
        NCSinProductsR = sum(InputSin .* NCSinR);
        NCCosProductsR = sum(InputSin .* NCCosR);
        NCMag = (NCSinProductsL * NCSinProductsR) + (NCCosProductsL * NCCosProductsR);
        if NCMag >= 0
            UpperZero = Freq;
            break
        end
    end

    % Sweep down from center until we hit the first 0
    LowerZero = 0;
    for FreqIndex = CenterFreqIndex : -1 : 0
        Freq = InputFrequencies(FreqIndex);
        InputSin = sin(t(1:WindowSize) .* (2 * pi * Freq));

        NCSinProductsL = sum(InputSin .* NCSinL);
        NCCosProductsL = sum(InputSin .* NCCosL);
        NCSinProductsR = sum(InputSin .* NCSinR);
        NCCosProductsR = sum(InputSin .* NCCosR);
        NCMag = (NCSinProductsL * NCSinProductsR) + (NCCosProductsL * NCCosProductsR);
        if NCMag >= 0
            LowerZero = Freq;
            break
        end
    end

    % Find the response width at this window size
    ResponseWidths(WindowSizeInd) = (UpperZero - BinCenter) + (BinCenter - LowerZero);
end

LastBadIndex = 0;
for WidthInd = 1:length(ResponseWidths)
    if ResponseWidths(WidthInd) > 800 || ResponseWidths(WidthInd) <= 0
        LastBadIndex = WidthInd;
    end
end

GoodWidowSizes = WindowSizes(LastBadIndex + 1:end);
GoodResponseWidths = ResponseWidths(LastBadIndex + 1:end);

FittedBinWidths = BinWidthAt(WindowSizes);

%% Plot of Actual Data and Modelled Function
figure(1);
plot(GoodWidowSizes, GoodResponseWidths, "-r");
hold on;
plot(WindowSizes, FittedBinWidths, "-b");
hold off;
xlim([0, MaxWindowSize]);
ylim([0, BinCenter + PlotRange]);
%xline(BinCenter);
title("Bin Response Width Curve");
xlabel("Window Size, samples");
ylabel("Bin Response Width, Hz");
legend("Actual Bin Response", "Modelled Bin Response");
grid on;

%% Curve Fitting Result
% This was done by using the curveFitter tool to find an appropriate function and coefficients.
% f(x) means Q(WindowSize)
function Width = BinWidthAt(windowSize)
    Width = 50222.5926786413 ./ (windowSize + 11.483904495504245);
end