#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTextureLoc;
layout (location = 2) in float aSegmentSide;
layout (location = 3) in vec3 aNormal;

out vec4 vertexColour;

uniform sampler2D tex;
uniform mat4 projection;

// All components are in the range [0…1], including hue.
vec3 rgb2hsv(vec3 c)
{
    vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));

    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

void main()
{
    ivec2 TexSize = textureSize(tex, 0);
    float TexY = mod(1.0 - (aTextureLoc.y - 0.0), 1.0);
    vec4 FromTex = texture(tex, vec2(aTextureLoc.x, TexY));
    vec4 FromTexLast = texture(tex, vec2(aTextureLoc.x, mod(TexY - (1.0 / TexSize.y), 1.0)));

    vec3 HSVFirst = rgb2hsv(FromTex.rgb);
    vec3 HSVLast = rgb2hsv(FromTexLast.rgb);
    float HueDifference = abs(max(HSVFirst.x - HSVLast.x, HSVLast.x - HSVFirst.x));

    vec4 TexOut = FromTex * (1.0 - step(0.5, aSegmentSide)) + FromTexLast * step(0.5, aSegmentSide);
    float SmoothingCutoff = 0.07;
    vertexColour = TexOut * (1.0 - step(SmoothingCutoff, HueDifference)) + FromTex * step(SmoothingCutoff, HueDifference);

    float AlphaVal = FromTex.a * (1.0 - step(0.5, aSegmentSide)) + FromTexLast.a * step(0.5, aSegmentSide);
    vec3 HeightOffset = (aNormal * AlphaVal) / 10;
    gl_Position = vec4(aPosition + HeightOffset, 1.0) * projection;
}
// remove this comment and it breaks on linux, WTF? - nikky
