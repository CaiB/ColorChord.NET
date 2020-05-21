#version 330 core

#define NOTE_QTY 12
#define INSIDE 0.6
#define OUTSIDE 0.9

out vec4 FragColor;

uniform float Colours[NOTE_QTY * 3];
uniform float Starts[NOTE_QTY];
uniform vec2 Resolution;
uniform float Advance;

void main()
{
    vec2 Coords = ((gl_FragCoord.xy / Resolution) * 2.0) - vec2(1.0);
    float Angle = (atan(-Coords.x, -Coords.y) + 3.1415926535) / 6.2831853071795864;
    Angle = mod(Angle + Advance, 1.0);
    float Radius = distance(vec2(0.0), Coords);
    
    vec3 Colour = vec3(0.1);

    for(int i = 0; i < (NOTE_QTY - 1); i++)
    {
        Colour = mix(Colour, vec3(Colours[(i*3)], Colours[(i*3)+1], Colours[(i*3)+2]), step(Starts[i], Angle) - step(Starts[i+1], Angle));
    }

    float OnePixelDist = 2.0 / Resolution.x;
    float RegionMult = 1.0 - smoothstep(OUTSIDE, OUTSIDE + OnePixelDist, Radius);
    Colour *= RegionMult;
    FragColor = vec4(Colour, smoothstep(INSIDE - OnePixelDist, INSIDE, Radius)); // Black outside, transparent inside
}