Shader "UI/MainMenu/WindowRain"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (0.58,0.76,0.92,0.55)
        _ReferenceResolution ("Reference Resolution", Vector) = (250,202,0,0)
        _PixelSize ("Pixel Size", Range(1,4)) = 1

        _BarCenterY ("Horizontal Bar Center Y", Range(0,202)) = 108
        _BarGap ("Horizontal Bar Gap", Range(0,48)) = 18

        _LaneWidth ("Lane Width", Range(3,40)) = 14
        _DropLength ("Drop Length", Range(1,40)) = 17
        _DropWidth ("Drop Width", Range(1,6)) = 1
        _FallSpeed ("Fall Speed", Range(1,320)) = 87
        _SpeedVariation ("Speed Variation", Range(0,0.5)) = 0.25
        _Pinning ("Pinning Hold Fraction", Range(0,0.65)) = 0.35
        _Density ("Moving Drop Density", Range(0,1)) = 0.64
        _Breakup ("Moving Drop Breakup", Range(0,0.8)) = 0.18

        _TraceHold ("Wet Trace Hold Seconds", Range(0,2)) = 0.9
        _TraceFade ("Wet Trace Fade Seconds", Range(0.1,5)) = 3
        _TraceIntensity ("Wet Trace Intensity", Range(0,1)) = 0.16

        _StaticSpacing ("Static Droplet Spacing", Range(4,32)) = 10
        _StaticDensity ("Static Droplet Density", Range(0,0.5)) = 0.13
        _StaticIntensity ("Static Droplet Intensity", Range(0,1)) = 0.18
        _GlintIntensity ("Pearl Glint Intensity", Range(0,2)) = 0.5
        _ShortStreakDensity ("Short Trace Density", Range(0,0.3)) = 0.045
        _ShortStreakLength ("Short Trace Length", Range(2,16)) = 6

        _Intensity ("Overall Intensity", Range(0,4)) = 1.2
        _Seed ("Seed", Float) = 17

        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 1
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend [_SrcBlend] [_DstBlend]
        ColorMask RGB

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _ReferenceResolution;
            float _PixelSize;
            float _BarCenterY;
            float _BarGap;
            float _LaneWidth;
            float _DropLength;
            float _DropWidth;
            float _FallSpeed;
            float _SpeedVariation;
            float _Pinning;
            float _Density;
            float _Breakup;
            float _TraceHold;
            float _TraceFade;
            float _TraceIntensity;
            float _StaticSpacing;
            float _StaticDensity;
            float _StaticIntensity;
            float _GlintIntensity;
            float _ShortStreakDensity;
            float _ShortStreakLength;
            float _Intensity;
            float _Seed;

            float Hash11(float value)
            {
                value = frac(value * 0.1031);
                value *= value + 33.33;
                value *= value + value;
                return frac(value);
            }

            float Hash21(float2 value)
            {
                float3 value3 = frac(
                    float3(value.x, value.y, value.x)
                    * float3(0.1031, 0.1030, 0.0973));
                value3 += dot(value3, value3.yzx + 33.33);
                return frac((value3.x + value3.y) * value3.z);
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(output.worldPosition);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 resolution = max(
                    _ReferenceResolution.xy,
                    float2(1.0, 1.0));
                float pixelSize = max(1.0, floor(_PixelSize + 0.5));
                float2 localUv = saturate(input.texcoord);
                float2 pixel = min(
                    floor(localUv * resolution / pixelSize)
                        * pixelSize + pixelSize * 0.5,
                    resolution - 0.5);

                float barCenter = clamp(
                    _BarCenterY,
                    0.0,
                    resolution.y);
                float halfGap = max(0.0, _BarGap * 0.5);
                float barBottom = clamp(
                    barCenter - halfGap,
                    0.0,
                    resolution.y);
                float barTop = clamp(
                    barCenter + halfGap,
                    0.0,
                    resolution.y);

                float isLowerPane = 1.0 - step(barBottom, pixel.y);
                float isUpperPane = step(barTop, pixel.y);
                float paneMask = saturate(isLowerPane + isUpperPane);
                float paneIndex = isUpperPane;
                float paneBottom = lerp(0.0, barTop, paneIndex);
                float paneTop = lerp(barBottom, resolution.y, paneIndex);
                float paneHeight = max(pixelSize, paneTop - paneBottom);
                float paneY = pixel.y - paneBottom;

                // Moving drops use pane-local Y, so both panes roll independently.
                float laneWidth = max(pixelSize, _LaneWidth);
                float lane = floor(pixel.x / laneWidth);
                float lanePosition = pixel.x - lane * laneWidth;
                float paneSeed = paneIndex * 193.17;
                float laneKey = lane + paneSeed + _Seed * 19.19;
                float laneSeed = Hash11(laneKey);
                float lengthSeed = Hash11(laneKey + 41.73);
                float positionSeed = Hash11(laneKey + 73.31);
                float phaseSeed = Hash11(laneKey + 113.91);
                float gapSeed = Hash11(laneKey + 157.43);
                float speedSeed = Hash11(laneKey + 181.37);
                float pinSeed = Hash11(laneKey + 223.61);

                float dropLength = max(
                    pixelSize,
                    floor(
                        _DropLength * lerp(0.65, 1.35, lengthSeed)
                        / pixelSize) * pixelSize);
                float dropCenter = floor(
                    lerp(
                        pixelSize,
                        max(pixelSize, laneWidth - pixelSize),
                        positionSeed) / pixelSize) * pixelSize
                    + pixelSize * 0.5;

                float speedVariation = saturate(_SpeedVariation);
                float speed = max(1.0, _FallSpeed) * lerp(
                    1.0 - speedVariation,
                    1.0 + speedVariation,
                    speedSeed);
                float holdDistance = speed * max(0.0, _TraceHold);
                float fadeDistance = speed * max(0.1, _TraceFade);
                float respawnGap = lerp(
                    pixelSize * 8.0,
                    max(pixelSize * 8.0, paneHeight * 0.35),
                    gapSeed);
                float cycleDistance = paneHeight + dropLength
                    + holdDistance + fadeDistance + respawnGap;
                float rawTravelled = frac(
                    _Time.y * speed / cycleDistance + phaseSeed)
                    * cycleDistance;
                float movingDistance = paneHeight + dropLength;
                float pinCount = floor(lerp(2.0, 5.0, pinSeed));
                float pinPosition = saturate(
                    rawTravelled / max(pixelSize, movingDistance))
                    * pinCount;
                float pinIndex = floor(min(
                    pinPosition,
                    pinCount - 0.0001));
                float pinPhase = frac(pinPosition);
                float slidePhase = saturate(
                    (pinPhase - saturate(_Pinning))
                    / max(0.001, 1.0 - saturate(_Pinning)));
                slidePhase = slidePhase * slidePhase
                    * (3.0 - 2.0 * slidePhase);
                float pinnedTravelled = (
                    pinIndex + slidePhase) / pinCount
                    * movingDistance;
                float travelled = rawTravelled < movingDistance
                    ? pinnedTravelled
                    : rawTravelled;
                float headY = paneHeight - travelled;
                float trail = paneY - headY;

                float halfWidth = max(
                    pixelSize * 0.55,
                    _DropWidth * 0.5);
                float insideX = step(
                    abs(lanePosition - dropCenter),
                    halfWidth);
                float insideY = step(0.0, trail)
                    * step(trail, dropLength);
                float laneEnabled = step(
                    laneSeed,
                    saturate(_Density));

                float movingSegmentSize = max(pixelSize * 2.0, 1.0);
                float movingSegment = floor(
                    max(trail, 0.0) / movingSegmentSize);
                float movingNoise = Hash21(float2(
                    laneKey * 0.37,
                    movingSegment + paneSeed));
                float connectedHead = 1.0
                    - step(pixelSize * 2.0, trail);
                float movingConnected = max(
                    connectedHead,
                    step(saturate(_Breakup), movingNoise));
                float headFade = saturate(1.0 - trail / dropLength);
                float tailRain = insideX * insideY
                    * movingConnected * lerp(0.22, 0.65, headFade);
                float movingRain = laneEnabled * tailRain;

                // The last passed tail leaves a pane-clipped trace, then fades.
                float traceAgeDistance = travelled
                    - (paneHeight + dropLength - paneY);
                float traceFadeDistance = max(pixelSize, fadeDistance);
                float traceAlive = step(0.0, traceAgeDistance)
                    * saturate(1.0 - max(
                        0.0,
                        traceAgeDistance - holdDistance)
                        / traceFadeDistance);
                float traceHalfWidth = max(
                    pixelSize * 0.55,
                    _DropWidth * 0.4);
                float traceInsideX = step(
                    abs(lanePosition - dropCenter),
                    traceHalfWidth);
                float pathSegment = floor(
                    paneY / max(pixelSize * 3.0, 1.0));
                float pathNoise = Hash21(float2(
                    laneKey + 17.0,
                    pathSegment + paneSeed));
                float traceTexture = lerp(0.4, 1.0, pathNoise)
                    * step(saturate(_Breakup * 0.7), pathNoise);
                float wetTrace = laneEnabled * traceInsideX
                    * traceAlive * traceTexture * _TraceIntensity;

                // Sparse, pane-local droplets and short marks suggest wet glass.
                float staticSpacing = max(
                    pixelSize * 4.0,
                    floor(max(_StaticSpacing, pixelSize * 4.0)
                        / pixelSize) * pixelSize);
                float2 panePixel = float2(pixel.x, paneY);
                float2 staticCell = floor(panePixel / staticSpacing);
                float2 staticOrigin = staticCell * staticSpacing;
                float staticSeed = Hash21(
                    staticCell + float2(
                        _Seed * 0.37 + paneSeed,
                        _Seed * 0.71 + paneSeed));
                float staticXSeed = Hash21(
                    staticCell + float2(23.17 + paneSeed, 11.31));
                float staticYSeed = Hash21(
                    staticCell + float2(41.73, 29.53 + paneSeed));
                float staticSizeSeed = Hash21(
                    staticCell + float2(67.91, 83.17 + paneSeed));
                float usableSpacing = max(
                    pixelSize,
                    staticSpacing - pixelSize);
                float2 staticCenter = staticOrigin + float2(
                    floor(staticXSeed * usableSpacing / pixelSize)
                        * pixelSize + pixelSize * 0.5,
                    floor(staticYSeed * usableSpacing / pixelSize)
                        * pixelSize + pixelSize * 0.5);
                float2 staticDelta = abs(panePixel - staticCenter);
                float staticHalfHeight = pixelSize * lerp(
                    0.55,
                    1.55,
                    step(0.72, staticSizeSeed));
                float microDrop = step(
                        staticDelta.x,
                        pixelSize * 0.55)
                    * step(staticDelta.y, staticHalfHeight)
                    * step(1.0 - saturate(_StaticDensity), staticSeed);
                float glintSeed = Hash21(
                    staticCell + float2(97.13 + paneSeed, 109.37));
                float pearlGlint = microDrop
                    * step(0.88, glintSeed)
                    * step(staticDelta.y, pixelSize * 0.55);

                float streakSeed = Hash21(
                    staticCell + float2(131.71, 149.23 + paneSeed));
                float streakXSeed = Hash21(
                    staticCell + float2(163.19 + paneSeed, 173.51));
                float streakYSeed = Hash21(
                    staticCell + float2(191.81, 211.37 + paneSeed));
                float streakLengthSeed = Hash21(
                    staticCell + float2(227.27 + paneSeed, 239.93));
                float2 streakCenter = staticOrigin + float2(
                    floor(streakXSeed * usableSpacing / pixelSize)
                        * pixelSize + pixelSize * 0.5,
                    floor(streakYSeed * usableSpacing / pixelSize)
                        * pixelSize + pixelSize * 0.5);
                float streakLength = max(
                    pixelSize * 2.0,
                    floor(lerp(
                        pixelSize * 2.0,
                        max(pixelSize * 2.0, _ShortStreakLength),
                        streakLengthSeed) / pixelSize) * pixelSize);
                float2 streakDelta = abs(panePixel - streakCenter);
                float shortStreak = step(
                        streakDelta.x,
                        pixelSize * 0.55)
                    * step(streakDelta.y, streakLength * 0.5)
                    * step(
                        1.0 - saturate(_ShortStreakDensity),
                        streakSeed);

                float glassDetail = microDrop * _StaticIntensity
                    + pearlGlint * _GlintIntensity
                    + shortStreak * _StaticIntensity * 0.7;
                float rain = paneMask
                    * (movingRain + wetTrace + glassDetail);

                fixed textureAlpha = (
                    tex2D(_MainTex, input.texcoord)
                    + _TextureSampleAdd).a;
                rain *= textureAlpha;

                #ifdef UNITY_UI_CLIP_RECT
                rain *= UnityGet2DClipping(
                    input.worldPosition.xy,
                    _ClipRect);
                #endif

                fixed alpha = saturate(rain * input.color.a);

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                fixed colorBoost = _Intensity
                    + pearlGlint * _GlintIntensity * 0.35;
                return fixed4(input.color.rgb * colorBoost, alpha);
            }
            ENDCG
        }
    }
}
