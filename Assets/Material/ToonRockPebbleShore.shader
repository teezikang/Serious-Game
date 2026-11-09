Shader "Custom/ToonRockPebbleShore"
{
    Properties
    {
        [Header(Pattern Space)]
        [Enum(Quad UV,0,World XY (camera facing),1,World XZ (flat on ground),2)] _Space ("Pattern Space", Float) = 0

        [Header(Pebbles)]
        _PebbleColorA ("Pebble Color A", Color) = (0.62, 0.58, 0.52, 1)
        _PebbleColorB ("Pebble Color B", Color) = (0.78, 0.72, 0.62, 1)
        _PebbleColorC ("Pebble Color C (rare)", Color) = (0.45, 0.42, 0.42, 1)
        _PebbleScale  ("Pebble Density (cells per unit)", Float) = 3
        _PebbleGap    ("Pebble Gap Width", Range(0, 0.4)) = 0.10
        _PebbleBulge  ("Pebble Roundness", Range(0, 3)) = 1.4

        [Header(Rocks)]
        _RockColorA   ("Rock Color A", Color) = (0.42, 0.44, 0.48, 1)
        _RockColorB   ("Rock Color B", Color) = (0.55, 0.55, 0.57, 1)
        _RockColorC   ("Rock Color C (mossy)", Color) = (0.38, 0.48, 0.38, 1)
        _RockScale    ("Rock Density (cells per unit)", Float) = 0.7
        _RockDensity  ("Rock Chance Per Cell", Range(0, 1)) = 0.35
        _RockGap      ("Rock Gap Width", Range(0, 0.4)) = 0.12
        _RockBulge    ("Rock Roundness", Range(0, 3)) = 1.8
        _RockShadow   ("Rock Contact Shadow", Range(0, 1)) = 0.6
        _RockShadowWidth ("Rock Contact Shadow Width", Range(0.01, 0.6)) = 0.25

        [Header(Shape)]
        _Soft         ("Edge Softness", Range(0.001, 0.2)) = 0.05
        _JunctionSize ("Corner Rounding", Range(0, 0.6)) = 0.2
        _DomeSize     ("Dome Size", Range(0.05, 1)) = 0.4
        _GapColor     ("Gap / Dirt Color", Color) = (0.20, 0.17, 0.16, 1)

        [Header(Toon Lighting)]
        _LightDir     ("Light Direction (x, y)", Vector) = (-0.5, 0.6, 0, 0)
        _LightZ       ("Light Height", Range(0.1, 2)) = 0.7
        _LightSteps   ("Light Steps", Range(1, 6)) = 3
        _LightTint    ("Lit Tint", Color) = (1, 0.97, 0.90, 1)
        _ShadowTint   ("Shadow Tint", Color) = (0.55, 0.52, 0.68, 1)
        _RimWidth     ("Edge Darken Width", Range(0.01, 0.4)) = 0.15
        _RimDarken    ("Edge Darken Amount", Range(0, 1)) = 0.35
        _Highlight    ("Toon Highlight", Range(0, 1)) = 0.5
        _HighlightCut ("Highlight Cutoff", Range(0.5, 1.5)) = 1.05

        [Header(Wet Band Near Lake)]
        _WaterRect    ("Lake Rect in UV (xMin, yMin, xMax, yMax)", Vector) = (0.2, 0.2, 0.8, 0.8)
        _WetWidth     ("Wet Band Width", Float) = 0.6
        _WetWobble    ("Wet Edge Wobble", Float) = 0.4
        _WetTint      ("Wet Tint", Color) = (0.55, 0.62, 0.72, 1)
        _WetStrength  ("Wet Strength", Range(0, 1)) = 0.8

        [Header(Outer Scatter (fade into ground))]
        _FadeWidth    ("Fade Width From Quad Edge", Float) = 1.5
        _ScatterNoiseScale ("Scatter Noise Scale", Float) = 1.2
        _ScatterNoise ("Scatter Noise Amount", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ToonRockPebbleShore"
            Tags { "LightMode"="UniversalForward" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Space;
                half4 _PebbleColorA, _PebbleColorB, _PebbleColorC;
                float _PebbleScale, _PebbleGap, _PebbleBulge;
                half4 _RockColorA, _RockColorB, _RockColorC;
                float _RockScale, _RockDensity, _RockGap, _RockBulge, _RockShadow, _RockShadowWidth;
                float _Soft, _JunctionSize, _DomeSize;
                half4 _GapColor;
                float4 _LightDir;
                float _LightZ, _LightSteps;
                half4 _LightTint, _ShadowTint;
                float _RimWidth, _RimDarken, _Highlight, _HighlightCut;
                float4 _WaterRect;
                float _WetWidth, _WetWobble;
                half4 _WetTint;
                float _WetStrength;
                float _FadeWidth, _ScatterNoiseScale, _ScatterNoise;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float2 size : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                OUT.size = float2(length(unity_ObjectToWorld._m00_m10_m20),
                                  length(unity_ObjectToWorld._m01_m11_m21));
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

            struct Vor
            {
                float f1, f2, f3;   // three nearest distances
                float2 id1, id2;    // nearest and second nearest cell ids
                float2 r;           // vector from pixel to nearest cell center
            };

            Vor voronoi(float2 uv)
            {
                float2 cell = floor(uv);
                float2 f = frac(uv);
                Vor v;
                v.f1 = 8; v.f2 = 8; v.f3 = 8;
                v.id1 = 0; v.id2 = 0; v.r = 0;

                [unroll] for (int j = -1; j <= 1; j++)
                {
                    [unroll] for (int i = -1; i <= 1; i++)
                    {
                        float2 g = float2(i, j);
                        float2 o = hash22(cell + g) * 0.8 + 0.1; // static, kept inside its cell
                        float2 rv = g + o - f;
                        float d = length(rv);

                        if (d < v.f1)      { v.f3 = v.f2; v.f2 = v.f1; v.f1 = d; v.id2 = v.id1; v.id1 = cell + g; v.r = rv; }
                        else if (d < v.f2) { v.f3 = v.f2; v.f2 = d; v.id2 = cell + g; }
                        else if (d < v.f3) { v.f3 = d; }
                    }
                }
                return v;
            }

            // 1 inside a stone, 0 in the gap; rounded corners via the junction term
            float stoneMask(Vor v, float gapW, float soft, float jsize)
            {
                float edge = v.f2 - v.f1;
                float lineMask = 1.0 - smoothstep(gapW, gapW + soft, edge);
                float junc = 1.0 - smoothstep(0.0, max(jsize, 0.0001), v.f3 - v.f1);
                float gap = saturate(max(lineMask, junc * junc));
                return 1.0 - gap;
            }

            float edgeAA(float m)
            {
                float w = max(fwidth(m), 1e-4);
                return saturate((m - 0.5) / w + 0.5);
            }

            half3 shadeStone(float2 id, float2 r, float edge, float gapW, float bulge,
                             half3 cA, half3 cB, half3 cC)
            {
                float h1 = hash21(id);
                float h2 = hash21(id + 9.7);
                half3 baseCol = lerp(cA, cB, h1);
                baseCol = lerp(baseCol, cC, smoothstep(0.7, 1.0, h2) * 0.85);
                baseCol *= 0.9 + 0.2 * hash21(id + 3.1);

                // fake dome normal: tilts away from the stone center, flatter in the middle
                float hgt = saturate((edge - gapW) / _DomeSize);
                float3 n = normalize(float3(-r * bulge * (1.0 - hgt * 0.5), 1.0));
                float3 L = normalize(float3(_LightDir.xy, _LightZ));
                float ndl = dot(n, L);

                float steps = max(_LightSteps, 1.0);
                float lit = floor(saturate(ndl) * steps + 0.5) / steps;
                half3 col = baseCol * lerp(_ShadowTint.rgb, _LightTint.rgb, lit);

                // darker rim near the gap
                float rim = 1.0 - smoothstep(gapW + _Soft, gapW + _Soft + _RimWidth, edge);
                col *= 1.0 - rim * _RimDarken;

                // hard toon highlight
                col += step(_HighlightCut, ndl) * _Highlight * 0.25;
                return col;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ---------- Pattern coordinates in world units ----------
                float2 pat;
                if (_Space < 0.5)      pat = IN.uv * IN.size;
                else if (_Space < 1.5) pat = IN.positionWS.xy;
                else                   pat = IN.positionWS.xz;

                float2 pw = IN.uv * IN.size; // position on quad in world units

                // ---------- Outer scatter: stones shrink and thin out toward the quad edge ----------
                float edgeDist = min(min(pw.x, IN.size.x - pw.x), min(pw.y, IN.size.y - pw.y));
                float outerFade = _FadeWidth > 0.0001 ? 1.0 - smoothstep(0.0, _FadeWidth, edgeDist) : 0.0;
                float n2 = valueNoise(pat * _ScatterNoiseScale);
                float scatter = outerFade + (n2 - 0.5) * _ScatterNoise * outerFade;
                float sc = saturate(scatter);

                // ---------- Pebbles ----------
                Vor p = voronoi(pat * _PebbleScale);
                float gapP = _PebbleGap + sc * 0.45 + hash21(p.id1) * sc * 0.25;
                float stoneP = edgeAA(stoneMask(p, gapP, _Soft, _JunctionSize + sc * 0.2));

                // ---------- Rocks (bigger cells, only some are rocks) ----------
                Vor rk = voronoi(pat * _RockScale + 40.1);
                float rockCut = 1.0 - _RockDensity;
                float isRock1 = step(rockCut, hash21(rk.id1 + 5.5));
                float isRock2 = step(rockCut, hash21(rk.id2 + 5.5));
                float gapR = _RockGap + sc * 0.5;
                float stoneR = edgeAA(stoneMask(rk, gapR, _Soft * 1.5, _JunctionSize * 1.5 + sc * 0.2)) * isRock1;

                // ---------- Compose ----------
                half3 dirt = _GapColor.rgb * (0.85 + 0.3 * valueNoise(pat * 3.0));
                half3 col = dirt;

                half3 pebCol = shadeStone(p.id1, p.r, p.f2 - p.f1, gapP, _PebbleBulge,
                                          _PebbleColorA.rgb, _PebbleColorB.rgb, _PebbleColorC.rgb);
                col = lerp(col, pebCol, stoneP);

                // contact shadow around rocks, drawn on pebbles and dirt next to them
                float rockNear = max(isRock1, isRock2);
                float edgeR = rk.f2 - rk.f1;
                float shadowZone = rockNear * (1.0 - smoothstep(gapR, gapR + _RockShadowWidth, edgeR));
                col = lerp(col, col * _ShadowTint.rgb * 0.6, shadowZone * _RockShadow);

                half3 rockCol = shadeStone(rk.id1, rk.r, edgeR, gapR, _RockBulge,
                                           _RockColorA.rgb, _RockColorB.rgb, _RockColorC.rgb);
                col = lerp(col, rockCol, stoneR);

                // ---------- Wet band around the lake (rect in UV space) ----------
                float2 rmin = _WaterRect.xy * IN.size;
                float2 rmax = _WaterRect.zw * IN.size;
                float2 c = (rmin + rmax) * 0.5;
                float2 hs = (rmax - rmin) * 0.5;
                float2 q = abs(pw - c) - hs;
                float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0); // <0 inside the lake
                sd += (n2 - 0.5) * _WetWobble;
                float wet = 1.0 - smoothstep(_WetWidth - 0.05, _WetWidth, sd);
                col = lerp(col, col * _WetTint.rgb, wet * _WetStrength);

                // ---------- Cutout: dirt disappears first in the scatter zone ----------
                float hasStone = max(stoneP, stoneR);
                float dirtVisible = 1.0 - step(0.35, scatter);
                float alpha = max(hasStone, dirtVisible);
                clip(alpha - 0.5);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
