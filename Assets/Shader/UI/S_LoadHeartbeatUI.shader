Shader "MrTGame/UI/LoadHeartbeat"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [NoScaleOffset] _CellularTex ("Cellular Birth Map", 2D) = "black" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _BaseWaveColor ("Unloaded Wave Color", Color) = (0.20, 0.20, 0.20, 1)
        _WaveColor ("Loaded Wave Color", Color) = (0.78, 0.78, 0.78, 1)
        _Progress ("Reveal Progress", Range(0, 1)) = 0
        _RectSize ("Rect Size", Vector) = (100, 30, 0, 0)

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
            Name "LoadHeartbeat"

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
                fixed4 color : COLOR;
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
            sampler2D _CellularTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half4 _BaseWaveColor;
            half4 _WaveColor;
            float _Progress;
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

            fixed4 Frag(Varyings input) : SV_Target
            {
                half4 cellularData = tex2D(_CellularTex, input.uv);
                half waveMask = step(0.5h, cellularData.r);
                half birthThreshold = max(cellularData.g, 1.0h / 255.0h);
                half reveal = step(birthThreshold, saturate(_Progress));
                half spriteAlpha = tex2D(_MainTex, input.uv).a;
                half clipAlpha = 1.0;

                #ifdef UNITY_UI_CLIP_RECT
                clipAlpha = UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                half alpha = lerp(
                        _BaseWaveColor.a,
                        _WaveColor.a,
                        reveal)
                    * waveMask
                    * spriteAlpha
                    * input.color.a
                    * clipAlpha;
                half3 color = lerp(
                        _BaseWaveColor.rgb,
                        _WaveColor.rgb,
                        reveal)
                    * input.color.rgb;
                fixed4 outputColor = fixed4(color, alpha);

                #ifdef UNITY_UI_ALPHACLIP
                clip(outputColor.a - 0.001);
                #endif

                return outputColor;
            }
            ENDCG
        }
    }
}
