Shader "UI/StateCheckErrorBlocks"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ErrorColor ("Error Color", Color) = (0.7411765,0.1215686,0.1058824,1)
        _DarkColor ("Dark Block Color", Color) = (0,0,0,1)
        _GridSize ("Block Grid", Vector) = (8,6,0,0)
        _GlitchRate ("Glitch Rate", Range(1,30)) = 10
        _GlitchAmount ("Glitch Amount", Range(0,1)) = 0.78
        _LocalRect ("Local Rect", Vector) = (0,0,1,1)
        _SpriteUvRect ("Sprite UV Rect", Vector) = (0,0,1,1)
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
                float2 localUv : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            fixed4 _ErrorColor;
            fixed4 _DarkColor;
            float4 _ClipRect;
            float4 _GridSize;
            float4 _LocalRect;
            float4 _SpriteUvRect;
            float _GlitchRate;
            float _GlitchAmount;

            float Hash21(float2 value)
            {
                return frac(sin(dot(
                    value,
                    float2(12.9898, 78.233))) * 43758.5453);
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(output.worldPosition);
                output.texcoord = input.texcoord;
                output.localUv =
                    (input.vertex.xy - _LocalRect.xy) * _LocalRect.zw;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 sample =
                    tex2D(_MainTex, input.texcoord) + _TextureSampleAdd;
                float2 localUv = saturate(
                    (input.texcoord - _SpriteUvRect.xy)
                    / max(
                        _SpriteUvRect.zw,
                        float2(0.000001, 0.000001)));
                float2 gridSize = max(
                    float2(1.0, 1.0),
                    floor(_GridSize.xy + 0.5));
                float2 cell = min(
                    floor(localUv * gridSize),
                    gridSize - 1.0);
                float tick = floor(
                    _Time.y * max(1.0, _GlitchRate));
                float burstNoise = Hash21(float2(
                    tick * 0.73 + 4.1,
                    tick * 1.91 + 2.7));
                float burst = lerp(
                    0.35,
                    1.0,
                    step(0.38, burstNoise));
                float blockNoise = Hash21(
                    cell + float2(tick * 1.37, tick * 2.11));
                float secondaryNoise = Hash21(
                    cell * float2(2.3, 3.7)
                    + float2(tick * 3.17, tick * 0.83));
                float threshold = 1.0
                    - saturate(_GlitchAmount)
                    * lerp(0.32, 0.7, burst);
                float activeBlock = step(threshold, blockNoise);
                float dropout = activeBlock
                    * step(0.84, secondaryNoise);

                float baseShade = lerp(
                    0.82,
                    1.0,
                    burstNoise);
                float blockShade = lerp(
                    0.03,
                    0.46,
                    step(0.5, secondaryNoise));
                float shade = saturate(lerp(
                    baseShade,
                    blockShade,
                    activeBlock));
                fixed4 color;
                color.rgb = lerp(
                    _DarkColor.rgb,
                    _ErrorColor.rgb,
                    shade);
                float spriteAlpha = sample.a * input.color.a
                    * lerp(1.0, 0.2, dropout);
                float ghostBlockAlpha = activeBlock
                    * lerp(0.16, 0.48, secondaryNoise)
                    * saturate(_GlitchAmount)
                    * input.color.a;
                color.a = max(spriteAlpha, ghostBlockAlpha);

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
