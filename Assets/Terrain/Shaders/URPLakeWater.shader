Shader "Custom/URPLakeWater"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.14, 0.56, 0.58, 1)
        _DeepColor ("Deep Color", Color) = (0.03, 0.14, 0.24, 1)
        _SkyReflectColor ("Sky Reflect Color", Color) = (0.58, 0.76, 0.92, 1)
        _FoamColor ("Foam Color", Color) = (0.92, 0.96, 0.98, 1)
        _WaterNormal ("Water Normal Map", 2D) = "bump" {}
        _NormalScale1 ("Normal Tiling 1", Float) = 0.022
        _NormalScale2 ("Normal Tiling 2", Float) = 0.058
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.45
        _WaveSpeed ("Wave Speed", Float) = 0.028
        _DepthFade ("Color Depth Distance", Float) = 18.0
        _EdgeFade ("Shore Transparency Distance", Float) = 2.6
        _FoamDistance ("Shore Foam Distance", Float) = 1.6
        _RefractionStrength ("Refraction Strength", Range(0, 0.1)) = 0.018
        _SpecularPower ("Specular Power", Float) = 240.0
        _SpecularIntensity ("Specular Intensity", Float) = 1.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_WaterNormal);
            SAMPLER(sampler_WaterNormal);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _SkyReflectColor;
                half4 _FoamColor;
                float _NormalScale1;
                float _NormalScale2;
                float _NormalStrength;
                float _WaveSpeed;
                float _DepthFade;
                float _EdgeFade;
                float _FoamDistance;
                float _RefractionStrength;
                float _SpecularPower;
                float _SpecularIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float fogCoord    : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float t = _Time.y;
                posWS.y += (sin(posWS.x * 0.045 + t * 0.9) + cos(posWS.z * 0.038 + t * 0.75)) * 0.08;

                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEyeDepth = IN.screenPos.w;
                float waterDepth = max(0.0, sceneEyeDepth - waterEyeDepth);

                float t = _Time.y * _WaveSpeed;
                float2 uv1 = IN.positionWS.xz * _NormalScale1 + float2(t * 0.9, t * 0.55);
                float2 uv2 = IN.positionWS.xz * _NormalScale2 + float2(-t * 0.65, t * 0.8);

                half3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_WaterNormal, sampler_WaterNormal, uv1), _NormalStrength);
                half3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_WaterNormal, sampler_WaterNormal, uv2), _NormalStrength * 0.75);
                half3 tangentN = normalize(half3(n1.xy + n2.xy, n1.z * n2.z));
                half3 normalWS = normalize(half3(tangentN.x, tangentN.z, tangentN.y));

                half3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                Light mainLight = GetMainLight();
                half3 lightDirWS = normalize(mainLight.direction);

                float depthT = 1.0 - exp(-waterDepth / max(0.01, _DepthFade));
                half3 waterBody = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depthT));

                float2 refractUV = screenUV + normalWS.xz * _RefractionStrength * saturate(waterDepth * 0.5);
                half3 underwaterCol = SampleSceneColor(refractUV);
                float clarity = exp(-waterDepth / max(0.01, _EdgeFade * 2.2));
                half3 refracted = lerp(waterBody, underwaterCol * lerp(half3(1,1,1), _ShallowColor.rgb * 1.45, 0.45), saturate(clarity * 0.78));

                half NdotV = saturate(dot(normalWS, viewDirWS));
                half fresnel = pow(1.0 - NdotV, 4.0);
                fresnel = lerp(0.04, 0.58, fresnel);

                half NdotL = saturate(dot(normalWS, lightDirWS));
                half3 litWater = refracted * (0.65 + 0.35 * NdotL * mainLight.color);
                half3 skyCol = _SkyReflectColor.rgb * (0.75 + 0.25 * mainLight.color);
                half3 col = lerp(litWater, skyCol, fresnel);

                half3 halfDir = normalize(lightDirWS + viewDirWS);
                half NdotH = saturate(dot(normalWS, halfDir));
                half specSharp = pow(NdotH, _SpecularPower) * _SpecularIntensity;
                half specBroad = pow(NdotH, 32.0) * 0.16;
                col += mainLight.color * (specSharp + specBroad);

                float foamNoise = saturate((n1.x + n2.y) * 1.8 + 0.55);
                float waveLap = sin(waterDepth * 4.5 - _Time.y * 1.8) * 0.5 + 0.5;
                float shoreMask = 1.0 - saturate(waterDepth / max(0.01, _FoamDistance));
                float foam = saturate(shoreMask * shoreMask * (0.55 + 0.45 * foamNoise) * (0.65 + 0.35 * waveLap));
                col = lerp(col, _FoamColor.rgb * (0.85 + 0.15 * mainLight.color), foam * 0.72);

                half alpha = saturate(waterDepth / 0.45);
                alpha = max(alpha, foam * 0.65);

                col = MixFog(col, IN.fogCoord);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}