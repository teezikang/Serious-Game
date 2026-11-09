Shader "Custom/ToonLakeQuad"
{
    Properties
    {
        [Header(Pattern Space)]
        [Enum(Quad UV,0,World XY (camera facing),1,World XZ (flat on ground),2)] _Space ("Pattern Space", Float) = 0

        [Header(Water Color and Depth)]
        _ShallowColor ("Shallow Color", Color) = (0.25, 0.75, 0.85, 1)
        _DeepColor    ("Deep Color", Color)    = (0.04, 0.20, 0.45, 1)
        _SkyColor     ("Sky Reflection Color", Color) = (0.75, 0.9, 1, 1)
        _SkyGradient  ("Sky Reflection (top of quad)", Range(0, 1)) = 0.25
        _DepthMax     ("Distance From Shore For Deep Color", Float) = 3
        _ShallowAlpha ("Shallow Opacity", Range(0, 1)) = 0.55
        _DeepAlpha    ("Deep Opacity", Range(0, 1)) = 0.95
        _ColorSteps   ("Toon Depth Steps (0 = smooth)", Range(0, 8)) = 3

        [Header(Shore (fake depth))]
        _ShoreSides   ("Shore Edges  L / R / Bottom / Top (0 or 1)", Vector) = (1, 1, 1, 1)
        [Toggle(_USE_SHOREMAP)] _UseShoreMap ("Use Shore Map Texture", Float) = 0
        [NoScaleOffset] _ShoreMap ("Shore Map (black = shore, white = deep)", 2D) = "white" {}
        _MapDepthRange ("Shore Map Distance Range", Float) = 3

        [Header(Shore Foam)]
        _FoamColor    ("Foam / Caustic Color", Color) = (0.95, 0.98, 1, 1)
        _FoamDistance ("Foam Width", Float) = 0.4
        _FoamNoiseScale ("Foam Noise Scale", Float) = 2.5
        _FoamNoiseAmount ("Foam Wobble", Range(0, 1)) = 0.5
        _FoamSpeed    ("Foam Speed", Float) = 0.3

        [Header(Cell Caustics)]
        _CellScale    ("Cell Scale", Float) = 0.6
        _CellSpeed    ("Cell Speed", Float) = 0.25
        _LineWidth    ("Line Width", Range(0, 0.5)) = 0.08
        _LineSoftness ("Line Softness", Range(0.001, 0.5)) = 0.10
        _CellStrength ("Caustic Strength", Range(0, 1)) = 0.8
        _CellFadeDepth("Caustic Fade Distance", Float) = 3
        _CellBase     ("Caustic Minimum In Deep Water", Range(0, 1)) = 0.35

        [Header(Surface Motion)]
        _WarpAmount   ("Ripple Warp Amount", Float) = 0.12
        _WarpFreq     ("Ripple Warp Frequency", Float) = 1.2
        _WaveSpeed    ("Ripple Speed", Float) = 0.6

        [Header(Sparkles)]
        _SparkleScale ("Sparkle Scale", Float) = 6
        _SparkleSpeed ("Sparkle Speed", Float) = 0.6
        _SparkleThreshold ("Sparkle Threshold", Range(0.5, 0.99)) = 0.85
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ToonLakeQuad"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _USE_SHOREMAP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ShoreMap);
            SAMPLER(sampler_ShoreMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _SkyColor, _FoamColor;
                float _Space, _SkyGradient, _DepthMax, _ShallowAlpha, _DeepAlpha, _ColorSteps;
                float4 _ShoreSides;
                float _MapDepthRange;
                float _FoamDistance, _FoamNoiseScale, _FoamNoiseAmount, _FoamSpeed;
                float _CellScale, _CellSpeed, _LineWidth, _LineSoftness, _CellStrength, _CellFadeDepth, _CellBase;
                float _WarpAmount, _WarpFreq, _WaveSpeed;
                float _SparkleScale, _SparkleSpeed, _SparkleThreshold;
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
                float2 size : TEXCOORD2;   // quad size in world units
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                // World size of the quad (a default Unity Quad is 1x1 object units)
                OUT.size = float2(length(unity_ObjectToWorld._m00_m10_m20),
                                  length(unity_ObjectToWorld._m01_m11_m21));
                return OUT;
            }

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
                float a = hash22(i).x;
                float b = hash22(i + float2(1, 0)).x;
                float c = hash22(i + float2(0, 1)).x;
                float d = hash22(i + float2(1, 1)).x;
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // returns F2 - F1 (~0 along cell borders)
            float voronoiEdge(float2 uv, float t)
            {
                float2 cell = floor(uv);
                float2 f = frac(uv);
                float d1 = 8, d2 = 8;
                [unroll] for (int j = -1; j <= 1; j++)
                {
                    [unroll] for (int i = -1; i <= 1; i++)
                    {
                        float2 g = float2(i, j);
                        float2 o = 0.5 + 0.45 * sin(t + 6.2831 * hash22(cell + g));
                        float d = length(g + o - f);
                        if (d < d1) { d2 = d1; d1 = d; }
                        else if (d < d2) { d2 = d; }
                    }
                }
                return d2 - d1;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float time = _Time.y;

                // ---------- Pattern coordinates (world-unit sized in every mode) ----------
                float2 pat;
                if (_Space < 0.5)      pat = IN.uv * IN.size;
                else if (_Space < 1.5) pat = IN.positionWS.xy;
                else                   pat = IN.positionWS.xz;

                // gentle ripple warp, so the pattern wobbles like surface water
                float wt = time * _WaveSpeed;
                pat += float2(sin(pat.y * _WarpFreq + wt),
                              sin(pat.x * _WarpFreq * 1.3 + wt * 1.2)) * _WarpAmount;

                // ---------- Fake depth: distance from shore in world units ----------
                float2 d = IN.uv * IN.size;            // distance from left / bottom edge
                float2 dFar = (1.0 - IN.uv) * IN.size; // distance from right / top edge
                float shoreDist = 1000.0;
                shoreDist = min(shoreDist, d.x    + (1.0 - _ShoreSides.x) * 1000.0); // left
                shoreDist = min(shoreDist, dFar.x + (1.0 - _ShoreSides.y) * 1000.0); // right
                shoreDist = min(shoreDist, d.y    + (1.0 - _ShoreSides.z) * 1000.0); // bottom
                shoreDist = min(shoreDist, dFar.y + (1.0 - _ShoreSides.w) * 1000.0); // top

                #ifdef _USE_SHOREMAP
                    float mapVal = SAMPLE_TEXTURE2D(_ShoreMap, sampler_ShoreMap, IN.uv).r;
                    shoreDist = min(shoreDist, mapVal * _MapDepthRange);
                #endif

                // ---------- Water color by depth ----------
                float depth01 = saturate(shoreDist / _DepthMax);
                if (_ColorSteps > 0.5)
                    depth01 = floor(depth01 * _ColorSteps + 0.5) / _ColorSteps;

                half3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);
                float alpha = lerp(_ShallowAlpha, _DeepAlpha, depth01);

                // ---------- Cell caustics ----------
                float edge = voronoiEdge(pat * _CellScale, time * _CellSpeed);
                float cellLine = 1.0 - smoothstep(_LineWidth, _LineWidth + _LineSoftness, edge);
                float cellFade = max(1.0 - saturate(shoreDist / _CellFadeDepth), _CellBase);
                float caustic = cellLine * cellFade * _CellStrength;
                col = lerp(col, _FoamColor.rgb, caustic);
                alpha = max(alpha, caustic * 0.9);

                // ---------- Sky reflection gradient (top of the quad) ----------
                float sky = smoothstep(0.0, 1.0, IN.uv.y) * _SkyGradient;
                col = lerp(col, _SkyColor.rgb, sky);

                // ---------- Sparkles ----------
                float sp = valueNoise(pat * _SparkleScale + float2(time * _SparkleSpeed, -time * _SparkleSpeed * 0.6));
                sp *= valueNoise(pat * _SparkleScale * 1.7 + 11.3 - time * _SparkleSpeed);
                float sparkle = smoothstep(_SparkleThreshold * 0.6, _SparkleThreshold * 0.6 + 0.03, sp);
                col = lerp(col, _FoamColor.rgb, sparkle);
                alpha = max(alpha, sparkle);

                // ---------- Shore foam ----------
                float2 fuv = pat * _FoamNoiseScale + float2(time * _FoamSpeed, -time * _FoamSpeed * 0.7);
                float n = valueNoise(fuv) * 0.6 + valueNoise(fuv * 2.3 + 7.1) * 0.4;
                float foamDist = _FoamDistance * (1.0 - _FoamNoiseAmount + n * _FoamNoiseAmount * 2.0);
                float foam = 1.0 - smoothstep(foamDist * 0.85, foamDist, shoreDist);
                col = lerp(col, _FoamColor.rgb, foam);
                alpha = max(alpha, foam);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
