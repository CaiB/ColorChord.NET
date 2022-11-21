% This script is used to check how NC peak width responds to different sample rates.


SampleRates = [8000 12000 16000 24000 32000 44100 48000 72000 96000 192000];
BinCenter = 440;
PlotPointCount = 5000;
BaseWindowSize = 1024;

PeakWidths = zeros(1, length(SampleRates));

figure(1);
hold on;

for SRIndex = 1:length(SampleRates)
    SampleRate = SampleRates(SRIndex);

    % This scales the window size to match the increase in sample rate, keeping a constant time period of input signal.
    WindowSize = ceil(BaseWindowSize / SampleRates(1) * SampleRate);
    
    t = 0:(1 / SampleRate):(WindowSize / SampleRate);
    PlotRange = BinCenter;
    FreqStepSize = ((PlotRange * 2) / PlotPointCount);
    InputFrequencies = (BinCenter - PlotRange):FreqStepSize:(BinCenter + PlotRange);

    NCOffset = SampleRate / WindowSize;
    NCBinCenterL = BinCenter - (NCOffset / 2);
    NCBinCenterR = BinCenter + (NCOffset / 2);

    NCSinL = sin(t(1:WindowSize) .* (2 * pi * NCBinCenterL));
    NCCosL = cos(t(1:WindowSize) .* (2 * pi * NCBinCenterL));
    NCSinR = sin(t(1:WindowSize) .* (2 * pi * NCBinCenterR));
    NCCosR = cos(t(1:WindowSize) .* (2 * pi * NCBinCenterR));

    NCMagnitudes = zeros(1, PlotPointCount + 1);

    for FreqIndex = 1:(PlotPointCount + 1)
        Freq = InputFrequencies(FreqIndex);
        InputSin = sin(t(1:WindowSize) .* (2 * pi * Freq));
    
        NCSinProductsL = sum(InputSin .* NCSinL);
        NCCosProductsL = sum(InputSin .* NCCosL);
        NCSinProductsR = sum(InputSin .* NCSinR);
        NCCosProductsR = sum(InputSin .* NCCosR);
        NCMag = (NCSinProductsL * NCSinProductsR) + (NCCosProductsL * NCCosProductsR);
        NCMag = max(0, -NCMag);
        NCMagnitudes(FreqIndex) = sqrt(NCMag);
    end

    plot(InputFrequencies, NCMagnitudes);

    ZeroIndex = find(NCMagnitudes(floor(length(NCMagnitudes) / 2):end) == 0, 1, 'first');
    PeakWidths(SRIndex) = ZeroIndex * 2 * FreqStepSize;
end

hold off;
xlim([BinCenter - PeakWidths(end), BinCenter + PeakWidths(end)]);
title("Bin Peak Width Test Across Sample Rates");
xlabel("Frequency, Hz");
ylabel("Response Strength");
legend(num2str(SampleRates.'));
grid on;

%% Summary Plot
figure(2);
plot(SampleRates, PeakWidths, "-r");
xlim([SampleRates(1), SampleRates(end)]);
%ylim([0, BinCenter + PlotRange]);
title("Bin Peak Width vs Sample Rate");
xlabel("Sample Rate, Hz");
ylabel("Peak Response Width, Hz");
% legend("ColorChord Lower", "ColorChord Upper", "NC Lower", "NC Upper"); %, "FFT", "NC FFT"
grid on;