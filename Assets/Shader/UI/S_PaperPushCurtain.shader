Shader "MrTGame/UI/PaperPushCurtain"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FabricTex ("Authored left panel", 2D) = "white" {}
        _ValanceTex ("Authored valance", 2D) = "white" {}
        _Closed ("Closed", Range(0,1)) = 0
        _PixelSize ("Native Size", Vector) = (304,184,0,0)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex : POSITION;float4 color : COLOR;float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION;fixed4 color : COLOR;float2 uv : TEXCOORD0;float4 local : TEXCOORD1; };
            fixed4 _Color;float4 _ClipRect, _PixelSize;float _Closed;sampler2D _FabricTex, _ValanceTex;
            v2f vert(appdata v)
            { v2f o;o.local=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color*_Color;return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                // Fabric gathers towards a waist-height tie. The full painted panel is
                // compressed into each row's silhouette, rather than swept away by a wipe.
                float2 pixel = min(floor(i.uv * _PixelSize.xy), _PixelSize.xy - 1);
                float y = 1 - (pixel.y + .5) / _PixelSize.y;
                float side = min(pixel.x, _PixelSize.x - 1 - pixel.x);
                float closed = saturate(_Closed);
                float shoulder = pow(abs(y - .60) / .60, 1.65);
                float gathered = 6 + 26 * shoulder;
                float exitTravel = (1 - smoothstep(0, .24, closed)) * 36;
                float edge = floor(lerp(gathered, _PixelSize.x * .5 + 1, closed) - exitTravel);
                float u = saturate((side + .5) / max(1, edge));
                float lift = (1 - closed) * pow(u, 2) * .045;
                float2 fabricUV = float2((floor(u * 151) + .5) / 152,
                    (floor(saturate(1 - y - lift) * 183) + .5) / 184);
                fixed4 c = tex2D(_FabricTex, fabricUV) * i.color;
                c.a *= step(side + .5, edge);
                // A one-pixel dark inside hem and a small horizontal tie at the waist.
                if (side >= edge - 1) c.rgb = 0;
                if (closed < .16 && abs(y - .60) < .007) c.rgb = 0;
                // The valance lifts clear at the end too, leaving the status row readable.
                float headerY = floor(y * _PixelSize.y + (1 - smoothstep(0, .35, closed)) * 16);
                if (headerY < 16)
                {
                    fixed4 header = tex2D(_ValanceTex,float2((pixel.x + .5)/304,1 - (headerY + .5)/16)) * i.color;
                    c.rgb = lerp(c.rgb,header.rgb,header.a);c.a = max(c.a,header.a);
                }
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.local.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - .001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
