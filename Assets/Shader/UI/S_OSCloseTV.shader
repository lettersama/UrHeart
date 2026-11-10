Shader "UI/OS/CloseTV"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Progress ("Close Progress", Range(0,1)) = 0
        _GlowColor ("Glow Color", Color) = (0.64,0.70,0.72,1)
        _GlowIntensity ("Glow Intensity", Range(0,2)) = 0.62
        _JitterPixels ("Jitter Pixels", Range(0,2)) = 0.75
        _ReferenceResolution ("Reference Resolution", Vector) = (640,360,0,0)
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
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
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            fixed4 _GlowColor;
            float4 _ClipRect;
            float _Progress;
            float _GlowIntensity;
            float _JitterPixels;
            float4 _ReferenceResolution;

            float Hash11(float value)
            {
                return frac(sin(value * 127.1) * 43758.5453);
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
                float progress = saturate(_Progress);
                float verticalPhase = smoothstep(0.02, 0.58, progress);
                float horizontalPhase = smoothstep(0.50, 0.88, progress);
                float finishPhase = smoothstep(0.86, 1.0, progress);
                float effectStarted = smoothstep(0.0, 0.04, progress);

                float2 resolution = max(
                    float2(1.0, 1.0),
                    _ReferenceResolution.xy);
                float2 pixel = min(
                    floor(saturate(input.texcoord) * resolution),
                    resolution - 1.0);
                float2 centerPixel = (resolution - 1.0) * 0.5;

                float tick = floor(_Time.y * 30.0);
                float jitterGate = effectStarted * (1.0 - finishPhase);
                float2 jitter = float2(
                    floor(Hash11(tick + 3.7) * 3.0) - 1.0,
                    floor(Hash11(tick + 9.2) * 3.0) - 1.0);
                jitter *= _JitterPixels * jitterGate;

                float2 centeredPixel = pixel - centerPixel - jitter;
                float2 distanceToCenter = abs(centeredPixel);
                float halfHeight = floor(lerp(
                    resolution.y * 0.5 + 1.0,
                    0.75,
                    verticalPhase) + 0.5);
                float halfWidth = floor(lerp(
                    resolution.x * 0.5 + 1.0,
                    0.75,
                    horizontalPhase) + 0.5);

                float insideX = 1.0 - step(
                    halfWidth + 0.5,
                    distanceToCenter.x);
                float insideY = 1.0 - step(
                    halfHeight + 0.5,
                    distanceToCenter.y);
                float aperture = insideX * insideY * (1.0 - finishPhase);
                float blackAlpha = (1.0 - aperture) * effectStarted;

                float edgeDistance = abs(distanceToCenter.y - halfHeight);
                float edgeCore = 1.0 - step(0.8, edgeDistance);
                float edgeHalo = 1.0 - step(2.8, edgeDistance);
                float closingEdges = max(
                    edgeCore * 0.54,
                    edgeHalo * 0.14);
                closingEdges *= insideX
                    * verticalPhase
                    * (1.0 - finishPhase);

                float lineCore = 1.0 - step(
                    0.8,
                    distanceToCenter.y);
                float lineHalo = 1.0 - step(
                    2.8,
                    distanceToCenter.y);
                float centerLine = max(
                    lineCore * 0.58,
                    lineHalo * 0.15);
                centerLine *= insideX * smoothstep(0.40, 0.68, progress);

                float dotDistance = distanceToCenter.x
                    + distanceToCenter.y;
                float dotCore = 1.0 - step(1.6, dotDistance);
                float dotHalo = 1.0 - step(4.2, dotDistance);
                float dotGlow = max(
                    dotCore * 0.56,
                    dotHalo * 0.12);
                dotGlow *= smoothstep(0.72, 0.90, progress);

                float alternateRow = step(
                    0.25,
                    frac(pixel.y * 0.5));
                float pixelStripe = lerp(0.86, 1.0, alternateRow);
                float beamFadeIn = smoothstep(0.08, 0.24, progress);
                float beamFadeOut = 1.0 - finishPhase;
                float glow = max(closingEdges, max(centerLine, dotGlow));
                glow *= pixelStripe * beamFadeIn * beamFadeOut;

                fixed4 color;
                color.rgb = _GlowColor.rgb * glow * _GlowIntensity;
                color.a = max(
                    blackAlpha,
                    glow * _GlowColor.a * 0.72);
                color.a *= input.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(
                    input.worldPosition.xy,
                    _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
