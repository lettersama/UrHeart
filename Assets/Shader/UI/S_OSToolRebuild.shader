Shader "UI/OS/ToolRebuild"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (0.67,0.76,0.7,1)
        _Progress ("Rebuild Progress", Range(0,1)) = 0
        _Phase ("Animation Time", Float) = 0
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 local : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            fixed4 _Color;
            float4 _ClipRect;
            float _Progress;
            float _Phase;
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.local = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float y = 1.0 - i.uv.y;
                float cover = smoothstep(y * 0.24, y * 0.24 + 0.12, _Progress);
                float reveal = smoothstep(0.56 + y * 0.24, 0.7 + y * 0.3, _Progress);
                float envelope = saturate(cover - reveal);
                float2 grid = i.uv * float2(70,32);
                float2 cell = floor(grid);
                float2 pixel = floor(frac(grid) * float2(4,6));
                float tick = floor(_Phase * 12);
                float glyph = step(0.56, hash(cell + tick)) * step(0.48, hash(pixel + cell * 5 + tick));
                glyph *= (1 - step(3, pixel.x)) * (1 - step(5, pixel.y));
                float scan = 1 - smoothstep(0, 0.025, abs(y - frac(_Phase * 0.65)));
                float rows = lerp(0.72, 1, step(0.3, frac(i.uv.y * 240)));
                fixed4 color = fixed4(i.color.rgb * (0.025 + glyph * 0.38 + scan * 0.25) * rows, envelope * i.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.local.xy, _ClipRect);
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
