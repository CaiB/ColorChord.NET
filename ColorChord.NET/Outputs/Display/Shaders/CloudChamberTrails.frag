#version 330 core

#define NOTE_QTY 12
#define RING_LOC 0.9

in vec2 TexCoord;
out vec4 FragColor;

uniform float Amplitudes[NOTE_QTY];
uniform float Locations[NOTE_QTY];
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
    // Information about this pixel
    //float Angle = (atan(-TexCoord.x, -TexCoord.y) + 3.1415926535) / 6.2831853071795864;
    //Angle = mod(Angle + Advance, 1.0);
    float Radius = distance(vec2(0.0), TexCoord);

    vec3 Colour = vec3(0.0);

    for (int i = 0; i < NOTE_QTY; i++)
    {
        vec2 LineStart = vec2(sin(Locations[i] * 6.2831853071795864), cos(Locations[i] * 6.2831853071795864)) * RING_LOC;
        vec2 LineEnd = -LineStart;
        vec2 LineDir = normalize(LineStart);
        float Distance = distance(vec2(0.0), LineDir * dot(TexCoord, LineDir));
        Colour += AngleToRGB(Locations[i], 1.0) * max(0.5 - sqrt(Distance), 0.0) * Amplitudes[i] * 2.5;
    }

    FragColor = vec4(Colour, 1.0);
}