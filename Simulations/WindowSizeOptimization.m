
%% Find the window size that best fits the number of bins
BinCount = 48; % Bins per octave
Octave = [2880 5760];
OctaveSpan = Octave(end) - Octave(1);

% The 2 frequencies to try to optimize spacing for
ReferenceFreq = NoteFreqAt(Octave(1), BinCount, BinCount - 1);
NextFreq = NoteFreqAt(Octave(1), BinCount, BinCount);

PreferredWindowSize = WindowSizeFor(NextFreq - ReferenceFreq);


SampleRate = 48000;
PlotPointCount = 8000;

t = 0:(1 / SampleRate):((PreferredWindowSize - 1) / SampleRate);
Bins = 1:(BinCount + 1);
BinFreqs = NoteFreqAt(Octave(1), BinCount, Bins);
InputFrequencies = Octave(1):(OctaveSpan / PlotPointCount):Octave(end);

NCMagnitudes = zeros(BinCount, PlotPointCount);

for BinIndex = Bins
    BinCenter = BinFreqs(BinIndex);
    NCOffset = SampleRate / PreferredWindowSize;
    NCBinCenterL = BinCenter - (NCOffset / 2);
    NCBinCenterR = BinCenter + (NCOffset / 2);

    NCSinL = sin(t .* (2 * pi * NCBinCenterL));
    NCCosL = cos(t .* (2 * pi * NCBinCenterL));
    NCSinR = sin(t .* (2 * pi * NCBinCenterR));
    NCCosR = cos(t .* (2 * pi * NCBinCenterR));

    for SweepFreqIndex = 1:(PlotPointCount + 1)
        Freq = InputFrequencies(SweepFreqIndex);
        InputSin = sin(t .* (2 * pi * Freq));
    
        NCSinProductsL = sum(InputSin .* NCSinL);
        NCCosProductsL = sum(InputSin .* NCCosL);
        NCSinProductsR = sum(InputSin .* NCSinR);
        NCCosProductsR = sum(InputSin .* NCCosR);
        NCMag = (NCSinProductsL * NCSinProductsR) + (NCCosProductsL * NCCosProductsR);
        NCMag = max(0, -NCMag);
        NCMagnitudes(BinIndex, SweepFreqIndex) = sqrt(NCMag);
    end

    NCMagnitudes(BinIndex,:) = NCMagnitudes(BinIndex,:) / norm(NCMagnitudes(BinIndex,:), inf);
end

NCMagnitudesdB = 20 * log10(NCMagnitudes);

NCMagnitudesSum = sum(NCMagnitudes, 1);
NCMagnitudesSumdB = 20 * log10(NCMagnitudesSum);

%% Plot bin overlaps across an entire octave
MyColours = {'#C33', '#CC3', '#3C3', '#3CC', '#33C'};
figure(1);
close all;
hold on;

colororder(MyColours);
for BinIndex = Bins
    %area(InputFrequencies, NCMagnitudes(BinIndex,:));
end

colororder(MyColours);
plot(InputFrequencies, NCMagnitudesdB);

plot(InputFrequencies, NCMagnitudesSumdB, '-k');

xlim(Octave);
ylim([-30, 10]);
title("Bin Overlap Test");
xlabel("Frequency, Hz");
grid on;
hold off;


%% Curve Fitting Result
% This was done by using the curveFitter tool to find an appropriate function and coefficients.
% f(x) means Q(WindowSize)
function Width = BinWidthAt(windowSize)
    Width = 50222.5926786413 ./ (windowSize + 11.483904495504245);
end
% This is just the inverse of the above function
function PreferredSize = WindowSizeFor(binWidth)
    PreferredSize = (50222.5926786413 ./ binWidth) - 11.483904495504245;
end
% This function calculates the frequency of a specific bin inside an octave
function NoteFreq = NoteFreqAt(octaveStart, binsPerOctave, binIndex)
    NoteFreq = octaveStart .* pow2((binIndex - 1) / binsPerOctave);
end