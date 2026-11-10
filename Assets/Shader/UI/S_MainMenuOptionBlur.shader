Shader "UI/MainMenu/OptionLiveBlur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Live Main Menu", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BlurRadius ("Blur Radius", Range(0,8)) = 2
        _Darkness ("Darkness", Range(0,1)) = 0.1
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
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float4 _ClipRect;
            float _BlurRadius;
            float _Darkness;

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
                float2 offset = _MainTex_TexelSize.xy * _BlurRadius;
                fixed4 color = tex2D(_MainTex, input.texcoord) * 0.20;
                color += tex2D(_MainTex, input.texcoord + float2(offset.x, 0)) * 0.12;
                color += tex2D(_MainTex, input.texcoord - float2(offset.x, 0)) * 0.12;
                color += tex2D(_MainTex, input.texcoord + float2(0, offset.y)) * 0.12;
                color += tex2D(_MainTex, input.texcoord - float2(0, offset.y)) * 0.12;
                color += tex2D(_MainTex, input.texcoord + offset) * 0.08;
                color += tex2D(_MainTex, input.texcoord - offset) * 0.08;
                color += tex2D(_MainTex, input.texcoord + float2(offset.x, -offset.y)) * 0.08;
                color += tex2D(_MainTex, input.texcoord + float2(-offset.x, offset.y)) * 0.08;

                color *= input.color;
                color.rgb *= 1.0 - saturate(_Darkness);

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
