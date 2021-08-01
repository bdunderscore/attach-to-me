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

Shader "bd_/AttachToMe/BoneSphere"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Width ("Width", Range(0,1)) = 0.1
        _Freq ("Freq", Float) = 1
        _RimlightWidth ("Rimlight width", Range(0,1)) = 0.8
        _DerivAdjust ("DerivAdjust", Float) = 1
    }
    SubShader
    {
		Tags {
            "IgnoreProjector"="True"
            "Queue"="Overlay"
            "RenderType"="Transparent"
        }

        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
			ZTest Off
			Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float3 objPos : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 viewDir : TEXCOORD1;
                float3 normal : TEXCOORD2;
            };

            fixed4 _Color;
            float _Width;
            float _RimlightWidth;
            float _Freq;
            float _DerivAdjust;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.objPos = v.vertex.xyz;
                o.viewDir = -normalize(ObjSpaceViewDir(v.vertex));
                o.normal = v.normal;

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float deriv = max(length(ddx(i.objPos)), length(ddy(i.objPos)));

                float rimlight = 1 - smoothstep(0, _RimlightWidth, abs(dot(i.normal, i.viewDir)));

                float3 vecPos = abs(frac(i.objPos * _Freq + 0.5) - 0.5);
                float3 rejectNorm = abs(max(float3(0,0,0), abs(i.normal) - 0.9));
                vecPos += rejectNorm;

                float minPos = min(vecPos.x, min(vecPos.y, vecPos.z));


                float alpha = 1 - smoothstep(0, _Width + deriv * _DerivAdjust, minPos);
                alpha = max(rimlight, alpha);

                return fixed4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
}
