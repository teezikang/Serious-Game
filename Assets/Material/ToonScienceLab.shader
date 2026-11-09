Shader "Custom/ToonScienceLab"
{
    Properties
    {
        [Header(Walls)]
        _WallColor    ("Wall Color", Color)   = (0.92, 0.95, 0.97, 1)
        _AccentColor  ("Accent Color (teal)", Color) = (0.10, 0.62, 0.68, 1)
        _CornerColor  ("Corner Pillar Color", Color) = (0.70, 0.78, 0.84, 1)
        _SlabTint     ("Floor Slab Tint", Color) = (0.82, 0.86, 0.90, 1)
        _SlabH        ("Floor Slab Height", Float) = 0.07
        _CornerW      ("Corner Pillar Width", Float) = 0.14
        _AccentEvery  ("Accent Strip Every N Bays", Float) = 3
        _StripeW      ("Accent Strip Width", Float) = 0.10

        [Header(Layout (fits any cube scale))]
        _FloorHeight  ("Target Floor Height", Float) = 1.4
        _BayWidth     ("Target Window Bay Width", Float) = 1.6

        [Header(Windows)]
        _GlassColor   ("Glass Light", Color) = (0.45, 0.82, 0.95, 1)
        _GlassDark    ("Glass Dark", Color)  = (0.12, 0.38, 0.62, 1)
        _FrameColor   ("Frame Color", Color) = (0.18, 0.28, 0.38, 1)
        _SillColor    ("Sill Color", Color)  = (0.76, 0.82, 0.88, 1)
        _WinWidth     ("Window Width (fraction of bay)", Range(0.2, 0.95)) = 0.62
        _WinHeight    ("Window Height (fraction of floor)", Range(0.2, 0.9)) = 0.55
        _WinBottom    ("Window Bottom (fraction of floor)", Range(0, 0.5)) = 0.24
        _WinRound     ("Window Corner Radius", Float) = 0.08
        _FrameW       ("Frame Thickness", Float) = 0.06
        _SillH        ("Sill Height", Float) = 0.05
        _LitAmount    ("Lit Windows Amount", Range(0, 1)) = 0.3
        _WindowLitColor ("Lit Window Color", Color) = (1, 0.88, 0.50, 1)
        _WindowGlow   ("Lit Window Glow", Range(0, 2)) = 0.6

        [Header(Door and Sign)]
        [Enum(Plus Z,0,Minus Z,1,Plus X,2,Minus X,3,All Sides,4,None,5)] _DoorSide ("Door Side", Float) = 0
        _DoorWidth    ("Door Width", Float) = 1.2
        _DoorHeight   ("Door Height", Float) = 1.0
        _SignSize     ("Sign Size (0 = no sign)", Float) = 0.4
        _SignColor    ("Sign Color", Color) = (0.06, 0.28, 0.34, 1)
        _LiquidColor  ("Flask Liquid Color", Color) = (0.35, 0.95, 0.45, 1)

        [Header(Base and Top Trim)]
        _BaseH        ("Hazard Base Height", Float) = 0.16
        _HazardYellow ("Hazard Yellow", Color) = (1, 0.80, 0.10, 1)
        _HazardDark   ("Hazard Dark", Color)   = (0.15, 0.15, 0.18, 1)
        _HazardScale  ("Hazard Stripe Density", Float) = 5
        _CorniceH     ("Top Trim Height", Float) = 0.18

        [Header(Roof)]
        _RoofColor    ("Roof Color", Color) = (0.62, 0.66, 0.70, 1)
        _ParapetColor ("Parapet Color", Color) = (0.88, 0.92, 0.95, 1)
        _ParapetW     ("Parapet Width", Float) = 0.25
        _RoofTile     ("Roof Panel Size", Float) = 1.0

        [Header(Helipad)]
        _PadColor     ("Pad Color", Color) = (0.22, 0.25, 0.30, 1)
        _PadRingColor ("Pad Ring Color", Color) = (1, 0.80, 0.10, 1)
        _PadHColor    ("Pad H Color", Color) = (0.97, 0.97, 0.97, 1)
        _PadRadius    ("Pad Radius (fraction of roof)", Range(0.2, 0.8)) = 0.6
        _PadRotation  ("Pad Rotation (degrees)", Float) = 0
        _PadLights    ("Landing Light Count", Float) = 12
        _LightOn      ("Light On Color", Color)  = (1, 0.45, 0.15, 1)
        _LightOff     ("Light Off Color", Color) = (0.25, 0.12, 0.08, 1)
        _LightSpeed   ("Light Chase Speed", Float) = 3
        _LightGlow    ("Light Glow", Range(0, 3)) = 1.2

        [Header(Roof Fans)]
        _FanRadius    ("Fan Radius (0 = none)", Float) = 0.4
        _FanSpeed     ("Fan Speed", Float) = 4
        _FanLight     ("Fan Blade Light", Color) = (0.85, 0.88, 0.92, 1)
        _FanDark      ("Fan Blade Dark", Color)  = (0.30, 0.34, 0.40, 1)

        [Header(Toon Lighting)]
        _ShadowTint   ("Shadow Tint", Color) = (0.58, 0.62, 0.82, 1)
        _LightSteps   ("Light Steps", Range(1, 5)) = 2
        _ShadowSoftness ("Shadow Edge Softness", Range(0.001, 0.4)) = 0.03
        _LightInfluence ("Sun Color Influence", Range(0, 1)) = 0.3
        _OutlineColor ("Edge Line Color", Color) = (0.10, 0.18, 0.28, 1)
        _OutlineWidth ("Edge Line Width", Float) = 0.04
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _WallColor, _AccentColor, _CornerColor, _SlabTint;
            float _SlabH, _CornerW, _AccentEvery, _StripeW;
            float _FloorHeight, _BayWidth;
            half4 _GlassColor, _GlassDark, _FrameColor, _SillColor;
            float _WinWidth, _WinHeight, _WinBottom, _WinRound, _FrameW, _SillH, _LitAmount;
            half4 _WindowLitColor;
            float _WindowGlow;
            float _DoorSide, _DoorWidth, _DoorHeight, _SignSize;
            half4 _SignColor, _LiquidColor;
            float _BaseH;
            half4 _HazardYellow, _HazardDark;
            float _HazardScale, _CorniceH;
            half4 _RoofColor, _ParapetColor;
            float _ParapetW, _RoofTile;
            half4 _PadColor, _PadRingColor, _PadHColor;
            float _PadRadius, _PadRotation, _PadLights;
            half4 _LightOn, _LightOff;
            float _LightSpeed, _LightGlow;
            float _FanRadius, _FanSpeed;
            half4 _FanLight, _FanDark;
            half4 _ShadowTint;
            float _LightSteps, _ShadowSoftness, _LightInfluence;
            half4 _OutlineColor;
            float _OutlineWidth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ToonScienceLab"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalOS   : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float3 scale      : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS = IN.normalOS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.scale = float3(length(unity_ObjectToWorld._m00_m10_m20),
                                   length(unity_ObjectToWorld._m01_m11_m21),
                                   length(unity_ObjectToWorld._m02_m12_m22));
                return OUT;
            }

            float hash21(float2 p) { return frac(sin(dot(p, float2(41.3, 289.1))) * 43758.5453); }

            // 1 inside a shape (sd < 0), 0 outside, anti-aliased with screen derivatives
            float aaMask(float sd)
            {
                float w = max(fwidth(sd), 1e-5);
                return saturate(0.5 - sd / w);
            }

            float sdBox(float2 p, float2 b)
            {
                float2 d = abs(p) - b;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                return sdBox(p, b - r) - r;
            }

            void fanLayer(float2 rw, float2 center, float radius, float t, inout half3 col)
            {
                float2 lp = rw - center;
                float r = length(lp);
                float housing = aaMask(r - radius);
                float inner = aaMask(r - radius * 0.86);
                float a = atan2(lp.y, lp.x) + t * _FanSpeed;
                float blade = step(0.5, frac(a * 4.0 / 6.28318));
                float hub = aaMask(r - radius * 0.18);
                col = lerp(col, _FrameColor.rgb, housing);
                col = lerp(col, lerp(_FanDark.rgb, _FanLight.rgb, blade), inner);
                col = lerp(col, _FrameColor.rgb, hub);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;
                float3 n = normalize(IN.normalOS);
                float3 ax = abs(n);
                float3 scl = IN.scale;
                float3 pOS = IN.positionOS;

                float isRoof = step(0.5, n.y);
                float isBottom = step(0.5, -n.y);
                float isXFace = step(ax.z, ax.x);

                // =====================================================
                // FACADE (side faces)
                // =====================================================
                float hDir = lerp(n.z >= 0.0 ? 1.0 : -1.0, n.x >= 0.0 ? 1.0 : -1.0, isXFace);
                float hPos = lerp(-pOS.x, pOS.z, isXFace);
                float h = hPos * hDir;                       // left-to-right when seen from outside

                float faceW = lerp(scl.x, scl.z, isXFace);
                float faceIdx = isXFace > 0.5 ? (n.x > 0.0 ? 2.0 : 3.0) : (n.z > 0.0 ? 0.0 : 1.0);
                float faceU = h + 0.5;
                float vWorld = (pOS.y + 0.5) * scl.y;

                // bays and floors always fit the cube exactly, at any scale
                float bays = max(1.0, round(faceW / _BayWidth));
                float floors = max(1.0, round(scl.y / _FloorHeight));
                float uu = faceU * bays;
                float bayId = floor(min(uu, bays - 0.001));
                float inBay = uu - bayId;
                float vv = (pOS.y + 0.5) * floors;
                float floorId = floor(min(vv, floors - 0.001));
                float inFloor = vv - floorId;
                float bayW = faceW / bays;
                float floorH = scl.y / floors;
                float lx = (inBay - 0.5) * bayW;             // world units from bay center
                float ly = inFloor * floorH;                 // world units above floor line
                float faceSeed = faceIdx * 13.7;

                // door area
                float doorU = (faceU - 0.5) * faceW;
                float doorHalf = _DoorWidth * 0.5;
                float doorOn = (abs(_DoorSide - faceIdx) < 0.5 || abs(_DoorSide - 4.0) < 0.5) ? 1.0 : 0.0;
                doorOn *= (1.0 - isRoof) * (1.0 - isBottom);
                float doorSD = sdBox(float2(doorU, vWorld - _DoorHeight * 0.5),
                                     float2(doorHalf, _DoorHeight * 0.5));
                float zone = doorOn * aaMask(doorSD - _FrameW - 0.12);

                half3 fcol = _WallColor.rgb;
                half3 fEmi = half3(0, 0, 0);

                // accent strips
                float accentBay = step(fmod(bayId, max(_AccentEvery, 1.0)), 0.5);
                float stripeMask = accentBay * aaMask(inBay * bayW - _StripeW);
                fcol = lerp(fcol, _AccentColor.rgb, stripeMask);

                // floor slab lines
                float slab = aaMask(ly - _SlabH);
                fcol = lerp(fcol, _WallColor.rgb * _SlabTint.rgb, slab);

                // window shape
                float2 wc = float2(0.0, (_WinBottom + _WinHeight * 0.5) * floorH);
                float2 wh = float2(bayW * _WinWidth * 0.5, floorH * _WinHeight * 0.5);
                float wr = min(_WinRound, min(wh.x, wh.y) * 0.95);
                float2 wp = float2(lx, ly) - wc;
                float wsd = sdRoundBox(wp, wh, wr);
                float winFrame = aaMask(wsd) * (1.0 - zone);
                float winGlass = aaMask(wsd + _FrameW) * (1.0 - zone);

                // sill
                float sillSD = sdBox(float2(lx, ly) - float2(0.0, wc.y - wh.y - _SillH * 0.5),
                                     float2(wh.x + _SillH, _SillH * 0.5));
                float sill = aaMask(sillSD) * (1.0 - zone);
                fcol = lerp(fcol, _SillColor.rgb, sill);
                fcol = lerp(fcol, _FrameColor.rgb, winFrame);

                // glass: gradient, toon reflection stripes, optional lit state, mullion
                float wx = saturate((lx - (wc.x - wh.x)) / max(2.0 * wh.x, 1e-4));
                float wy = saturate((ly - (wc.y - wh.y)) / max(2.0 * wh.y, 1e-4));
                half3 glass = lerp(_GlassDark.rgb, _GlassColor.rgb, wy);
                float d1 = wx - wy * 0.6;
                float s1 = smoothstep(0.30, 0.32, d1) - smoothstep(0.46, 0.48, d1);
                float s2 = smoothstep(0.55, 0.57, d1) - smoothstep(0.62, 0.64, d1);
                float wid = hash21(float2(bayId + faceSeed, floorId * 3.1 + 1.7));
                float isLit = step(wid, _LitAmount);
                float mull = 1.0 - smoothstep(0.02, 0.03, abs(wx - 0.5));
                glass = lerp(glass, _WindowLitColor.rgb, isLit * 0.9);
                glass += (s1 * 0.25 + s2 * 0.15) * (1.0 - isLit * 0.6);
                glass = lerp(glass, _FrameColor.rgb, mull);
                fcol = lerp(fcol, glass, winGlass);
                fEmi += _WindowLitColor.rgb * isLit * (1.0 - mull) * winGlass * _WindowGlow;

                // top trim (cornice) and corner pillars
                float topDist = scl.y - vWorld;
                fcol = lerp(fcol, _AccentColor.rgb, aaMask(topDist - _CorniceH));
                fcol = lerp(fcol, _FrameColor.rgb, aaMask(abs(topDist - (_CorniceH + 0.04)) - 0.025));
                float cornerDist = min(faceU, 1.0 - faceU) * faceW;
                fcol = lerp(fcol, _CornerColor.rgb, aaMask(cornerDist - _CornerW));

                // hazard stripes at the base
                float hazStripe = step(0.5, frac((doorU + vWorld) * _HazardScale));
                half3 hazCol = lerp(_HazardDark.rgb, _HazardYellow.rgb, hazStripe);
                fcol = lerp(fcol, hazCol, aaMask(vWorld - _BaseH) * (1.0 - zone));

                // entrance door
                float doorFrame = doorOn * aaMask(doorSD - _FrameW);
                float doorGlass = doorOn * aaMask(doorSD);
                fcol = lerp(fcol, _FrameColor.rgb, doorFrame);
                float dx = saturate(doorU / max(_DoorWidth, 1e-4) + 0.5);
                float dy = saturate(vWorld / max(_DoorHeight, 1e-4));
                half3 dGlass = lerp(_GlassDark.rgb, _GlassColor.rgb, dy);
                float dd1 = dx - dy * 0.6;
                dGlass += (smoothstep(0.30, 0.32, dd1) - smoothstep(0.42, 0.44, dd1)) * 0.25;
                float split = 1.0 - smoothstep(0.012, 0.02, abs(doorU));
                float bars = (1.0 - smoothstep(0.02, 0.03, abs(abs(doorU) - 0.14)))
                           * (1.0 - smoothstep(0.0, 0.03, abs(vWorld - _DoorHeight * 0.5) - 0.12));
                dGlass = lerp(dGlass, _FrameColor.rgb, saturate(split + bars));
                fcol = lerp(fcol, dGlass, doorGlass);

                // canopy above the door
                float canopySD = sdBox(float2(doorU, vWorld - (_DoorHeight + _FrameW + 0.08)),
                                       float2(doorHalf + 0.3, 0.07));
                fcol = lerp(fcol, _AccentColor.rgb, doorOn * aaMask(canopySD));

                // lab sign with a flask icon
                float signOn = doorOn * step(0.01, _SignSize);
                float cy = _DoorHeight + _FrameW + 0.16 + _SignSize * 0.5 + 0.06;
                float2 sp = float2(doorU, vWorld - cy);
                float boardSD = sdRoundBox(sp, float2(_SignSize * 0.85, _SignSize * 0.5), 0.06);
                float board = signOn * aaMask(boardSD);
                fcol = lerp(fcol, _SignColor.rgb, board);

                float2 fp = sp / max(_SignSize * 0.5, 1e-4);
                float neckSD = sdBox(fp - float2(0.0, 0.42), float2(0.13, 0.30));
                float lipSD = sdBox(fp - float2(0.0, 0.68), float2(0.20, 0.07));
                float bodySD = max(abs(fp.x) - (0.13 + (0.15 - fp.y) * 0.5), max(fp.y - 0.15, -0.62 - fp.y));
                float flaskSD = min(min(neckSD, lipSD), bodySD);
                float flask = board * aaMask(flaskSD);
                float liquid = board * aaMask(max(bodySD + 0.06, fp.y + 0.2));
                fcol = lerp(fcol, half3(0.90, 0.97, 1.0), flask);
                fcol = lerp(fcol, _LiquidColor.rgb, liquid);
                fEmi += _LiquidColor.rgb * liquid * 0.5;

                // =====================================================
                // ROOF with helipad
                // =====================================================
                float2 rn = pOS.xz;
                float2 rw = rn * scl.xz;                     // world units from roof center
                float minHalf = 0.5 * min(scl.x, scl.z);

                half3 rcol = _RoofColor.rgb;
                half3 rEmi = half3(0, 0, 0);

                // concrete panel lines
                float2 gp = abs(frac(rw / max(_RoofTile, 0.01) + 0.5) - 0.5) * _RoofTile;
                rcol = lerp(rcol, rcol * 0.85, aaMask(min(gp.x, gp.y) - 0.02));

                // parapet around the edge
                float eDist = min((0.5 - abs(rn.x)) * scl.x, (0.5 - abs(rn.y)) * scl.z);
                rcol = lerp(rcol, _ParapetColor.rgb, aaMask(eDist - _ParapetW));
                rcol = lerp(rcol, _RoofColor.rgb * 0.6, aaMask(abs(eDist - (_ParapetW + 0.05)) - 0.03));

                // helipad
                float padR = max(_PadRadius * minHalf, 0.001);
                float pr = radians(_PadRotation);
                float pc = cos(pr);
                float ps = sin(pr);
                float2 hp = float2(rw.x * pc - rw.y * ps, rw.x * ps + rw.y * pc) / padR;
                float dpad = length(hp);

                float padMask = aaMask(dpad - 1.0);
                float ringMask = aaMask(abs(dpad - 0.88) - 0.04);
                float hSD = min(min(sdBox(hp - float2(-0.24, 0.0), float2(0.07, 0.45)),
                                    sdBox(hp - float2(0.24, 0.0), float2(0.07, 0.45))),
                                sdBox(hp, float2(0.24, 0.07)));
                float hMask = aaMask(hSD);
                rcol = lerp(rcol, _PadColor.rgb, padMask);
                rcol = lerp(rcol, _PadRingColor.rgb, ringMask * padMask);
                rcol = lerp(rcol, _PadHColor.rgb, hMask * padMask);

                // blinking landing lights around the pad
                float lightN = max(_PadLights, 3.0);
                float sectorW = 6.28318 / lightN;
                float angP = atan2(hp.y, hp.x);
                float sector = floor(angP / sectorW + 0.5);
                float sa = sector * sectorW;
                float2 lc = float2(cos(sa), sin(sa)) * 1.12;
                float lightMask = aaMask(length(hp - lc) - 0.045);
                float sectorPos = fmod(sector + lightN, lightN);
                float phase = 0.5 + 0.5 * sin(t * _LightSpeed - sectorPos * 0.6);
                float onAmt = smoothstep(0.4, 0.6, phase);
                rcol = lerp(rcol, lerp(_LightOff.rgb, _LightOn.rgb, onAmt), lightMask);
                rEmi += _LightOn.rgb * onAmt * lightMask * _LightGlow;

                // spinning roof fans in two corners
                if (_FanRadius > 0.01)
                {
                    float2 fc1 = float2(0.37 * scl.x, 0.37 * scl.z);
                    fanLayer(rw, fc1, _FanRadius, t, rcol);
                    fanLayer(rw, -fc1, _FanRadius, t, rcol);
                }

                // =====================================================
                // Combine faces
                // =====================================================
                half3 albedo = lerp(fcol, rcol, isRoof);
                half3 emi = lerp(fEmi, rEmi, isRoof);
                albedo = lerp(albedo, _FrameColor.rgb * 0.7, isBottom);
                emi *= (1.0 - isBottom);

                // toon edge lines along the cube edges
                float3 e = (0.5 - abs(pOS)) * scl + ax * 100.0;
                float nearestEdge = min(e.x, min(e.y, e.z));
                albedo = lerp(albedo, _OutlineColor.rgb, aaMask(nearestEdge - _OutlineWidth));

                // =====================================================
                // Toon lighting + received shadows
                // =====================================================
                float3 normalWS = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndl = dot(normalWS, mainLight.direction);
                float steps = max(_LightSteps, 1.0);
                float litAmt = floor(saturate(ndl) * steps + 0.5) / steps;
                float shadowToon = smoothstep(0.5 - _ShadowSoftness, 0.5 + _ShadowSoftness, mainLight.shadowAttenuation);
                float light = litAmt * shadowToon;

                half3 col = albedo * lerp(_ShadowTint.rgb, half3(1, 1, 1), light);
                col *= lerp(half3(1, 1, 1), mainLight.color, _LightInfluence);
                col += emi;

                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE * positionCS.w);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE * positionCS.w);
                #endif

                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
