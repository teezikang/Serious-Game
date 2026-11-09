Shader "SeriousGame/Isometric Ground Grid"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.16, 0.25, 0.25, 1)
        _AlternateColor ("Alternate Tile Color", Color) = (0.19, 0.29, 0.28, 1)
        _GridColor ("Grid Line Color", Color) = (0.32, 0.46, 0.42, 1)
        _TileSize ("Tile Size", Float) = 2.5
        _GridWidth ("Grid Width", Range(0.002, 0.08)) = 0.018
        _GroundScale ("Ground Scale", Float) = 120
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "GroundGrid"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _AlternateColor;
                float4 _GridColor;
                float _TileSize;
                float _GridWidth;
                float _GroundScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float2 groundPosition : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.groundPosition = input.positionOS.xz * _GroundScale;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (normalize(input.normalWS).y < 0.8)
                    return half4(MixFog(_BaseColor.rgb, input.fogFactor), 1.0);

                float size = max(_TileSize, 0.001);
                float2 worldGrid = input.groundPosition / size;
                float2 diamondGrid = float2(
                    worldGrid.x + worldGrid.y,
                    worldGrid.x - worldGrid.y) * 0.5;

                float2 tileIndex = floor(diamondGrid);
                float checker = fmod(abs(tileIndex.x + tileIndex.y), 2.0);
                float3 color = lerp(_BaseColor.rgb, _AlternateColor.rgb, checker * 0.35);

                float2 distanceToLine = abs(frac(diamondGrid + 0.5) - 0.5);
                float2 antiAlias = float2(
                    fwidth(diamondGrid.x),
                    fwidth(diamondGrid.y));
                antiAlias = max(antiAlias, float2(0.0001, 0.0001));
                float2 lineMaskPerAxis = 1.0 - smoothstep(
                    float2(_GridWidth, _GridWidth),
                    float2(_GridWidth, _GridWidth) + antiAlias,
                    distanceToLine);
                float gridMask = max(lineMaskPerAxis.x, lineMaskPerAxis.y);
                color = lerp(color, _GridColor.rgb, gridMask);
                color = MixFog(color, input.fogFactor);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
