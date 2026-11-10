Shader "UI/OS/CursorExperimentInterior"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _BackgroundColor ("Background", Color) = (0.0156863, 0.0156863, 0.0156863, 1)
        _InkColor ("Warm grey edge peak", Color) = (0.2047059, 0.19294116, 0.18352944, 1)
        _FragmentMode ("Fragment mode", Float) = 0
        _Wipe ("Visible extent, initial reference mask, brightness, seed", Vector) = (0.56, 1, 0.8, 0.5)
        _WipeBounds ("Authored window bounds", Vector) = (0, 0, 100, 50)
        _WipeAxis ("Spatial erase direction", Vector) = (1, 0.6, 0, 0)
        _Clock ("Owned presentation time", Float) = 0
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

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float4 local : TEXCOORD0; float4 uv : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _Wipe, _WipeBounds, _WipeAxis, _ClipRect;
            fixed4 _BackgroundColor, _InkColor;
            float _FragmentMode, _Clock;

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

            fixed4 frag(v2f i) : SV_Target
            {
                float3 rgb;
                float alpha;
                if (_FragmentMode < .5)
                {
                    // The central black field remains clear of texture and lines.
                    rgb = _BackgroundColor.rgb;
                    alpha = 1.0;
                }
                else
                {
                    // Quantize the moving cut to the artwork's pixel grid. The
                    // same 2D front crosses every edge; this is not a line tracer
                    // or an opacity pulse. No _Time: pauses preserve the mask.
                    float2 pixel = floor(i.uv.xy) + .5;
                    float2 p = saturate((pixel - _WipeBounds.xy) / max(_WipeBounds.zw, 1.0));
                    p.x = _WipeAxis.x < 0 ? 1.0 - p.x : p.x;
                    p.y = _WipeAxis.y < 0 ? 1.0 - p.y : p.y;
                    float2 weights = abs(_WipeAxis.xy);
                    float scan = dot(p, weights) / max(weights.x + weights.y, .001);
                    // Static, small stepped interruptions at the erase edge.
                    // They travel with the cut, never glitter across intact ink.
                    float notch = frac(sin(dot(floor(pixel / 3.0), float2(12.9898, 78.233)) + _Wipe.w) * 43758.5453);
                    scan = saturate(scan + (notch - .5) * .035);
                    // The two spatial bands encode the approved half-erased
                    // initial state, while retaining ALL missing window edges.
                    float initialRank = i.uv.z < .5 ? lerp(.02, .54, scan) : lerp(.58, .98, scan);
                    // Once initially cleared, all pixels share one spatial front.
                    float rank = _Wipe.y > .5 ? initialRank : lerp(.02, .98, scan);
                    // uv.w marks an opaque window backing: background colour, same wipe.
                    rgb = i.uv.w > .5 ? _BackgroundColor.rgb : _InkColor.rgb * _Wipe.z;
                    alpha = step(rank, _Wipe.x);
                }
                rgb *= i.color.rgb;
                alpha *= i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(i.local.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - .001);
                #endif
                return fixed4(rgb, saturate(alpha));
            }
            ENDCG
        }
    }
}
