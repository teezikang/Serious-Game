Shader "ArcadeRecycler/Toon"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _OutlineColor("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth("Outline Width (pixels)", Range(0,4)) = 1.35
        _ApplyPlayerMask("Apply Player Occlusion Mask", Float) = 0
        _PlayerOcclusionCircle("Player Occlusion Circle", Vector) = (0.5,0.5,0.17,0.025)
        _PlayerOcclusionDepth("Player Occlusion Depth", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _OutlineColor;
                float _OutlineWidth;
                float _ApplyPlayerMask;
                float4 _PlayerOcclusionCircle;
                float _PlayerOcclusionDepth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 shadowCoord : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float3 worldPos : TEXCOORD4;
                float4 screenPos : TEXCOORD5;
            };

            void ApplyPlayerOcclusionMask(float4 screenPos, float3 worldPos, float2 pixelPos)
            {
                if (_ApplyPlayerMask < 0.5)
                    return;

                float2 screenUV = screenPos.xy / screenPos.w;
                float2 delta = screenUV - _PlayerOcclusionCircle.xy;
                delta.x *= _ScreenParams.x / _ScreenParams.y;
                float radius = _PlayerOcclusionCircle.z;
                float softness = max(_PlayerOcclusionCircle.w, 0.0001);
                float distanceFromPlayer = length(delta);
                float coverage = smoothstep(radius - softness, radius + softness, distanceFromPlayer);

                float objectDepth = -TransformWorldToView(worldPos).z;
                float inFrontOfPlayer = step(objectDepth, _PlayerOcclusionDepth - 0.01);
                float dither = frac(52.9829189 * frac(dot(pixelPos, float2(0.06711056, 0.00583715))));
                clip(coverage - dither + (1.0 - inFrontOfPlayer) * 2.0);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.shadowCoord = GetShadowCoord(positions);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.worldPos = positions.positionWS;
                output.screenPos = ComputeScreenPos(positions.positionCS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                ApplyPlayerOcclusionMask(input.screenPos, input.worldPos, input.positionCS.xy);
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);
                float diffuse = saturate(dot(normalWS, mainLight.direction));

                // Three broad lighting bands keep imported models in one clear toon style.
                float toonBand = diffuse > 0.68 ? 1.0 : (diffuse > 0.28 ? 0.72 : 0.48);
                toonBand *= lerp(0.58, 1.0, mainLight.shadowAttenuation);

                float3 ambient = SampleSH(normalWS) * 0.42;
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float3 color = albedo.rgb * (toonBand * mainLight.color + ambient);
                color = MixFog(color, input.fogFactor);
                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BlackOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _OutlineColor;
                float _OutlineWidth;
                float _ApplyPlayerMask;
                float4 _PlayerOcclusionCircle;
                float _PlayerOcclusionDepth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            void ApplyPlayerOcclusionMask(float4 screenPos, float3 worldPos, float2 pixelPos)
            {
                if (_ApplyPlayerMask < 0.5)
                    return;

                float2 screenUV = screenPos.xy / screenPos.w;
                float2 delta = screenUV - _PlayerOcclusionCircle.xy;
                delta.x *= _ScreenParams.x / _ScreenParams.y;
                float radius = _PlayerOcclusionCircle.z;
                float softness = max(_PlayerOcclusionCircle.w, 0.0001);
                float distanceFromPlayer = length(delta);
                float coverage = smoothstep(radius - softness, radius + softness, distanceFromPlayer);

                float objectDepth = -TransformWorldToView(worldPos).z;
                float inFrontOfPlayer = step(objectDepth, _PlayerOcclusionDepth - 0.01);
                float dither = frac(52.9829189 * frac(dot(pixelPos, float2(0.06711056, 0.00583715))));
                clip(coverage - dither + (1.0 - inFrontOfPlayer) * 2.0);
            }

            Varyings vertOutline(Attributes input)
            {
                Varyings output;
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 normalVS = TransformWorldToViewDir(normalWS, true);
                float2 outlineDirection = normalVS.xy;
                float directionLength = length(outlineDirection);
                outlineDirection = directionLength > 0.0001
                    ? outlineDirection / directionLength
                    : float2(0.0, 1.0);

                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(worldPos);
                float2 pixelSize = 2.0 / _ScreenParams.xy;
                output.positionCS.xy += outlineDirection * (_OutlineWidth * pixelSize) * output.positionCS.w;
                output.worldPos = worldPos;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 fragOutline(Varyings input) : SV_Target
            {
                ApplyPlayerOcclusionMask(input.screenPos, input.worldPos, input.positionCS.xy);
                return _OutlineColor;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
