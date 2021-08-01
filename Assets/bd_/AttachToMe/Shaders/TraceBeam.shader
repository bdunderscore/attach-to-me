/*
 * Copyright (c) 2021 bd_
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
 * CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
 * SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */


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
            };

            struct v2f
            {
                float4 worldPosAndScale : TEXCOORD0;
                float3 worldYvec : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                float4 vertex : SV_POSITION;
            };

            float4 _Color;
            float _Scale, _Ramp, _ScrollTime;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                o.worldYvec = normalize(mul(unity_ObjectToWorld, float4(0,1,0, 0)));

                o.worldPosAndScale = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 0));

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

                float index = frac(dot(i.worldPosAndScale.xyz, i.worldYvec) / i.worldPosAndScale.w / _Scale + (_Time.y / _ScrollTime));
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
