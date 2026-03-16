static const float PI      = 3.1415926536;
static const float TAU     = 6.2831853072;
static const float HALF_PI = 1.5707963268;
static const float ROOT_2  = 1.4142135624;

static const float REC_PI      = 0.3183098862;
static const float REC_TAU     = 0.1591549431;
static const float REC_HALF_PI = 0.6366197724;
static const float REC_ROOT_2  = 0.7071067812;

float3 HSVToRGB(float3 c)
{
    float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

float3 AngleToRGB(float angle, float sat, float val)
{
    float Hue;
    Hue = (1.0 - step(4.0 / 12.0, angle)) * ((1.0 / 3.0) - angle) * 0.5; // Yellow -> Red
    Hue += (step(4.0 / 12.0, angle) - step(8.0 / 12.0, angle)) * (1 - (angle - (1.0 / 3.0))); // Red -> Blue
    Hue += step(8.0 / 12.0, angle) * ((2.0 / 3.0) - (1.5 * (angle - (2.0 / 3.0)))); // Blue -> Yellow
    return HSVToRGB(float3(Hue, sat, val));
}