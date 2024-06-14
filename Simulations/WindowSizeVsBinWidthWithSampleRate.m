% This script is used to check how wide the first-order response curve is at different window sizes, and different sample rates
% Of interest is developing a formula to determine the necessary window size at a given sample rate to give a certain desirable bin size

% Code surrounded in %%% is specific to showing a full-colour response display to visually compare the entire spectrum of response curves

BinCenter = 440;
MaxWindowSize = 6144;
PlotPointCount = 600;
ZeroCutoff = 0.05; % This is multiplied by the maximum value to set a cutoff for where a zero is

SampleRates = [8000 12000 16000 24000 32000 44100 48000 72000 96000 192000];
WindowSizes = 1:MaxWindowSize;

PlotRange = BinCenter;
InputFrequencies = (BinCenter - PlotRange):((PlotRange * 2) / PlotPointCount):(BinCenter + PlotRange);

%%% 
AllMagnitudes = zeros(PlotPointCount + 1, MaxWindowSize, length(SampleRates));
AllMagnitudesNC = zeros(PlotPointCount + 1, MaxWindowSize, length(SampleRates));
%%%

UpperZero = zeros(2, MaxWindowSize, length(SampleRates));
LowerZero = zeros(2, MaxWindowSize, length(SampleRates));
ResponseWidths = zeros(length(SampleRates), MaxWindowSize);

RandInput = rand(MaxWindowSize, 1);

for SampleRateIndex = 1:length(SampleRates)
    SampleRate = SampleRates(SampleRateIndex);
    t = 0:(1 / SampleRate):((MaxWindowSize - 1) / SampleRate);
    Sin = sin(t .* (2 * pi * BinCenter));
    Cos = cos(t .* (2 * pi * BinCenter));

    for WindowSizeIndex = 1:length(WindowSizes)
        WindowSize = WindowSizes(WindowSizeIndex);
        NCOffset = SampleRate / WindowSize;
        NCBinCenterL = BinCenter - (NCOffset / 2);
        NCBinCenterR = BinCenter + (NCOffset / 2);

        NCSinL = sin(t(1:WindowSize) .* (2 * pi * NCBinCenterL));
        NCCosL = cos(t(1:WindowSize) .* (2 * pi * NCBinCenterL));
        NCSinR = sin(t(1:WindowSize) .* (2 * pi * NCBinCenterR));
        NCCosR = cos(t(1:WindowSize) .* (2 * pi * NCBinCenterR));

        Magnitudes = zeros(1, PlotPointCount + 1);
        NCMagnitudes = zeros(1, PlotPointCount + 1);

        % Use this if you want only random noise to be input
        %InputSin = RandInput(1:WindowSize)';
        for FreqIndex = 1:(PlotPointCount + 1)
            Freq = InputFrequencies(FreqIndex);
            % Use this if you want random noise plus a faint signal to be input
            %InputSin = RandInput(1:WindowSize)'; % + (0.05 .* sin(t(1:WindowSize) .* (2 * pi * Freq)));
            % Use this if you just want the signal to be input
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
        AllMagnitudes(:, WindowSizeIndex, SampleRateIndex) = Magnitudes;
        AllMagnitudesNC(:, WindowSizeIndex, SampleRateIndex) = NCMagnitudes;
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
                    LowerZero(1, WindowSizeIndex, SampleRateIndex) = ThisFreq;
                end
            else % Right of the peak, only save first
                if Magnitudes(FreqIndex) < CutoffVal && UpperZero(1, WindowSizeIndex, SampleRateIndex) == 0
                    UpperZero(1, WindowSizeIndex, SampleRateIndex) = ThisFreq;
                end
            end

            % NC
            if FreqIndex < PeakNCIndex % Left of peak
                if NCMagnitudes(FreqIndex) < CutoffValNC
                    LowerZero(2, WindowSizeIndex, SampleRateIndex) = ThisFreq;
                end
            else % Right of peak
                if NCMagnitudes(FreqIndex) < CutoffValNC && UpperZero(2, WindowSizeIndex, SampleRateIndex) == 0
                    UpperZero(2, WindowSizeIndex, SampleRateIndex) = ThisFreq;
                end
            end
        end

        ResponseWidths(SampleRateIndex, WindowSizeIndex) = (UpperZero(2, WindowSizeIndex, SampleRateIndex) - BinCenter) + (BinCenter - LowerZero(2, WindowSizeIndex, SampleRateIndex));

        if (mod(WindowSizeIndex, 1000) == 0)
            disp(sprintf("  Up to window size %i", WindowSize));
        end
    end
    disp(sprintf("Done sample rate %i (%i/%i)", SampleRate, SampleRateIndex, length(SampleRates)));
end

%% Curve Fitting
FitCoeffs = zeros(length(SampleRates), 2);
for SampleRateIndex = 1:length(SampleRates)
    SampleRate = SampleRates(SampleRateIndex);

    [FitX, FitY] = prepareCurveData(WindowSizes, ResponseWidths(SampleRateIndex, :));
    FitOpts = fitoptions('Method', 'NonlinearLeastSquares');
    FitOpts.Exclude = (FitY < 0.5) | (FitY > 400);
    FitOpts.Lower = [0 0];
    [Fitted, FitQuality] = fit(FitX, FitY, fittype('rat01'), FitOpts);
    FitCoeffs(SampleRateIndex, :) = coeffvalues(Fitted);
    if(FitQuality.rsquare < 0.98)
        fprintf("The curve fitting at sample rate %i (index %i) got a poor result, and the results cannot be trusted. (R^2=%.4f)\n", SampleRate, SampleRateIndex, FitQuality.rsquare);
    end
    % Curves are in form: BinWidth = (p1) / (WindowSize + q1)
end

%% Equation Extraction (just more curve fitting but with a fancier name)
% Equations here are: Coeff = p1 * SampleRate + p2
[P1Fit, P1FitQuality] = fit(SampleRates', FitCoeffs(:,1), 'poly1');
if(P1FitQuality.rsquare < 0.98)
    fprintf("The secondary curve fitting for p1 got a poor result, and the results cannot be trusted. (R^2=%.4f)\n", P1FitQuality.rsquare);
end

[Q1Fit, Q1FitQuality] = fit(SampleRates', FitCoeffs(:,2), 'poly1');
if(Q1FitQuality.rsquare < 0.98)
    fprintf("The secondary curve fitting for p1 got a poor result, and the results cannot be trusted. (R^2=%.4f)\n", Q1FitQuality.rsquare);
end

fprintf("Got the following bin width/window size relation equations from regressed data:\n");
fprintf("BinWidth = ((sampleRate * %.8GF) + %.8GF) / (windowSize + ((sampleRate * %.8GF) + %.8GF))\n", P1Fit.p1, P1Fit.p2, Q1Fit.p1, Q1Fit.p2);
fprintf("WindowSize = (((sampleRate * %.8GF) + %.8GF) / binWidth) - ((sampleRate * %.8GF) + %.8GF)\n", P1Fit.p1, P1Fit.p2, Q1Fit.p1, Q1Fit.p2);

%% Plots
SRToPlot = 8;
figure(1);
plot(WindowSizes, LowerZero(1, :, SRToPlot), "-r", WindowSizes, UpperZero(1, :, SRToPlot), "-r");
hold on;
plot(WindowSizes, LowerZero(2, :, SRToPlot), "-b", WindowSizes, UpperZero(2, :, SRToPlot), "-b");
plot([1, MaxWindowSize], [BinCenter, BinCenter], ":k")
hold off;
xlim([1, MaxWindowSize]);
ylim([0, BinCenter + PlotRange]);
%xline(BinCenter);
title("Bin Peak Width Test");
xlabel("Window Size, samples");
ylabel("Frequency, Hz");
legend("ColorChord Lower", "ColorChord Upper", "NC Lower", "NC Upper"); %, "FFT", "NC FFT"
grid on;

%%%
XAxisRange = [min(WindowSizes), max(WindowSizes)];
YAxisRange = [BinCenter + PlotRange, BinCenter - PlotRange];
figure(2);
grid off;
subplot(2, 1, 1);
imagesc(XAxisRange, YAxisRange, max(0, log(AllMagnitudes(:, :, SRToPlot))));
axis xy;
colormap jet;
title("ColorChord DFT Bin");
xlabel("Window Size, samples");
xlim([1, MaxWindowSize]);
ylim([BinCenter - PlotRange, BinCenter + PlotRange]);
ylabel("Frequency (Hz)");

subplot(2, 1, 2);
imagesc(XAxisRange, YAxisRange, max(0, log(AllMagnitudesNC(:, :, SRToPlot))));
axis xy;
colormap jet;
title("ColorChord DFT Bin with NC");
xlabel("Window Size, samples");
xlim([1, MaxWindowSize]);
ylabel("Frequency (Hz)");
%%%

figure(3);
subplot(2, 1, 1);
title("NC Bin Width Regression Coefficients");
plot(SampleRates, FitCoeffs(:,1));
xlabel("Sample Rate, Hz");
ylabel("p1 Coefficient");
grid on;

subplot(2, 1, 2);
plot(SampleRates, FitCoeffs(:,2));
xlabel("Sample Rate, Hz");
ylabel("q1 Coefficient");
grid on;

hold off;