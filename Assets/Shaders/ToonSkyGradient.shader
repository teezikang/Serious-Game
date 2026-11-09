Shader "ArcadeRecycler/Toon Sky Gradient"
{
    Properties
    {
        _TopColor ("Zenith", Color) = (0.22, 0.58, 0.86, 1)
        _HorizonColor ("Horizon", Color) = (0.68, 0.84, 0.91, 1)
        _GroundColor ("Below Horizon", Color) = (0.31, 0.43, 0.52, 1)
        _HorizonWidth ("Horizon Blend", Range(0.05, 0.8)) = 0.35
        _GradientPower ("Sky Gradient", Range(0.25, 3)) = 0.8
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _GroundColor;
                half _HorizonWidth;
                half _GradientPower;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                #if UNITY_REVERSED_Z
                    output.positionCS.z = 0.0;
                #else
                    output.positionCS.z = output.positionCS.w;
                #endif
                output.direction = input.positionOS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half height = normalize(input.direction).y;
                half horizon = max(_HorizonWidth, 0.001h);

                if (height >= 0.0h)
                {
                    half skyBlend = pow(saturate(height), max(_GradientPower, 0.001h));
                    return lerp(_HorizonColor, _TopColor, skyBlend);
                }

                half groundBlend = smoothstep(-horizon, 0.0h, height);
                return lerp(_GroundColor, _HorizonColor, groundBlend);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
