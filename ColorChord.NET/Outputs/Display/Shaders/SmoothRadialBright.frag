#version 330 core

#define BIN_QTY 12
#define SIGMA 0.6
#define SQRT2PI 2.506628253

out vec4 FragColor;

uniform float Amplitudes[BIN_QTY];
uniform float Means[BIN_QTY];
uniform vec2 Resolution;

vec3 HSVToRGB(vec3 c)
{
    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

vec3 AngleToRGB(float angle, float val)
{
    float Hue;
    Hue = (1.0 - step(4.0 / 12.0, angle)) * ((1.0 / 3.0) - angle) * 0.5; // Yellow -> Red
    Hue += (step(4.0 / 12.0, angle) - step(8.0 / 12.0, angle)) * (1 - (angle - (1.0 / 3.0))); // Red -> Blue
    Hue += step(8.0 / 12.0, angle) * ((2.0 / 3.0) - (1.5 * (angle - (2.0 / 3.0)))); // Blue -> Yellow
    return HSVToRGB(vec3(Hue, 1.0, val));
}

void main()
{
    vec2 Coords = ((gl_FragCoord.xy / Resolution) * 2.0) - vec2(1.0);
    float Angle = (atan(-Coords.x, -Coords.y) + 3.1415926535) / 6.2831853071795864;
    float Radius = distance(vec2(0.0), Coords);
    
    float Value = 0.0;

    for (int i = 0; i < BIN_QTY; i++)
    {
        float x = Means[i] - (Angle * BIN_QTY);
        x += BIN_QTY * (1 - step(BIN_QTY / -2.0, x));
        x -= BIN_QTY * step(BIN_QTY / 2.0, x);
        Value += (Amplitudes[i] * 1.0) / (SIGMA * SQRT2PI) * exp(-(x * x) / (2 * SIGMA * SIGMA));
    }

    float OnePixelDist = 2.0 / Resolution.x;

    //vec3 Colour = vec3(0.1);

    /*for(int i = 0; i < (NOTE_QTY - 1); i++)
    {
        Colour = mix(Colour, vec3(Colours[(i*3)], Colours[(i*3)+1], Colours[(i*3)+2]), step(Starts[i], Angle) - step(Starts[i+1], Angle));
    }

    
    float RegionMult = 1.0 - smoothstep(OUTSIDE, OUTSIDE + OnePixelDist, Radius);
    Colour *= RegionMult;
    FragColor = vec4(Colour, smoothstep(INSIDE - OnePixelDist, INSIDE, Radius)); // Black outside, transparent inside*/
    FragColor = vec4(AngleToRGB(Angle, (0.0 + (Value * 1.0)) * (1 - smoothstep(0.98, 0.98 + OnePixelDist, Radius))), 1.0);
}