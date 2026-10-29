Shader "PBF/SSFR/FluidBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        float4 _MainTex_TexelSize;

        float2 _BlurDir;
        float _BlurRadius;
        float _RangeFalloff;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv         : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
        };

        Varyings vert(Attributes v)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
            o.uv = v.uv;
            return o;
        }
        ENDHLSL

        Pass
        {
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragBilateral
            #pragma target 4.5

            #define SSFR_EMPTY_THRESHOLD 5.0e7

            float fragBilateral(Varyings i) : SV_Target
            {
                float d0 = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv, 0).r;
                if (d0 >= SSFR_EMPTY_THRESHOLD)
                    return d0;

                int radius = (int) _BlurRadius;
                float sigmaS = max(_BlurRadius * 0.5, 1.0);
                float sigmaR = max(_RangeFalloff, 1e-4);

                float sum = 0.0;
                float weightSum = 0.0;

                for (int t = -radius; t <= radius; t++)
                {
                    float2 uv = i.uv + _BlurDir * _MainTex_TexelSize.xy * t;
                    float d = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0).r;

                    float wSpatial = exp(-(t * t) / (2.0 * sigmaS * sigmaS));
                    float diff = d - d0;
                    float wRange = exp(-(diff * diff) / (2.0 * sigmaR * sigmaR));
                    float w = wSpatial * wRange * (d < SSFR_EMPTY_THRESHOLD ? 1.0 : 0.0);

                    sum += d * w;
                    weightSum += w;
                }

                return sum / max(weightSum, 1e-5);
            }
            ENDHLSL
        }

        Pass
        {
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragGaussian
            #pragma target 4.5

            float fragGaussian(Varyings i) : SV_Target
            {
                int radius = (int) _BlurRadius;
                float sigmaS = max(_BlurRadius * 0.5, 1.0);

                float sum = 0.0;
                float weightSum = 0.0;

                for (int t = -radius; t <= radius; t++)
                {
                    float2 uv = i.uv + _BlurDir * _MainTex_TexelSize.xy * t;
                    float v = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0).r;

                    float w = exp(-(t * t) / (2.0 * sigmaS * sigmaS));
                    sum += v * w;
                    weightSum += w;
                }

                return sum / max(weightSum, 1e-5);
            }
            ENDHLSL
        }
    }
}
