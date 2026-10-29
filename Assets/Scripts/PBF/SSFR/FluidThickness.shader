Shader "PBF/SSFR/FluidThickness"
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
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float3> _ParticlePositions;
            float _SphereRadius;
            float _ThicknessScale;

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float2 quadUV     : TEXCOORD0;
            };

            static const float2 kCorners[6] =
            {
                float2(-1, -1), float2(1, -1), float2(1, 1),
                float2(-1, -1), float2(1, 1), float2(-1, 1)
            };

            v2f vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                v2f o;
                float3 centerVS = TransformWorldToView(_ParticlePositions[instanceID]);
                float2 corner = kCorners[vertexID];
                float3 cornerVS = centerVS + float3(corner * _SphereRadius, 0.0);

                o.positionCS = TransformWViewToHClip(cornerVS);
                o.quadUV = corner;
                return o;
            }

            float frag(v2f i) : SV_Target
            {
                float r2 = dot(i.quadUV, i.quadUV);
                clip(1.0 - r2);

                float nz = sqrt(saturate(1.0 - r2));
                return 2.0 * nz * _SphereRadius * _ThicknessScale;
            }
            ENDHLSL
        }
    }
}
