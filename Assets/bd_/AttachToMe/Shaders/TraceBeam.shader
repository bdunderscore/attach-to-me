Shader "bd_/AttachToMe/TraceBeam"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Scale ("Scale", Float) = 1
        _Ramp ("Ramp", Float) = 1
        _ScrollTime ("ScrollTime", Float) = 1
    }
    SubShader
    {
        Tags {"Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent"}
        LOD 100

        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 normal : NORMAL;
                float4 tangent : TANGENT;
            };

            struct v2f
            {
                float4 worldPosAndScale : TEXCOORD0;
                float3 worldTangentVec : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                float4 vertex : SV_POSITION;
            };

            float4 _Color;
            float _Scale, _Ramp, _ScrollTime;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                float3 bitangent = cross(v.tangent.xyz, v.normal.xyz);

                o.worldTangentVec = normalize(mul(unity_ObjectToWorld, float4(bitangent, 0)));

                o.worldPosAndScale = mul(unity_ObjectToWorld, v.vertex);

                o.worldPosAndScale.w = max(
                    max(
                        length(UNITY_MATRIX_V[0].xyz),
                        length(UNITY_MATRIX_V[1].xyz)
                    ),
                    length(UNITY_MATRIX_V[2].xyz)
                );

                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = _Color;

                float index = frac(dot(i.worldPosAndScale.xyz, i.worldTangentVec) / i.worldPosAndScale.w / _Scale + (_Time.y / _ScrollTime));
                float ramp = saturate(_Ramp * (2 * index - 1));

                col *= ramp;

                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
