// TextMesh Pro mobile SDF variant with a low-resolution monitor glitch.
Shader "TextMeshPro/OS/No Signal Glitch"
{
    Properties
    {
        _FaceColor          ("Face Color", Color) = (1,1,1,1)
        _FaceDilate         ("Face Dilate", Range(-1,1)) = 0

        _OutlineColor       ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth       ("Outline Thickness", Range(0,1)) = 0
        _OutlineSoftness    ("Outline Softness", Range(0,1)) = 0

        _UnderlayColor      ("Border Color", Color) = (0,0,0,.5)
        _UnderlayOffsetX    ("Border OffsetX", Range(-1,1)) = 0
        _UnderlayOffsetY    ("Border OffsetY", Range(-1,1)) = 0
        _UnderlayDilate     ("Border Dilate", Range(-1,1)) = 0
        _UnderlaySoftness   ("Border Softness", Range(0,1)) = 0

        _WeightNormal       ("Weight Normal", Float) = 0
        _WeightBold         ("Weight Bold", Float) = .5

        _ShaderFlags        ("Flags", Float) = 0
        _ScaleRatioA        ("Scale RatioA", Float) = 1
        _ScaleRatioB        ("Scale RatioB", Float) = 1
        _ScaleRatioC        ("Scale RatioC", Float) = 1

        _MainTex            ("Font Atlas", 2D) = "white" {}
        _TextureWidth       ("Texture Width", Float) = 512
        _TextureHeight      ("Texture Height", Float) = 512
        _GradientScale      ("Gradient Scale", Float) = 5
        _ScaleX             ("Scale X", Float) = 1
        _ScaleY             ("Scale Y", Float) = 1
        _PerspectiveFilter  ("Perspective Correction", Range(0,1)) = .875
        _Sharpness          ("Sharpness", Range(-1,1)) = 0

        _VertexOffsetX      ("Vertex OffsetX", Float) = 0
        _VertexOffsetY      ("Vertex OffsetY", Float) = 0

        _ClipRect           ("Clip Rect", Vector) = (-32767,-32767,32767,32767)
        _MaskSoftnessX      ("Mask SoftnessX", Float) = 0
        _MaskSoftnessY      ("Mask SoftnessY", Float) = 0

        _StencilComp        ("Stencil Comparison", Float) = 8
        _Stencil            ("Stencil ID", Float) = 0
        _StencilOp          ("Stencil Operation", Float) = 0
        _StencilWriteMask   ("Stencil Write Mask", Float) = 255
        _StencilReadMask    ("Stencil Read Mask", Float) = 255

        _CullMode           ("Cull Mode", Float) = 0
        _ColorMask          ("Color Mask", Float) = 15

        [HideInInspector] _GlitchTime         ("Glitch Time", Float) = 0
        [HideInInspector] _GlitchIntensity    ("Glitch Intensity", Range(0,1)) = .72
        [HideInInspector] _GlitchPixelSize    ("Glitch Pixel Size", Float) = 1
        [HideInInspector] _GlitchBandHeight   ("Glitch Band Height", Float) = 3
        [HideInInspector] _GlitchOffsetPixels ("Glitch Offset Pixels", Float) = 4
        [HideInInspector] _GlitchDropout      ("Glitch Dropout", Range(0,1)) = .14
        [HideInInspector] _GlitchSeed         ("Glitch Seed", Float) = 13.37
        [HideInInspector] _GlitchReferenceResolution
            ("Glitch Reference Resolution", Vector) = (640,360,0,0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
        }

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
        Fog { Mode Off }
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex VertShader
            #pragma fragment PixShader
            #pragma shader_feature __ OUTLINE_ON
            #pragma shader_feature __ UNDERLAY_ON UNDERLAY_INNER
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "../../TextMesh Pro/Shaders/TMPro_Properties.cginc"

            struct vertex_t
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float4 texcoord0 : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
            };

            struct pixel_t
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
                float4 vertex : SV_POSITION;
                fixed4 faceColor : COLOR;
                fixed4 outlineColor : COLOR1;
                float4 texcoord0 : TEXCOORD0;
                half4 param : TEXCOORD1;
                half4 mask : TEXCOORD2;
                #if (UNDERLAY_ON | UNDERLAY_INNER)
                float4 texcoord1 : TEXCOORD3;
                half2 underlayParam : TEXCOORD4;
                #endif
            };

            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            float _GlitchTime;
            float _GlitchIntensity;
            float _GlitchPixelSize;
            float _GlitchBandHeight;
            float _GlitchOffsetPixels;
            float _GlitchDropout;
            float _GlitchSeed;
            float4 _GlitchReferenceResolution;

            float Hash11(float value)
            {
                return frac(sin(value * 12.9898 + 78.233) * 43758.5453);
            }

            float BurstAmount()
            {
                float burstTime = _GlitchTime * 2.15;
                float burstIndex = floor(burstTime);
                float burstGate = step(
                    0.6,
                    Hash11(burstIndex + _GlitchSeed * 5.17));
                float attackAndHold = 1.0 - smoothstep(
                    0.18,
                    0.56,
                    frac(burstTime));
                return burstGate * attackAndHold;
            }

            pixel_t VertShader(vertex_t input)
            {
                pixel_t output;

                UNITY_INITIALIZE_OUTPUT(pixel_t, output);
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float bold = step(input.texcoord0.w, 0);
                float4 vert = input.vertex;

                float burst = BurstAmount();
                float jitterTick = floor(_GlitchTime * 18.0);
                float jitterGate = step(
                    0.68,
                    Hash11(jitterTick + _GlitchSeed * 9.31));
                float jitterDirection = floor(
                    Hash11(jitterTick * 1.73 + _GlitchSeed) * 3.0) - 1.0;
                vert.x += jitterDirection
                    * max(1.0, _GlitchPixelSize)
                    * jitterGate
                    * burst
                    * saturate(_GlitchIntensity);

                vert.x += _VertexOffsetX;
                vert.y += _VertexOffsetY;
                float4 vPosition = UnityObjectToClipPos(vert);

                float2 pixelSize = vPosition.w;
                pixelSize /= float2(_ScaleX, _ScaleY)
                    * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float scale = rsqrt(dot(pixelSize, pixelSize));
                scale *= abs(input.texcoord0.w)
                    * _GradientScale
                    * (_Sharpness + 1);
                if (UNITY_MATRIX_P[3][3] == 0)
                {
                    scale = lerp(
                        abs(scale) * (1 - _PerspectiveFilter),
                        scale,
                        abs(dot(
                            UnityObjectToWorldNormal(input.normal.xyz),
                            normalize(WorldSpaceViewDir(vert)))));
                }

                float weight = lerp(_WeightNormal, _WeightBold, bold) / 4.0;
                weight = (weight + _FaceDilate) * _ScaleRatioA * 0.5;

                float layerScale = scale;
                scale /= 1 + (_OutlineSoftness * _ScaleRatioA * scale);
                float bias = (0.5 - weight) * scale - 0.5;
                float outline = _OutlineWidth * _ScaleRatioA * 0.5 * scale;

                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    input.color.rgb = UIGammaToLinear(input.color.rgb);

                float opacity = input.color.a;
                #if (UNDERLAY_ON | UNDERLAY_INNER)
                opacity = 1.0;
                #endif

                fixed4 faceColor = fixed4(input.color.rgb, opacity) * _FaceColor;
                faceColor.rgb *= faceColor.a;

                fixed4 outlineColor = _OutlineColor;
                outlineColor.a *= opacity;
                outlineColor.rgb *= outlineColor.a;
                outlineColor = lerp(
                    faceColor,
                    outlineColor,
                    sqrt(min(1.0, outline * 2)));

                #if (UNDERLAY_ON | UNDERLAY_INNER)
                layerScale /= 1
                    + (_UnderlaySoftness * _ScaleRatioC * layerScale);
                float layerBias = (0.5 - weight) * layerScale
                    - 0.5
                    - (_UnderlayDilate * _ScaleRatioC * 0.5 * layerScale);
                float x = -(_UnderlayOffsetX * _ScaleRatioC)
                    * _GradientScale
                    / _TextureWidth;
                float y = -(_UnderlayOffsetY * _ScaleRatioC)
                    * _GradientScale
                    / _TextureHeight;
                float2 layerOffset = float2(x, y);
                #endif

                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                float2 maskUV = (vert.xy - clampedRect.xy)
                    / (clampedRect.zw - clampedRect.xy);

                output.vertex = vPosition;
                output.faceColor = faceColor;
                output.outlineColor = outlineColor;
                output.texcoord0 = float4(
                    input.texcoord0.x,
                    input.texcoord0.y,
                    maskUV.x,
                    maskUV.y);
                output.param = half4(
                    scale,
                    bias - outline,
                    bias + outline,
                    bias);

                const half2 maskSoftness = half2(
                    max(_UIMaskSoftnessX, _MaskSoftnessX),
                    max(_UIMaskSoftnessY, _MaskSoftnessY));
                output.mask = half4(
                    vert.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * maskSoftness + pixelSize.xy));

                #if (UNDERLAY_ON | UNDERLAY_INNER)
                output.texcoord1 = float4(
                    input.texcoord0 + layerOffset,
                    input.color.a,
                    0);
                output.underlayParam = half2(layerScale, layerBias);
                #endif

                return output;
            }

            fixed4 PixShader(pixel_t input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float intensity = saturate(_GlitchIntensity);
                float pixelStep = max(1.0, floor(_GlitchPixelSize + 0.5));
                float bandHeight = max(1.0, floor(_GlitchBandHeight + 0.5));
                float2 screenToReference = max(
                    float2(1.0, 1.0),
                    _GlitchReferenceResolution.xy)
                    / max(float2(1.0, 1.0), _ScreenParams.xy);
                float2 referencePixelPosition = input.vertex.xy
                    * screenToReference;
                float burst = BurstAmount();
                float sliceTick = floor(_GlitchTime * 14.0);
                float bandIndex = floor(
                    referencePixelPosition.y / bandHeight);
                float bandNoise = Hash11(
                    bandIndex * 19.19
                    + sliceTick * 7.73
                    + _GlitchSeed);
                float quietSlice = step(0.94, bandNoise) * 0.24;
                float activeSlice = step(0.43, bandNoise) * burst;
                float sliceStrength = saturate(quietSlice + activeSlice)
                    * intensity;

                float offsetNoise = Hash11(
                    bandIndex * 5.31
                    + sliceTick * 17.17
                    + _GlitchSeed * 2.7);
                float offsetSteps = floor(
                    ((offsetNoise * 2.0 - 1.0)
                    * max(0.0, _GlitchOffsetPixels)
                    / pixelStep) + 0.5);
                float offsetPixels = offsetSteps * pixelStep * sliceStrength;
                float atlasUvPerScreenPixel = max(
                    abs(ddx(input.texcoord0.x)),
                    1.0 / max(1.0, _TextureWidth));
                float screenOffsetPixels = offsetPixels
                    / max(0.0001, screenToReference.x);
                float atlasOffset = screenOffsetPixels
                    * atlasUvPerScreenPixel;

                float2 sampleUv = input.texcoord0.xy;
                sampleUv.x += atlasOffset;

                half d = tex2D(_MainTex, sampleUv).a * input.param.x;
                half4 c = input.faceColor * saturate(d - input.param.w);

                #ifdef OUTLINE_ON
                c = lerp(
                    input.outlineColor,
                    input.faceColor,
                    saturate(d - input.param.z));
                c *= saturate(d - input.param.y);
                #endif

                #if UNDERLAY_ON
                float2 underlayUv = input.texcoord1.xy;
                underlayUv.x += atlasOffset;
                d = tex2D(_MainTex, underlayUv).a
                    * input.underlayParam.x;
                c += float4(
                    _UnderlayColor.rgb * _UnderlayColor.a,
                    _UnderlayColor.a)
                    * saturate(d - input.underlayParam.y)
                    * (1 - c.a);
                #endif

                #if UNDERLAY_INNER
                half sd = saturate(d - input.param.z);
                float2 underlayUv = input.texcoord1.xy;
                underlayUv.x += atlasOffset;
                d = tex2D(_MainTex, underlayUv).a
                    * input.underlayParam.x;
                c += float4(
                    _UnderlayColor.rgb * _UnderlayColor.a,
                    _UnderlayColor.a)
                    * (1 - saturate(d - input.underlayParam.y))
                    * sd
                    * (1 - c.a);
                #endif

                float blockWidth = max(3.0, pixelStep * 4.0);
                float blockIndex = floor(
                    referencePixelPosition.x / blockWidth);
                float dropoutNoise = Hash11(
                    blockIndex * 23.41
                    + bandIndex * 41.07
                    + sliceTick * 3.11
                    + _GlitchSeed);
                float blockDrop = step(
                    1.0 - saturate(_GlitchDropout),
                    dropoutNoise)
                    * burst
                    * intensity;

                float blinkTick = floor(_GlitchTime * 24.0);
                float briefBlink = step(
                    0.965,
                    Hash11(blinkTick * 13.7 + _GlitchSeed * 8.3))
                    * burst
                    * intensity;
                float visibility = (1.0 - blockDrop * 0.72)
                    * (1.0 - briefBlink * 0.48);
                c *= visibility;

                float darkBand = step(0.9, bandNoise)
                    * burst
                    * intensity;
                c.rgb *= 1.0 - darkBand * 0.16;

                #if UNITY_UI_CLIP_RECT
                half2 m = saturate(
                    (_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy))
                    * input.mask.zw);
                c *= m.x * m.y;
                #endif

                #if (UNDERLAY_ON | UNDERLAY_INNER)
                c *= input.texcoord1.z;
                #endif

                #if UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif

                return c;
            }
            ENDCG
        }
    }

    CustomEditor "TMPro.EditorUtilities.TMP_SDFShaderGUI"
}
