Shader "UI/HeartHealStateLiquid"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FillAmount ("Fill Amount", Range(0,1)) = 1
        _SpriteUvRect ("Sprite UV Rect", Vector) = (0,0,1,1)
        _WaveAmplitude ("Wave Amplitude", Range(0,0.08)) = 0.022
        _WaveFrequency ("Wave Frequency", Range(0.25,4)) = 0.5
        _WaveSpeed ("Wave Speed", Range(0,6)) = 2.24
        _DarkWaveDepth ("Dark Wave Depth", Range(0.002,0.08)) = 0.018
        _DarkWaveWidth ("Dark Wave Width", Range(0.002,0.08)) = 0.026
        _DarkWaveOpacity ("Dark Wave Opacity", Range(0,1)) = 0.42
        [HideInInspector] _WaveTime ("Wave Time", Float) = 0
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
            fixed4 _TextureSampleAdd;
            fixed4 _Color;
            float _FillAmount;
            float4 _SpriteUvRect;
            float _WaveAmplitude;
            float _WaveFrequency;
            float _WaveSpeed;
            float _DarkWaveDepth;
            float _DarkWaveWidth;
            float _DarkWaveOpacity;
            float _WaveTime;
            float4 _ClipRect;

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

            float InteriorWaveBand(
                float horizontalUv,
                float boundary,
                float width,
                float antialias)
            {
                float insideDistance = boundary - horizontalUv;
                float insideMask = smoothstep(
                    -antialias,
                    antialias,
                    insideDistance);
                float depthMask = 1.0 - smoothstep(
                    width,
                    width + antialias,
                    insideDistance);
                return insideMask * depthMask;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 spriteSize = max(
                    _SpriteUvRect.zw,
                    float2(0.0001, 0.0001));
                float2 localUv = saturate(
                    (input.texcoord - _SpriteUvRect.xy) / spriteSize);
                float fillAmount = saturate(_FillAmount);
                float edgeAvailability = smoothstep(
                        0.0,
                        0.06,
                        fillAmount)
                    * smoothstep(0.0, 0.06, 1.0 - fillAmount);
                float verticalPhase = localUv.y
                    * 6.2831853
                    * max(0.01, _WaveFrequency);
                float timePhase = _WaveTime * _WaveSpeed;
                float frontWave = sin(verticalPhase + timePhase);
                float darkWave = sin(
                    verticalPhase + timePhase + 1.05);
                float frontBoundary = saturate(
                    fillAmount
                    + frontWave * _WaveAmplitude * edgeAvailability);
                float darkBoundary = saturate(
                    fillAmount
                    - _DarkWaveDepth
                    + darkWave
                        * _WaveAmplitude
                        * 0.75
                        * edgeAvailability);
                float antialias = max(fwidth(localUv.x) * 0.75, 0.0005);
                float fillMask = 1.0 - smoothstep(
                    frontBoundary - antialias,
                    frontBoundary + antialias,
                    localUv.x);
                fillMask = lerp(
                    fillMask,
                    1.0,
                    step(0.9999, fillAmount));
                fillMask *= step(0.0001, fillAmount);

                float darkLayer = InteriorWaveBand(
                        localUv.x,
                        darkBoundary,
                        max(0.002, _DarkWaveWidth),
                        antialias)
                    * fillMask
                    * edgeAvailability;

                fixed4 spriteSample =
                    tex2D(_MainTex, input.texcoord)
                    + _TextureSampleAdd;
                fixed4 color = fixed4(
                    input.color.rgb,
                    spriteSample.a * input.color.a);
                float spriteFillAlpha = color.a * fillMask;
                float liquidSurfaceStart = darkBoundary
                    - max(0.002, _DarkWaveWidth);
                float liquidSurfaceEnvelope = smoothstep(
                        liquidSurfaceStart - antialias,
                        liquidSurfaceStart + antialias,
                        localUv.x)
                    * fillMask
                    * edgeAvailability;
                color.rgb = lerp(
                    color.rgb,
                    input.color.rgb,
                    liquidSurfaceEnvelope);
                color.rgb = lerp(
                    color.rgb,
                    input.color.rgb * 0.5,
                    darkLayer * _DarkWaveOpacity);
                color.a = max(
                    spriteFillAlpha,
                    input.color.a * liquidSurfaceEnvelope);

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
