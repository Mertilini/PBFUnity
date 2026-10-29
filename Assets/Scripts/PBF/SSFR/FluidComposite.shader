Shader "PBF/SSFR/FluidComposite"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            ZWrite On
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_FluidDepthTex);
            SAMPLER(sampler_FluidDepthTex);
            float4 _FluidDepthTex_TexelSize;

            TEXTURE2D(_FluidThicknessTex);
            SAMPLER(sampler_FluidThicknessTex);

            float _P00;
            float _P11;
            float4x4 _CamToWorld;

            float4 _AbsorptionColor;
            float _AbsorptionStrength;
            float4 _ReflectionColor;
            float _ReflectionStrength;
            float _RefractionStrength;
            float _SpecularPower;
            float _SpecularIntensity;

            struct v2f
            {
                float4 positionCS : SV_POSITION;
            };

            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.positionCS = float4(uv * 2.0 - 1.0, 0.5, 1.0);
                return o;
            }

            float SampleFluidDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_LOD(_FluidDepthTex, sampler_FluidDepthTex, uv, 0).r;
            }

            float3 ViewPosFromDepth(float2 uv, float eyeDepth)
            {

                #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1.0 - uv.y;
                #endif

                float2 ndc = uv * 2.0 - 1.0;
                return float3(ndc.x * eyeDepth / _P00, ndc.y * eyeDepth / _P11, -eyeDepth);
            }

            struct FragOutput
            {
                half4 color : SV_Target;
                float depth : SV_Depth;
            };

            FragOutput frag(v2f input)
            {

                float2 uvSS = input.positionCS.xy / _ScaledScreenParams.xy;

                float eyeDepth = SampleFluidDepth(uvSS);
                if (eyeDepth >= 5.0e7)
                    discard;

                float sceneEye = LinearEyeDepth(SampleSceneDepth(uvSS), _ZBufferParams);
                if (eyeDepth > sceneEye)
                    discard;

                float2 dx = float2(_FluidDepthTex_TexelSize.x, 0.0);
                float2 dy = float2(0.0, _FluidDepthTex_TexelSize.y);

                float dC = eyeDepth;
                float dR = SampleFluidDepth(uvSS + dx);
                float dL = SampleFluidDepth(uvSS - dx);
                float dT = SampleFluidDepth(uvSS + dy);
                float dB = SampleFluidDepth(uvSS - dy);

                float3 pC = ViewPosFromDepth(uvSS, dC);

                float3 ddxV = ViewPosFromDepth(uvSS + dx, dR) - pC;
                float3 ddxV2 = pC - ViewPosFromDepth(uvSS - dx, dL);
                if (abs(dR - dC) > abs(dC - dL) || dR >= 5.0e7)
                    ddxV = ddxV2;

                float3 ddyV = ViewPosFromDepth(uvSS + dy, dT) - pC;
                float3 ddyV2 = pC - ViewPosFromDepth(uvSS - dy, dB);
                if (abs(dT - dC) > abs(dC - dB) || dT >= 5.0e7)
                    ddyV = ddyV2;

                float3 nVS = normalize(cross(ddxV, ddyV));
                if (nVS.z < 0.0)
                    nVS = -nVS;

                float3 nWS = normalize(mul((float3x3) _CamToWorld, nVS));
                float3 posWS = mul(_CamToWorld, float4(pC, 1.0)).xyz;
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - posWS);

                float thickness = SAMPLE_TEXTURE2D_LOD(_FluidThicknessTex, sampler_FluidThicknessTex, uvSS, 0).r;

                float2 refrOffset = nVS.xy * _RefractionStrength * saturate(thickness);
                #if UNITY_UV_STARTS_AT_TOP
                    refrOffset.y = -refrOffset.y;
                #endif
                float3 sceneColor = SampleSceneColor(uvSS + refrOffset);

                float3 absorb = exp(-_AbsorptionColor.rgb * _AbsorptionStrength * thickness);
                float3 color = sceneColor * absorb;

                float fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(nWS, viewDirWS)), 5.0);
                color = lerp(color, _ReflectionColor.rgb, fresnel * _ReflectionStrength);

                Light mainLight = GetMainLight();
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float spec = pow(saturate(dot(nWS, halfDir)), _SpecularPower) * _SpecularIntensity;
                color += mainLight.color * spec;

                float4 fluidCS = TransformWViewToHClip(float3(0.0, 0.0, -eyeDepth));

                FragOutput o;
                o.color = half4(color, 1.0);
                o.depth = fluidCS.z / fluidCS.w;
                return o;
            }
            ENDHLSL
        }
    }
}
