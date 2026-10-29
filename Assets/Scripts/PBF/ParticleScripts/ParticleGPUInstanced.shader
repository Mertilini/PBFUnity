Shader "PBF/ParticleGPUInstanced"
{
    Properties
    {
        _Color("Color", Color) = (0.2, 0.6, 1.0, 0.8)
        _Size("Size", Float) = 0.1
    }
    
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
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            StructuredBuffer<float3> _ParticlePositions;
            float4 _Color;
            float _Size;

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o = (v2f)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 pos = _ParticlePositions[instanceID];
                float4 worldPos = float4(pos + v.vertex.xyz * _Size, 1.0);
                o.vertex = TransformWorldToHClip(worldPos.xyz);
                o.uv = v.uv;
                o.color = _Color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float dist = length(i.uv - 0.5) * 2.0;
                float alpha = smoothstep(1.0, 0.0, dist);
                return float4(i.color.rgb, i.color.a * alpha);
            }

            ENDHLSL
        }
    }
}
