Shader "UI/HeartHealReinforcedBreak"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BreakProgress ("Break Progress", Range(0,1)) = 0
        _Seed ("Seed", Float) = 0
        _SpriteUvRect ("Sprite UV Rect", Vector) = (0,0,1,1)
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
            #pragma target 3.0
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
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _BreakProgress;
            float _Seed;
            float4 _SpriteUvRect;

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

            float Hash(float2 value)
            {
                return frac(sin(dot(value + _Seed, float2(12.9898, 78.233)))
                    * 43758.5453);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                const float gridSize = 5.0;
                float progress = saturate(_BreakProgress);
                float eased = 1.0 - pow(1.0 - progress, 3.0);
                float2 spriteSize = max(
                    _SpriteUvRect.zw,
                    float2(0.0001, 0.0001));
                float2 localUv = (input.texcoord - _SpriteUvRect.xy)
                    / spriteSize;
                fixed4 result = fixed4(0.0, 0.0, 0.0, 0.0);

                [unroll]
                for (int y = 0; y < 5; y++)
                {
                    [unroll]
                    for (int x = 0; x < 5; x++)
                    {
                        float2 cell = float2((float)x, (float)y);
                        float2 sourceCenter = (cell + 0.5) / gridSize;
                        float2 radial = sourceCenter - 0.5;
                        float radialLength = max(length(radial), 0.08);
                        float2 direction = radial / radialLength;
                        float jitterAngle = (Hash(cell + 3.1) - 0.5) * 0.85;
                        float sineAngle = sin(jitterAngle);
                        float cosineAngle = cos(jitterAngle);
                        direction = float2(
                            direction.x * cosineAngle - direction.y * sineAngle,
                            direction.x * sineAngle + direction.y * cosineAngle);
                        float distance = lerp(
                            0.045,
                            0.09,
                            Hash(cell + 11.7));
                        float2 targetCenter = sourceCenter
                            + direction * distance * eased;
                        float2 local = (localUv - targetCenter)
                            * gridSize + 0.5;
                        float fragmentGap = 0.035 * progress;
                        float inside = step(fragmentGap, local.x)
                            * step(local.x, 1.0 - fragmentGap)
                            * step(fragmentGap, local.y)
                            * step(local.y, 1.0 - fragmentGap);
                        float2 sourceUv = _SpriteUvRect.xy
                            + ((cell + local) / gridSize) * spriteSize;
                        fixed4 sampleColor = tex2D(_MainTex, sourceUv)
                            + _TextureSampleAdd;
                        sampleColor *= input.color * inside;
                        float replace = step(result.a, sampleColor.a);
                        result = lerp(result, sampleColor, replace);
                    }
                }

                result.a *= lerp(1.0, 0.72, progress);

                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(
                    input.worldPosition.xy,
                    _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif

                return result;
            }
            ENDCG
        }
    }
}
