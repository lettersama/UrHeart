Shader "UI/OS/CameraSignalIn"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Progress ("Signal Progress", Range(0,1)) = 1
        _SpriteUvRect ("Sprite UV Rect", Vector) = (0,0,1,1)
        _RectSize ("Rect Size", Vector) = (94,150,0,0)
        _BandHeightPixels ("Band Height", Range(1,12)) = 4
        _MaximumOffsetPixels ("Maximum Offset", Range(0,24)) = 12
        _ChromaticOffsetPixels ("Chromatic Offset", Range(0,8)) = 2.5
        _ScanlineStrength ("Scanline Strength", Range(0,1)) = 0.28
        _GlitchSeed ("Glitch Seed", Float) = 31.7
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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float4 _ClipRect;
            float _Progress;
            float4 _SpriteUvRect;
            float4 _RectSize;
            float _BandHeightPixels;
            float _MaximumOffsetPixels;
            float _ChromaticOffsetPixels;
            float _ScanlineStrength;
            float _GlitchSeed;

            float Hash11(float value)
            {
                return frac(sin(value * 12.9898 + 78.233) * 43758.5453);
            }

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

            float2 ClampSpriteUv(float2 uv)
            {
                float2 halfTexel = _MainTex_TexelSize.xy * 0.5;
                float2 minimumUv = _SpriteUvRect.xy + halfTexel;
                float2 maximumUv = _SpriteUvRect.xy
                    + _SpriteUvRect.zw
                    - halfTexel;
                return clamp(uv, minimumUv, maximumUv);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float progress = saturate(_Progress);
                float instability = pow(1.0 - progress, 1.35);
                float2 rectSize = max(_RectSize.xy, float2(1.0, 1.0));
                float2 localUv = saturate(
                    (input.texcoord - _SpriteUvRect.xy)
                    / max(_SpriteUvRect.zw, float2(0.0001, 0.0001)));
                float2 localPixel = localUv * rectSize;
                float bandHeight = max(1.0, _BandHeightPixels);
                float bandIndex = floor(localPixel.y / bandHeight);
                float tick = floor(progress * 22.0);

                float bandNoise = Hash11(
                    bandIndex * 17.13
                    + tick * 5.71
                    + _GlitchSeed);
                float directionNoise = Hash11(
                    bandIndex * 29.17
                    + tick * 11.31
                    + _GlitchSeed * 2.7);
                float offsetPixels = (directionNoise * 2.0 - 1.0)
                    * max(0.0, _MaximumOffsetPixels)
                    * instability;
                offsetPixels *= lerp(0.35, 1.0, bandNoise);

                float atlasUvPerPixel = _SpriteUvRect.z / rectSize.x;
                float2 centerUv = input.texcoord;
                centerUv.x += offsetPixels * atlasUvPerPixel;
                centerUv = ClampSpriteUv(centerUv);

                float chromaticPixels = max(0.0, _ChromaticOffsetPixels)
                    * instability
                    * lerp(0.45, 1.0, directionNoise);
                float2 redUv = centerUv;
                float2 blueUv = centerUv;
                redUv.x += chromaticPixels * atlasUvPerPixel;
                blueUv.x -= chromaticPixels * atlasUvPerPixel;
                redUv = ClampSpriteUv(redUv);
                blueUv = ClampSpriteUv(blueUv);

                fixed4 centerSample = tex2D(_MainTex, centerUv);
                fixed4 redSample = tex2D(_MainTex, redUv);
                fixed4 blueSample = tex2D(_MainTex, blueUv);
                fixed4 color = fixed4(
                    redSample.r,
                    centerSample.g,
                    blueSample.b,
                    centerSample.a) * input.color;

                float revealChance = saturate(progress * 1.18 + 0.04);
                float bandVisible = step(bandNoise, revealChance);
                float syncPosition = lerp(0.48, 0.72, progress);
                float syncLine = saturate(
                    1.0 - abs(localUv.y - syncPosition)
                        * rectSize.y / 2.2);
                float visibility = lerp(
                    1.0,
                    max(bandVisible * 0.82, syncLine),
                    instability);

                float dropoutNoise = Hash11(
                    floor(localPixel.x / 9.0) * 7.19
                    + bandIndex * 23.41
                    + tick * 3.17
                    + _GlitchSeed);
                float dropout = step(0.82, dropoutNoise)
                    * instability
                    * (1.0 - syncLine);
                visibility *= 1.0 - dropout * 0.88;

                float scanline = 0.5 + 0.5 * sin(
                    localPixel.y * 3.14159265);
                color.rgb *= 1.0
                    - scanline
                        * saturate(_ScanlineStrength)
                        * instability;
                color.rgb = lerp(
                    color.rgb,
                    fixed3(0.72, 0.9, 1.0),
                    syncLine * instability * 0.58);
                color.a *= visibility;

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
