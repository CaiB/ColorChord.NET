% This script is used to check how wide the first-order response curve is at different window sizes
% Of interest is developing a formula to determine the necessary window size at a given sample rate to give a certain desirable bin size

% Code surrounded in %%% is specific to showing a full-colour response display to visually compare the entire spectrum of response curves

SampleRate = 48000;
BinCenter = 440;
MaxWindowSize = 6144;
PlotPointCount = 1200;
ZeroCutoff = 0.05; % This is multiplied by the maximum value to set a cutoff for where a zero is

WindowSizes = 1:MaxWindowSize;
t = [0:(1 / SampleRate):((MaxWindowSize - 1) / SampleRate)];
Sin = sin(t .* (2 * pi * BinCenter));
Cos = cos(t .* (2 * pi * BinCenter));

PlotRange = BinCenter;
InputFrequencies = (BinCenter - PlotRange):((PlotRange * 2) / PlotPointCount):(BinCenter + PlotRange);

%%% 
AllMagnitudes = zeros(PlotPointCount + 1, MaxWindowSize);
AllMagnitudesNC = zeros(PlotPointCount + 1, MaxWindowSize);
%%%

UpperZero = zeros(2, MaxWindowSize);
LowerZero = zeros(2, MaxWindowSize);

for WindowSize = WindowSizes
    NCOffset = SampleRate / WindowSize;
    NCBinCenterL = BinCenter - (NCOffset / 2);
    NCBinCenterR = BinCenter + (NCOffset / 2);

    NCSinL = sin(t(1:WindowSize) .* (2 * pi * NCBinCenterL));
    NCCosL = cos(t(1:WindowSize) .* (2 * pi * NCBinCenterL));
    NCSinR = sin(t(1:WindowSize) .* (2 * pi * NCBinCenterR));
    NCCosR = cos(t(1:WindowSize) .* (2 * pi * NCBinCenterR));

    Magnitudes = zeros(1, PlotPointCount + 1);
    NCMagnitudes = zeros(1, PlotPointCount + 1);

    for FreqIndex = 1:(PlotPointCount + 1)
        Freq = InputFrequencies(FreqIndex);
        InputSin = sin(t(1:WindowSize) .* (2 * pi * Freq));
    
        SinProducts = sum(InputSin .* Sin(1:WindowSize));
        CosProducts = sum(InputSin .* Cos(1:WindowSize));
        Magnitudes(FreqIndex) = sqrt((SinProducts * SinProducts) + (CosProducts * CosProducts));
    
        NCSinProductsL = sum(InputSin .* NCSinL);
        NCCosProductsL = sum(InputSin .* NCCosL);
        NCSinProductsR = sum(InputSin .* NCSinR);
        NCCosProductsR = sum(InputSin .* NCCosR);
        NCMag = (NCSinProductsL * NCSinProductsR) + (NCCosProductsL * NCCosProductsR);
        NCMag = max(0, -NCMag);
        NCMagnitudes(FreqIndex) = sqrt(NCMag) * 1.7;
    end

    %%%
    AllMagnitudes(:,WindowSize) = Magnitudes;
    AllMagnitudesNC(:,WindowSize) = NCMagnitudes;
    %%%

    [PeakMag, PeakIndex] = max(Magnitudes);
    [PeakNCMag, PeakNCIndex] = max(NCMagnitudes);
    CutoffVal = PeakMag * ZeroCutoff;
    CutoffValNC = PeakNCMag * ZeroCutoff;

    for FreqIndex = 1:(PlotPointCount + 1)
        ThisFreq = InputFrequencies(FreqIndex);
        % Regular
        if FreqIndex < PeakIndex % We are left of the peak, overwrite with each
            if Magnitudes(FreqIndex) < CutoffVal
                LowerZero(1, WindowSize) = ThisFreq;
            end
        else % Right of the peak, only save first
            if Magnitudes(FreqIndex) < CutoffVal && UpperZero(1, WindowSize) == 0
                UpperZero(1, WindowSize) = ThisFreq;
            end
        end

        % NC
        if FreqIndex < PeakNCIndex % Left of peak
            if NCMagnitudes(FreqIndex) < CutoffValNC
                LowerZero(2, WindowSize) = ThisFreq;
            end
        else % Right of peak
            if NCMagnitudes(FreqIndex) < CutoffValNC && UpperZero(2, WindowSize) == 0
                UpperZero(2, WindowSize) = ThisFreq;
            end
        end
    end
end

hold on;
figure(1);
plot(WindowSizes, LowerZero(1,:), "-r", WindowSizes, UpperZero(1,:), "-r");
plot(WindowSizes, LowerZero(2,:), "-b", WindowSizes, UpperZero(2,:), "-b");
plot([1, MaxWindowSize], [BinCenter, BinCenter], ":k")
xlim([1, MaxWindowSize]);
ylim([0, BinCenter + PlotRange]);
%xline(BinCenter);
title("Bin Peak Width Test");
xlabel("Window Size, samples");
ylabel("Frequency, Hz");
legend("ColorChord Lower", "ColorChord Upper", "NC Lower", "NC Upper"); %, "FFT", "NC FFT"
grid on;

%%%
figure(2);
grid off;
subplot(2, 1, 1);
imagesc(max(0, log(AllMagnitudes)));
colormap jet;
title("ColorChord DFT Bin");
xlabel("Window Size, samples");
xlim([1, MaxWindowSize]);
ylabel("Frequency");

subplot(2, 1, 2);
imagesc(max(0, log(AllMagnitudesNC)));
colormap jet;
title("ColorChord DFT Bin with NC");
xlabel("Window Size, samples");
xlim([1, MaxWindowSize]);
ylabel("Frequency");
%%%

hold off;