#version 330 core

out vec4 FragColor;

uniform float Colours[36];
uniform float Starts[12];
uniform vec2 Resolution;
uniform float Advance;

void main()
{
    vec2 Coords = ((gl_FragCoord.xy / Resolution) * 2.0) - vec2(1.0);
    float Angle = (atan(-Coords.x, -Coords.y) + 3.1415926535) / 6.2831853071795864;
    Angle = mod(Angle + Advance, 1.0);
    float Radius = distance(vec2(0.0), Coords);
    
    vec3 Colour = vec3(0.1);

    for(int i = 0; i < 11; i++)
    {
        Colour = mix(Colour, vec3(Colours[(i*3)], Colours[(i*3)+1], Colours[(i*3)+2]), step(Starts[i], Angle) - step(Starts[i+1], Angle));
    }
    //Colour = mix(Colour, vec3(Colours[0], Colours[1], Colours[2]), step(Starts[0], Angle) - step(Starts[1], Angle));
    //Colour = mix(Colour, vec3(Colours[3], Colours[4], Colours[5]), step(Starts[1], Angle) - step(Starts[2], Angle));

    Colour *= smoothstep(0.6, 0.60001, Radius) - smoothstep(0.9, 0.90001, Radius); // TODO actually smooth this by 1 pixel based on resolution
    FragColor = vec4(Colour, 1.0);
}