#version 330 core

uniform sampler2D gPosition;
uniform sampler2D gNormal;
uniform sampler2D texNoise;
uniform vec3[64] samples;
uniform vec2 screenSize;
uniform sampler2D revealage;

in vec2 texcoord;
out vec4 outOcclusion;

#if SSAOLEVEL == 2
int kernelSize = 24;
float radius = 0.9;
#else
int kernelSize = 20;
float radius = 0.9;
#endif

float bias = 0.01;


uniform mat4 projection;

vec3 safeNormalize(vec3 value, vec3 fallback) {
    float len2 = dot(value, value);
    return len2 > 0.000001 ? value * inversesqrt(len2) : fallback;
}

#if SSDO > 0
const vec2 check_offsets[25] = vec2[25](vec2(-0.4894566f, -0.3586783f),
vec2(-0.1717194f, 0.6272162f),
vec2(-0.4709477f, -0.01774091f),
vec2(-0.9910634f, 0.03831699f),
vec2(-0.2101292f, 0.2034733f),
vec2(-0.7889516f, -0.5671548f),
vec2(-0.1037751f, -0.1583221f),
vec2(-0.5728408f, 0.3416965f),
vec2(-0.1863332f, 0.5697952f),
vec2(0.3561834f, 0.007138769f),
vec2(0.2868255f, -0.5463203f),
vec2(-0.4640967f, -0.8804076f),
vec2(0.1969438f, 0.6236954f),
vec2(0.6999109f, 0.6357007f),
vec2(-0.3462536f, 0.8966291f),
vec2(0.172607f, 0.2832828f),
vec2(0.4149241f, 0.8816f),
vec2(0.136898f, -0.9716249f),
vec2(-0.6272043f, 0.6721309f),
vec2(-0.8974028f, 0.4271871f),
vec2(0.5551881f, 0.324069f),
vec2(0.9487136f, 0.2605085f),
vec2(0.7140148f, -0.312601f),
vec2(0.0440252f, 0.9363738f),
vec2(0.620311f, -0.6673451f)
);

float calcSSDO(vec3 fragpos, vec3 normal) {
    float finalAO = 0.0;

    const float attenuationAngleThreshold = 0.1;
    const int numSamples = 16;
    const float aoWeight = 0.7;
    float noise = fract(dot(gl_FragCoord.xy, vec2(0.5, 0.25)));
    float aspectRatio = screenSize.x / screenSize.y;
    float radius = 0.15 / max(abs(fragpos.z), 0.1);

    for (int i = 0; i < numSamples; ++i) {
        vec2 texOffset = pow(length(check_offsets[i].xy), 0.5) * radius * vec2(1.0, aspectRatio) * normalize(check_offsets[i].xy);
        vec2 newTC = clamp(texcoord + texOffset * noise, vec2(0.001), vec2(0.999));
        vec3 t0 = texture(gPosition, newTC).xyz;
        vec3 centerToSample = t0 - fragpos.xyz;
        float dist = length(centerToSample);
        if (dist < 0.0001) continue;
        vec3 centerToSampleNormalized = centerToSample / dist;

        float attenuation = 1.0f - clamp(dist / 6.0f, 0.0f, 1.0f);
        float dp = dot(normal, centerToSampleNormalized);

        attenuation = sqrt(max(dp, 0.0)) * attenuation*attenuation * step(attenuationAngleThreshold, dp);
        finalAO += attenuation * (aoWeight / numSamples);
    }

    return finalAO;
}

#else

float calcSSAO(vec3 fragPos, vec3 normal, bool leavesHack) {
    // tile noise texture over screen based on screen dimensions divided by noise size
    vec2 noiseScale = vec2(screenSize.x/8.0, screenSize.y/8.0);
    vec3 randomVec = texture(texNoise, texcoord * noiseScale).xyz;

    vec3 tangent = safeNormalize(randomVec - normal * dot(randomVec, normal), vec3(1.0, 0.0, 0.0));
    vec3 bitangent = safeNormalize(cross(normal, tangent), vec3(0.0, 1.0, 0.0));
    mat3 TBN = mat3(tangent, bitangent, normal);

    float occlusion = 0.0;

    for (int i = 0; i < kernelSize; ++i)
    {
        vec3 sample = TBN * samples[i];
    sample = fragPos + sample * radius;

vec4 offset = vec4(sample, 1.0);
offset = projection * offset;
if (abs(offset.w) < 0.000001) continue;
offset.xyz /= offset.w;
offset.xyz = offset.xyz * 0.5 + 0.5;

offset.x = clamp(offset.x, texcoord.x - 0.04, texcoord.x + 0.04);
offset.y = clamp(offset.y, texcoord.y - 0.04, texcoord.y + 0.04);

float sampleDepth = texture(gPosition, offset.xy).z;
float depthDiff = sampleDepth - (sample.z + bias);
float rangeCheck = 0;

if (leavesHack) {

    if (depthDiff >= 0.02 && depthDiff < 0.2 && abs(dot(texture(gNormal, offset.xy).rgb, normal) - 1) > 0.25) {
        rangeCheck = smoothstep(0.0, 1.0, radius / abs(fragPos.z - sampleDepth));
    }

} else {

    if (depthDiff >= 0 && depthDiff < 0.2) {
        rangeCheck = smoothstep(0.0, 1.0, radius / abs(fragPos.z - sampleDepth));
    }

}

occlusion += rangeCheck;
}

return occlusion / kernelSize;
}

#endif

void main()
{
    float wboitatn = max(0, 1 - texture(revealage, texcoord).r) * 0.75;

    vec4 texVal = texture(gPosition, texcoord);

    vec3 fragPos = texVal.xyz;
    float attenuate = texVal.w + wboitatn;

    texVal = texture(gNormal, texcoord);
    vec3 normal = safeNormalize(texVal.xyz, vec3(0.0, 1.0, 0.0));
    bool leavesHack = texVal.w > 0;

    // This seems to completely fix any distant ssao flickering artifacts while perservering everything else
    #if SSDO == 0
    if (!leavesHack) {
        fragPos += normal * clamp(-fragPos.z/90 - 0.05, 0, 10);
    }
    #endif


    float distanceFade = clamp(1.2 - (-fragPos.z) / 250, 0, 1);

    if (fragPos.x == 0 || distanceFade == 0) {
        outOcclusion = vec4(1);
        return;
    }

    #if SSDO > 0
    float occlusion = calcSSDO(fragPos, normal);
    #else
    float occlusion = calcSSAO(fragPos, normal, leavesHack);
    #endif

    float occ = clamp(1.0 - min(1, occlusion * distanceFade) * (1-attenuate), 0, 1);

    outOcclusion = vec4(occ, occ, occ, 1);
}
