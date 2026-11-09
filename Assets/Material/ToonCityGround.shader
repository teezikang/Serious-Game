Shader "Custom/ToonCityGround"
{
    Properties
    {
        [Header(Pattern)]
        _Rotation     ("Pattern Rotation (degrees)", Float) = 0

        [Header(Color Blotches)]
        _ColorA       ("Base Color", Color)      = (0.93, 0.82, 0.66, 1)
        _ColorB       ("Warm Patch Color", Color) = (0.96, 0.70, 0.55, 1)
        _ColorC       ("Grass / Moss Patch Color", Color) = (0.62, 0.78, 0.48, 1)
        _BlotchScale  ("Blotch Scale", Float) = 0.08
        _BlotchCut1   ("Warm Patch Amount (higher = less)", Range(0, 1)) = 0.62
        _BlotchCut2   ("Grass Patch Amount (higher = less)", Range(0, 1)) = 0.66

        [Header(Pavers)]
        _TileScale    ("Paver Density (tiles per unit)", Float) = 0.5
        _TileGap      ("Gap Width", Range(0, 0.2)) = 0.05
        _TileRound    ("Corner Roundness", Range(0, 0.4)) = 0.12
        _TileVariation("Tile Color Variation", Range(0, 0.5)) = 0.14
        _GapColor     ("Gap Tint", Color) = (0.62, 0.52, 0.50, 1)
        _Bevel        ("Bevel Width", Range(0.01, 0.3)) = 0.12
        _BevelStrength("Bevel Strength", Range(0, 1)) = 0.5

        [Header(Specks)]
        _SpeckColor   ("Speck Color", Color) = (1, 0.95, 0.85, 1)
        _SpeckScale   ("Speck Grid Scale", Float) = 1.5
        _SpeckAmount  ("Speck Amount", Range(0, 1)) = 0.12

        [Header(Toon Lighting and Shadows)]
        _ShadowTint   ("Shadow Tint", Color) = (0.62, 0.58, 0.82, 1)
        _ShadowSoftness ("Shadow Edge Softness", Range(0.001, 0.4)) = 0.03
        _LightInfluence ("Sun Color Influence", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ToonCityGround"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Rotation;
                half4 _ColorA, _ColorB, _ColorC;
                float _BlotchScale, _BlotchCut1, _BlotchCut2;
                float _TileScale, _TileGap, _TileRound, _TileVariation;
                half4 _GapColor;
                float _Bevel, _BevelStrength;
                half4 _SpeckColor;
                float _SpeckScale, _SpeckAmount;
                half4 _ShadowTint;
                float _ShadowSoftness, _LightInfluence;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            float hash21(float2 p) { return frac(sin(dot(p, float2(41.3, 289.1))) * 43758.5453); }

            float2 hash22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                return valueNoise(p) * 0.65 + valueNoise(p * 2.1 + 5.2) * 0.35;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ---------- World-space pattern, rotated to match your city grid ----------
                float2 p = IN.positionWS.xz;
                float rad = radians(_Rotation);
                float cs = cos(rad);
                float sn = sin(rad);
                p = float2(p.x * cs - p.y * sn, p.x * sn + p.y * cs);

                // ---------- Large hard-edged color blotches ----------
                float n1 = fbm(p * _BlotchScale);
                float n2 = fbm(p * _BlotchScale * 0.8 + 31.7);
                half3 baseCol = _ColorA.rgb;
                baseCol = lerp(baseCol, _ColorB.rgb, smoothstep(_BlotchCut1, _BlotchCut1 + 0.03, n1));
                baseCol = lerp(baseCol, _ColorC.rgb, smoothstep(_BlotchCut2, _BlotchCut2 + 0.03, n2));

                // ---------- Rounded pavers in a running-bond pattern ----------
                float2 q = p * _TileScale;
                float rowId = floor(q.y);
                q.x += rowId * 0.5;
                float2 cellId = floor(q);
                float2 f = frac(q) - 0.5;

                float hb = 0.5 - _TileGap;           // half size of a tile body
                float rr = min(_TileRound, hb);
                float2 dd = abs(f) - (hb - rr);
                float sdf = length(max(dd, 0.0)) + min(max(dd.x, dd.y), 0.0) - rr;
                float aa = max(fwidth(q.x) * 1.5, 1e-4);
                float inside = 1.0 - smoothstep(-aa, aa, sdf);

                float th = hash21(cellId);
                half3 tileCol = baseCol * (1.0 + (th - 0.5) * _TileVariation * 2.0);

                // bevel: lighter on the top-left edge, darker on the bottom-right
                float edgeBand = 1.0 - smoothstep(0.0, _Bevel, -sdf);
                float bevelDir = f.y - f.x * 0.5;
                tileCol *= 1.0 + edgeBand * bevelDir * _BevelStrength * 0.6;

                half3 gapCol = baseCol * _GapColor.rgb;
                half3 col = lerp(gapCol, tileCol, inside);

                // ---------- Small specks (pebbles / petals) ----------
                float2 sp = p * _SpeckScale;
                float2 spCell = floor(sp);
                float2 spF = frac(sp);
                float exists = step(1.0 - _SpeckAmount, hash21(spCell + 7.7));
                float spDist = length(spF - (0.2 + 0.6 * hash22(spCell)));
                float speck = exists * (1.0 - smoothstep(0.08, 0.11, spDist));
                col = lerp(col, _SpeckColor.rgb, speck);

                // ---------- Toon shadows received from buildings and props ----------
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float toon = smoothstep(0.5 - _ShadowSoftness, 0.5 + _ShadowSoftness, mainLight.shadowAttenuation);
                col = lerp(col * _ShadowTint.rgb, col, toon);
                col *= lerp(half3(1, 1, 1), mainLight.color, _LightInfluence);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
