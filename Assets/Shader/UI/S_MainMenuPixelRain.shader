Shader "UI/MainMenu/PixelRain"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (0.6,0.8,1,0.5)
        _ReferenceResolution ("Reference Resolution", Vector) = (250,202,0,0)
        _PixelSize ("Pixel Size", Range(1,4)) = 1
        _LaneWidth ("Lane Width", Range(3,40)) = 12
        _DropLength ("Drop Length", Range(1,40)) = 12
        _DropWidth ("Drop Width", Range(1,6)) = 1
        _FallSpeed ("Fall Speed", Range(1,320)) = 55
        _Slant ("Slant", Range(-0.5,0.5)) = -0.08
        _Density ("Density", Range(0,1)) = 0.7
        _Breakup ("Breakup", Range(0,0.8)) = 0.2
        _Intensity ("Intensity", Range(0,4)) = 1
        _Seed ("Seed", Float) = 1

        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 1
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
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
        Blend [_SrcBlend] [_DstBlend]
        ColorMask RGB

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
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
            fixed4 _Color;
            float4 _ClipRect;
            float4 _ReferenceResolution;
            float _PixelSize;
            float _LaneWidth;
            float _DropLength;
            float _DropWidth;
            float _FallSpeed;
            float _Slant;
            float _Density;
            float _Breakup;
            float _Intensity;
            float _Seed;

            float Hash11(float value)
            {
                return frac(sin(value * 127.1 + 311.7) * 43758.5453);
            }

            float Hash21(float2 value)
            {
                return frac(
                    sin(dot(value, float2(127.1, 311.7))) * 43758.5453);
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

            fixed4 frag(v2f input) : SV_Target
            {
                float2 resolution = max(
                    _ReferenceResolution.xy,
                    float2(1.0, 1.0));
                float pixelSize = max(1.0, floor(_PixelSize + 0.5));
                float2 localUv = saturate(input.texcoord);
                float2 pixel = min(
                    floor(localUv * resolution / pixelSize)
                        * pixelSize + pixelSize * 0.5,
                    resolution - 0.5);

                float laneWidth = max(pixelSize, _LaneWidth);
                float slantedX = pixel.x - pixel.y * _Slant;
                float lane = floor(slantedX / laneWidth);
                float lanePosition = slantedX - lane * laneWidth;

                float laneSeed = Hash11(lane + _Seed * 19.19);
                float lengthSeed = Hash11(lane + _Seed * 41.73 + 7.0);
                float positionSeed = Hash11(lane + _Seed * 73.31 + 13.0);
                float dropLength = max(
                    pixelSize,
                    floor(
                        _DropLength * lerp(0.65, 1.35, lengthSeed)
                        / pixelSize) * pixelSize);
                float dropCenter = lerp(
                    laneWidth * 0.2,
                    laneWidth * 0.8,
                    positionSeed);

                float travel = resolution.y + dropLength
                    + lerp(12.0, resolution.y * 0.65, laneSeed);
                float phase = frac(
                    (_Time.y * _FallSpeed + laneSeed * travel) / travel);
                float headY = resolution.y + dropLength - phase * travel;
                float trail = pixel.y - headY;

                float halfWidth = max(0.5, _DropWidth * 0.5);
                float insideX = step(abs(lanePosition - dropCenter), halfWidth);
                float insideY = step(0.0, trail) * step(trail, dropLength);
                float laneEnabled = step(laneSeed, saturate(_Density));

                float segmentSize = max(pixelSize * 2.0, 1.0);
                float segment = floor(max(trail, 0.0) / segmentSize);
                float segmentNoise = Hash21(float2(
                    lane + _Seed * 3.17,
                    segment + _Seed * 5.93));
                float connectedHead = 1.0 - step(pixelSize * 2.0, trail);
                float segmentEnabled = max(
                    connectedHead,
                    step(saturate(_Breakup), segmentNoise));

                float headFade = saturate(1.0 - trail / dropLength);
                float brightness = lerp(0.28, 1.0, headFade);
                float rain = laneEnabled * insideX * insideY
                    * segmentEnabled * brightness;

                rain *= tex2D(_MainTex, input.texcoord).a;

                #ifdef UNITY_UI_CLIP_RECT
                rain *= UnityGet2DClipping(
                    input.worldPosition.xy,
                    _ClipRect);
                #endif

                fixed alpha = saturate(rain * input.color.a);

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(input.color.rgb * _Intensity, alpha);
            }
            ENDCG
        }
    }
}
