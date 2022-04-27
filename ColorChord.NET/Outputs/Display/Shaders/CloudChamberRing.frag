#version 330 core

#define NOTE_QTY 12
#define SQRT2PI 2.506628253
#define BASE_BRIGHT 1.0
#define INSIDE 0.6
#define OUTSIDE 0.9

in vec2 TexCoord;
out vec4 FragColor;

uniform vec2 Resolution;
uniform float Advance;

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
    float Angle = (atan(-TexCoord.x, -TexCoord.y) + 3.1415926535) / 6.2831853071795864;
    Angle = mod(Angle + Advance, 1.0);
    float Radius = distance(vec2(0.0), TexCoord);
    
    float OnePixelDist = 2.0 / Resolution.x;

    vec3 Colour = AngleToRGB(Angle, BASE_BRIGHT * (1 - smoothstep(0.98, 0.98 + OnePixelDist, Radius)));

    float RegionMult = 1.0 - smoothstep(OUTSIDE, OUTSIDE + OnePixelDist, Radius);
    Colour *= RegionMult;
    FragColor = vec4(Colour, smoothstep(INSIDE - OnePixelDist, INSIDE, Radius)); // Black outside, transparent inside
}