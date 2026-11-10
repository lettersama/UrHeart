// Modal-only TMP coverage: retain the project's font atlases and metrics,
// sample on the same 360-line logical grid as OSDitherFill, then use a hard
// glyph edge. Existing Canvas/vertex fades use the modal's screen-wide Bayer grid.
Shader "TextMeshPro/OS/Chapter03 Modal Pixels"
{
    Properties
    {
        _FaceColor ("Face Color", Color) = (1,1,1,1)
        _FaceDilate ("Face Dilate", Range(-1,1)) = 0
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width", Range(0,1)) = 0
        _OutlineSoftness ("Outline Softness", Range(0,1)) = 0
        _WeightNormal ("Weight Normal", Float) = 0
        _WeightBold ("Weight Bold", Float) = 0.5
        _ShaderFlags ("Flags", Float) = 0
        _ScaleRatioA ("Scale Ratio A", Float) = 1
        _ScaleRatioB ("Scale Ratio B", Float) = 1
        _ScaleRatioC ("Scale Ratio C", Float) = 1
        _MainTex ("Font Atlas", 2D) = "white" {}
        _TextureWidth ("Texture Width", Float) = 512
        _TextureHeight ("Texture Height", Float) = 512
        _GradientScale ("Gradient Scale", Float) = 5
        _ScaleX ("Scale X", Float) = 1
        _ScaleY ("Scale Y", Float) = 1
        _PerspectiveFilter ("Perspective Filter", Range(0,1)) = 0.875
        _Sharpness ("Sharpness", Range(-1,1)) = 0
        _VertexOffsetX ("Vertex Offset X", Float) = 0
        _VertexOffsetY ("Vertex Offset Y", Float) = 0
        _ClipRect ("Clip Rect", Vector) = (-32767,-32767,32767,32767)
        _MaskSoftnessX ("Mask Softness X", Float) = 0
        _MaskSoftnessY ("Mask Softness Y", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _CullMode ("Cull Mode", Float) = 0
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull [_CullMode]
        ZWrite Off
        Lighting Off
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
            fixed4 _FaceColor;
            float _WeightNormal, _WeightBold, _FaceDilate, _ScaleRatioA;
            float _VertexOffsetX, _VertexOffsetY;
            float4 _ClipRect;
            int _UIVertexColorAlwaysGammaSpace;
            struct AppData
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float4 uv : TEXCOORD0;
            };
            struct Varyings
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 localPosition : TEXCOORD1;
                float threshold : TEXCOORD2;
            };
            Varyings Vert(AppData input)
            {
                Varyings output;
                UNITY_INITIALIZE_OUTPUT(Varyings, output);
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                input.vertex.xy += float2(_VertexOffsetX, _VertexOffsetY);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.localPosition = input.vertex.xy;
                output.uv = input.uv.xy;
                float bold = step(input.uv.w, 0);
                float weight = lerp(_WeightNormal, _WeightBold, bold) / 4.0;
                output.threshold = 0.5 - (weight + _FaceDilate) * _ScaleRatioA * 0.5;
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace()) input.color.rgb = UIGammaToLinear(input.color.rgb);
                output.color = input.color * _FaceColor;
                return output;
            }
            fixed4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float cell = max(1.0, _ScreenParams.y / 360.0);
                float2 offset = (floor(input.vertex.xy / cell) + 0.5) * cell - input.vertex.xy;
                float2 uv = input.uv + ddx(input.uv) * offset.x + ddy(input.uv) * offset.y;
                half coverage = step(input.threshold, tex2D(_MainTex, uv).a);
                #ifdef UNITY_UI_CLIP_RECT
                    coverage *= UnityGet2DClipping(input.localPosition, _ClipRect);
                #endif
                clip(coverage - 0.5);
                clip(input.color.a - Chapter03BayerThreshold(input.vertex.xy));
                return fixed4(input.color.rgb, 1.0);
            }
            ENDCG
        }
    }
}
