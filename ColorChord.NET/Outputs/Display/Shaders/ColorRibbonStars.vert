#version 330 core

layout (location = 0) in vec3 InVertPosition;
layout (location = 1) in vec2 InTexCoord;
layout (location = 2) in vec3 InStarPosition;
layout (location = 3) in int InLEDIndex;

uniform mat4 Projection;
uniform mat4 View;
uniform mat4 Model;
uniform sampler2D Texture;
uniform float LEDCount;

out vec2 TexCoord;
out vec3 Colour;

void main()
{
	TexCoord = InTexCoord;
	Colour = texture(Texture, vec2(InLEDIndex / LEDCount, 0.5)).rgb;

	//vec3 TransformedPos = InStarPosition + (CameraRight * InVertPosition.x) + (CameraUp * InVertPosition.y);
	//gl_Position = vec4(TransformedPos, 1.0) * Projection;
	gl_Position = vec4(InVertPosition + InStarPosition, 1.0) * View * Projection; //  * Model 
}