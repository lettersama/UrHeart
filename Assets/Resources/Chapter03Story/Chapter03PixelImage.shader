// Only the chapter 3 paper/keypad UI opts into this material. CanvasGroup
// alpha removes screen-aligned Bayer pixels; authored sprite RGB is unchanged.
Shader "UI/OS/Chapter03 Modal Pixels"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
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
            "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"
            "PreviewType"="Plane" "CanUseSpriteAtlas"="True"
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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "Chapter03Bayer.cginc"

            sampler2D _MainTex;
            fixed4 _Color, _TextureSampleAdd;
            float4 _ClipRect;
            int _UIVertexColorAlwaysGammaSpace;
            struct AppData
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 localPosition : TEXCOORD1;
            };
            Varyings Vert(AppData input)
            {
                Varyings output;
                UNITY_INITIALIZE_OUTPUT(Varyings, output);
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.localPosition = input.vertex.xy;
                output.uv = input.uv;
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace()) input.color.rgb = UIGammaToLinear(input.color.rgb);
                output.color = input.color * _Color;
                return output;
            }
            fixed4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                fixed4 color = (tex2D(_MainTex, input.uv) + _TextureSampleAdd) * input.color;
                #ifdef UNITY_UI_CLIP_RECT
                    color.a *= UnityGet2DClipping(input.localPosition, _ClipRect);
                #endif
                clip(color.a - Chapter03BayerThreshold(input.vertex.xy));
                // Never premultiply RGB by the fading alpha: surviving paper
                // and ink pixels retain their original two-color values.
                return fixed4(color.rgb, 1.0);
            }
            ENDCG
        }
    }
}
