Shader "PBF/SSFR/FluidDepth"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            BlendOp Min
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float3> _ParticlePositions;
            float _SphereRadius;

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float2 quadUV     : TEXCOORD0;
                float3 centerVS   : TEXCOORD1;
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
                o.centerVS = centerVS;
                return o;
            }

            float frag(v2f i) : SV_Target
            {
                float r2 = dot(i.quadUV, i.quadUV);
                clip(1.0 - r2);

                float nz = sqrt(saturate(1.0 - r2));
                float3 surfaceVS = i.centerVS + float3(i.quadUV * _SphereRadius, nz * _SphereRadius);

                return -surfaceVS.z;
            }
            ENDHLSL
        }
    }
}
