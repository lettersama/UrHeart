Shader "MrTGame/UI/Memory Hotspot"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OverrideInk ("Override sprite ink", Float) = 0
        _InkColor ("Sprite ink", Color) = (1,1,1,1)
        _Hover ("Hover", Float) = 0
        _Glitch ("Glitch", Float) = 0
        _Clock ("Unscaled Time", Float) = 0
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }
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
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 world:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _ClipRect;
            fixed4 _Color, _InkColor;
            float _Hover, _Glitch, _Clock, _OverrideInk;
            v2f vert(appdata v)
            {
                v2f o; o.world=v.vertex; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float2 stepUV=_MainTex_TexelSize.xy;
                float row=floor(i.uv.y/max(stepUV.y,0.00001));
                float pulse=step(4.72,fmod(_Clock,5.0))*_Glitch;
                float band=step(0.0,2.0-abs(fmod(row+floor(_Clock*17),29)-14));
                float shift=(fmod(floor(_Clock*22),2)*2-1)*2.0;
                float2 uv=i.uv+float2(stepUV.x*shift*pulse*band,0);
                fixed4 c=tex2D(_MainTex,uv);
                // Change only the colored ink; retain black details and the white hover outline.
                float ink=step(0.001,max(c.r,max(c.g,c.b)));
                c.rgb=lerp(c.rgb,_InkColor.rgb,ink*_OverrideInk);
                float a=max(max(tex2D(_MainTex,uv+float2(stepUV.x,0)).a,tex2D(_MainTex,uv-float2(stepUV.x,0)).a),
                    max(tex2D(_MainTex,uv+float2(0,stepUV.y)).a,tex2D(_MainTex,uv-float2(0,stepUV.y)).a));
                float outline=step(0.5,a)*(1-step(0.5,c.a))*_Hover;
                c.rgb=lerp(c.rgb,fixed3(1,1,1),outline);
                c.a=max(c.a,outline); c*=i.color;
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a-0.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
