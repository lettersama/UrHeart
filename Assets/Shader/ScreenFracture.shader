Shader "Hidden/MrTGame/ScreenFracture"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 0

        ZTest Always
        ZWrite Off
        ZClip False
        Cull Off

        Pass
        {
            Name "Screen Fracture"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            // x: intensity, y: translation, z: rotation radians, w: zoom
            float4 _FractureSettings;
            // x: chromatic shift, y: crack width, z: crack darkness, w: rim highlight
            float4 _CrackSettings;
            // xy: center, zw: horizontal/vertical safe radii
            float4 _SafeArea;
            float _SafeFeather;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = input.positionOS;
                output.uv = input.uv;
                return output;
            }

            float AngleDistance(float a, float b)
            {
                return abs(atan2(sin(a - b), cos(a - b)));
            }

            float Hash11(float value)
            {
                return frac(sin(value * 12.9898) * 43758.5453);
            }

            float BoundaryOffset(float radius, float seed)
            {
                float cellCoordinate = radius * 11.0;
                float cell = floor(cellCoordinate);
                float cellBlend = frac(cellCoordinate);
                float startOffset = Hash11(cell + seed) * 2.0 - 1.0;
                float endOffset = Hash11(cell + 1.0 + seed) * 2.0 - 1.0;

                // Linear interpolation intentionally keeps a sharp change of slope at each cell.
                return lerp(startOffset, endOffset, cellBlend) * 0.026;
            }

            float2 Rotate(float2 value, float angle)
            {
                float sineValue;
                float cosineValue;
                sincos(angle, sineValue, cosineValue);
                return float2(
                    cosineValue * value.x - sineValue * value.y,
                    sineValue * value.x + cosineValue * value.y);
            }

            int GetShard(
                float angle,
                float boundary0,
                float boundary1,
                float boundary2,
                float boundary3,
                float boundary4)
            {
                if (angle < boundary0 || angle >= boundary4)
                    return 0;
                if (angle < boundary1)
                    return 1;
                if (angle < boundary2)
                    return 2;
                if (angle < boundary3)
                    return 3;
                return 4;
            }

            float2 RemapShard(float2 uv, int shard, float aspect)
            {
                float2 offset;
                float rotationFactor;
                float zoomFactor;

                if (shard == 0)
                {
                    offset = float2(0.52, -0.16);
                    rotationFactor = -0.76;
                    zoomFactor = 0.42;
                }
                else if (shard == 1)
                {
                    offset = float2(-0.30, 0.64);
                    rotationFactor = 0.48;
                    zoomFactor = -0.62;
                }
                else if (shard == 2)
                {
                    offset = float2(0.38, -0.56);
                    rotationFactor = -0.38;
                    zoomFactor = 0.74;
                }
                else if (shard == 3)
                {
                    offset = float2(-0.66, 0.18);
                    rotationFactor = 0.82;
                    zoomFactor = -0.34;
                }
                else
                {
                    offset = float2(0.16, -0.46);
                    rotationFactor = -0.56;
                    zoomFactor = 0.56;
                }

                float2 local = uv - _SafeArea.xy;
                local.x *= aspect;
                local = Rotate(local, _FractureSettings.z * rotationFactor);
                local *= 1.0 + _FractureSettings.w * zoomFactor;
                local.x /= aspect;

                offset.x /= aspect;
                return _SafeArea.xy + local + offset * _FractureSettings.y;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float aspect = max(1.0, _MainTex_TexelSize.z / max(_MainTex_TexelSize.w, 1.0));

                float2 radial = uv - _SafeArea.xy;
                radial.x *= aspect;
                float radius = length(radial);
                float angle = atan2(radial.y, radial.x);

                float boundary0 = -2.44 + BoundaryOffset(radius, 3.17);
                float boundary1 = -1.26 + BoundaryOffset(radius, 17.43);
                float boundary2 = -0.14 + BoundaryOffset(radius, 31.91);
                float boundary3 = 0.96 + BoundaryOffset(radius, 47.29);
                float boundary4 = 2.10 + BoundaryOffset(radius, 63.73);

                int shard = GetShard(
                    angle,
                    boundary0,
                    boundary1,
                    boundary2,
                    boundary3,
                    boundary4);
                float2 remappedUv = RemapShard(uv, shard, aspect);

                float2 safeDelta = (uv - _SafeArea.xy) / max(_SafeArea.zw, float2(0.01, 0.01));
                float safeDistance = length(safeDelta);
                float outsideSafeArea = smoothstep(1.0, 1.0 + _SafeFeather, safeDistance);
                float effectWeight = saturate(_FractureSettings.x) * outsideSafeArea;

                float2 sampleUv = saturate(lerp(uv, remappedUv, effectWeight));

                float angularDistance = AngleDistance(angle, boundary0);
                angularDistance = min(angularDistance, AngleDistance(angle, boundary1));
                angularDistance = min(angularDistance, AngleDistance(angle, boundary2));
                angularDistance = min(angularDistance, AngleDistance(angle, boundary3));
                angularDistance = min(angularDistance, AngleDistance(angle, boundary4));

                float crackDistance = angularDistance * max(radius, 0.08);
                float crackWidth = max(_CrackSettings.y, _MainTex_TexelSize.y * 0.75);
                float crackCore = 1.0 - smoothstep(crackWidth * 0.22, crackWidth, crackDistance);
                float crackOuter = 1.0 - smoothstep(crackWidth, crackWidth * 2.8, crackDistance);
                float crackRim = saturate(crackOuter - crackCore);
                crackCore *= outsideSafeArea;
                crackRim *= outsideSafeArea;

                float2 chromaticDirection = normalize(radial + float2(0.0001, 0.0));
                chromaticDirection.x /= aspect;
                float edgeEmphasis = saturate(0.25 + crackOuter * 0.75);
                float2 chromaticOffset = chromaticDirection
                    * _CrackSettings.x
                    * effectWeight
                    * edgeEmphasis;

                half4 centerSample = tex2D(_MainTex, sampleUv);
                half redSample = tex2D(_MainTex, saturate(sampleUv + chromaticOffset)).r;
                half blueSample = tex2D(_MainTex, saturate(sampleUv - chromaticOffset)).b;
                half3 color = half3(redSample, centerSample.g, blueSample);

                float visibleCrack = saturate(_FractureSettings.x);
                color *= 1.0 - crackCore * _CrackSettings.z * visibleCrack;
                color += crackRim * _CrackSettings.w * visibleCrack;

                return half4(color, centerSample.a);
            }
            ENDHLSL
        }
    }
}
