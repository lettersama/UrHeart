Shader "UI/OS/CursorExperimentFocus"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Strength ("Corner opacity", Range(0, 1)) = 0
        _Breath ("Corner reach", Range(0, 1)) = 0.5
        _NativeSize ("Native canvas size", Vector) = (640, 360, 0, 0)
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
            struct v2f { float4 vertex : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 local : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            float _Strength, _Breath;
            float4 _NativeSize, _ClipRect;
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.local = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }
            float Bayer4(float2 p)
            {
                float2 a = fmod(p, 2.0);
                float2 b = fmod(floor(p / 2.0), 2.0);
                float small = a.x * 2.0 + a.y * 3.0 - a.x * a.y * 4.0;
                float large = b.x * 2.0 + b.y * 3.0 - b.x * b.y * 4.0;
                return (small * 4.0 + large + .5) / 16.0;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 pixel = floor(i.uv * max(_NativeSize.xy, 1.0));
                float2 uv = (pixel + .5) / max(_NativeSize.xy, 1.0);
                float2 distanceFromCenter = abs(uv * 2.0 - 1.0);
                // All four screen corners breathe in together; preserve the
                // centre and both central axes for the message and the cage.
                // Opacity and reach change together, so the slow breath stays
                // perceptible over the mostly-black desktop without flashing.
                float2 inner = lerp(float2(.24, .24), float2(.08, .10), saturate(_Breath));
                float x = smoothstep(inner.x, 1.0, distanceFromCenter.x);
                float y = smoothstep(inner.y, 1.0, distanceFromCenter.y);
                float alpha = sqrt(saturate(x * y)) * _Strength;
                // Dither the falloff on the native pixel grid, not a noisy or
                // blurred post-process. More steps preserve the quiet breath.
                alpha = floor(alpha * 16.0 + Bayer4(pixel)) / 16.0;
                alpha *= i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(i.local.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - .001);
                #endif
                return fixed4(0, 0, 0, saturate(alpha));
            }
            ENDCG
        }
    }
}
