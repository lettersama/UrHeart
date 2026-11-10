Shader "MrTGame/UI/PixelHeartbeat"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _WaveTex ("Wave Data", 2D) = "gray" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _BackgroundColor ("Background Color", Color) = (0, 0, 0, 1)
        [HDR] _CoreColor ("Core Color", Color) = (1.4, 0.5, 0.25, 1)
        [HDR] _CoreTransitionColor ("Core Transition Color", Color) = (1.05, 0.35, 0.156, 1)
        [HDR] _GlowColor ("Glow Color", Color) = (0.8, 0.1, 0.025, 1)
        [HDR] _HighlightCoreColor ("Highlight Core Color", Color) = (2.1, 1.05, 0.35, 1)
        [HDR] _HighlightTransitionColor ("Highlight Transition Color", Color) = (1.575, 0.759, 0.215, 1)
        [HDR] _HighlightGlowColor ("Highlight Glow Color", Color) = (0.55, 0.95, 0.28, 1)
        _GridColor ("Grid Color", Color) = (0.05, 0.28, 0.2, 0.2)

        _VirtualSize ("Virtual Pixel Size", Vector) = (120, 40, 0, 0)
        _GridStep ("Grid Step", Range(2, 16)) = 8
        _GridSweepCenter ("Grid Sweep Center", Float) = -2
        _GridSweepWidth ("Grid Sweep Width", Range(2, 40)) = 25
        _GridSweepTilt ("Grid Sweep Tilt", Range(-45, 45)) = 20
        _CoreWidth ("Core Width", Range(0.1, 2)) = 0.55
        _InnerWidth ("Inner Glow Width", Range(1, 4)) = 1.6
        _OuterWidth ("Outer Glow Width", Range(2, 8)) = 3.6
        _InnerStrength ("Inner Glow Strength", Range(0, 1)) = 0.35
        _OuterStrength ("Outer Glow Strength", Range(0, 1)) = 0.12
        _HighlightHaloWidth ("Highlight Halo Width", Range(2, 12)) = 6
        _HighlightHaloStrength ("Highlight Halo Strength", Range(0, 1)) = 0.2

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "PixelHeartbeat"

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler2D _WaveTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half4 _BackgroundColor;
            half4 _CoreColor;
            half4 _CoreTransitionColor;
            half4 _GlowColor;
            half4 _HighlightCoreColor;
            half4 _HighlightTransitionColor;
            half4 _HighlightGlowColor;
            half4 _GridColor;
            float4 _VirtualSize;
            half _GridStep;
            float _GridSweepCenter;
            half _GridSweepWidth;
            half _GridSweepTilt;
            half _CoreWidth;
            half _InnerWidth;
            half _OuterWidth;
            half _InnerStrength;
            half _OuterStrength;
            half _HighlightHaloWidth;
            half _HighlightHaloStrength;
            float4 _ClipRect;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            half3 SampleHeartbeat(float column, float width)
            {
                float sampleU = (clamp(column, 0.0, width - 1.0) + 0.5) / width;
                return tex2D(_WaveTex, float2(sampleU, 0.5)).rgb;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float2 virtualSize = max(floor(_VirtualSize.xy + 0.5), 1.0);
                float2 pixelIndex = floor(input.uv * virtualSize);
                float2 pixelCenter = pixelIndex + 0.5;

                half3 currentSample = SampleHeartbeat(pixelIndex.x, virtualSize.x);
                half3 previousSample = SampleHeartbeat(pixelIndex.x - 1.0, virtualSize.x);
                half3 previous2Sample = SampleHeartbeat(pixelIndex.x - 2.0, virtualSize.x);
                half3 nextSample = SampleHeartbeat(pixelIndex.x + 1.0, virtualSize.x);
                float currentY = floor(currentSample.r * (virtualSize.y - 1.0) + 0.5) + 0.5;
                float previousY = floor(previousSample.r * (virtualSize.y - 1.0) + 0.5) + 0.5;
                previousY = lerp(previousY, currentY, step(0.5h, currentSample.b));

                float lowY = min(previousY, currentY);
                float highY = max(previousY, currentY);
                float distanceToWave = max(max(lowY - pixelCenter.y, pixelCenter.y - highY), 0.0);

                half core = 1.0h - step(_CoreWidth, distanceToWave);
                half innerGlow = (1.0h - step(_InnerWidth, distanceToWave)) * _InnerStrength;
                half outerGlow = (1.0h - step(_OuterWidth, distanceToWave)) * _OuterStrength;

                half highlight = max(currentSample.g, previousSample.g);
                half highlightEdge1 = max(
                    previous2Sample.g,
                    nextSample.g) * 0.55h;
                half highlightEdge2 = max(
                    SampleHeartbeat(pixelIndex.x - 3.0, virtualSize.x).g,
                    SampleHeartbeat(pixelIndex.x + 2.0, virtualSize.x).g) * 0.2h;
                highlight = max(highlight, max(highlightEdge1, highlightEdge2));

                float gridStep = max(floor(_GridStep + 0.5), 1.0);
                half gridX = 1.0h - step(1.0, fmod(pixelIndex.x, gridStep));
                half gridY = 1.0h - step(1.0, fmod(pixelIndex.y, gridStep));
                half gridLine = max(gridX, gridY);

                float sweepCenterPixel = _GridSweepCenter * virtualSize.x;
                float sweepSlope = tan(_GridSweepTilt * 0.01745329252h);
                float tiltedPixelX = pixelCenter.x
                    - (pixelCenter.y - virtualSize.y * 0.5) * sweepSlope;
                float sweepDistance = abs(tiltedPixelX - sweepCenterPixel);
                half sweepOuter = 1.0h - step(_GridSweepWidth, sweepDistance);
                half sweepMiddle = 1.0h - step(_GridSweepWidth * 0.65h, sweepDistance);
                half sweepInner = 1.0h - step(_GridSweepWidth * 0.3h, sweepDistance);
                half gridReveal = sweepOuter * 0.25h
                    + sweepMiddle * 0.35h
                    + sweepInner * 0.4h;

                half visibleGrid = gridLine * gridReveal * _GridColor.a;
                half3 coreColor = lerp(
                    _CoreColor.rgb * _CoreColor.a,
                    _HighlightCoreColor.rgb * _HighlightCoreColor.a,
                    highlight);
                half3 transitionColor = lerp(
                    _CoreTransitionColor.rgb * _CoreTransitionColor.a,
                    _HighlightTransitionColor.rgb * _HighlightTransitionColor.a,
                    highlight);
                half3 glowColor = lerp(_GlowColor.rgb, _HighlightGlowColor.rgb, highlight);

                float incomingDelta = currentY - previousY;
                half incomingVertical = step(0.5h, abs(incomingDelta));
                half currentEndpoint = 1.0h - step(0.5h, abs(pixelCenter.y - currentY));
                half transitionPixel = core
                    * incomingVertical
                    * (1.0h - currentEndpoint);
                half3 pixelCoreColor = lerp(coreColor, transitionColor, transitionPixel);

                half highlightHalo = (1.0h - step(_HighlightHaloWidth, distanceToWave))
                    * _HighlightHaloStrength
                    * highlight;

                half3 rgb = _BackgroundColor.rgb
                    + _GridColor.rgb * visibleGrid
                    + glowColor * (innerGlow + outerGlow + highlightHalo)
                    + pixelCoreColor * core;

                half textureAlpha = tex2D(_MainTex, input.uv).a;
                half visibility = textureAlpha * input.color.a * _BackgroundColor.a;

                #ifdef UNITY_UI_CLIP_RECT
                    visibility *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                rgb *= input.color.rgb;

                #ifdef UNITY_UI_ALPHACLIP
                    clip(visibility - 0.001h);
                #endif

                return half4(rgb, visibility);
            }
            ENDCG
        }
    }

    Fallback Off
}
