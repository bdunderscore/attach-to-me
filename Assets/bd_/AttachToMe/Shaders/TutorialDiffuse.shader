/*
 * Copyright (c) 2016 Unity Technologies
 * Copyright (c) VRChat, Inc.
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

// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

// Based on the VRChat Mobile Diffuse shader (which, according to the header, is under the MIT license), plus:
// * Backface culling off
// * Only use material colors

// Originally: Simplified Diffuse shader. Differences from regular Diffuse one:
// - no Main Color
// - fully supports only 1 directional light. Other lights can affect it, but it will be per-vertex/SH.

Shader "bd_/AttachToMe/TutorialShader"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 150
        /*
        Pass {
            Cull Off
            ZWrite On
            ZTest Always
            Colormask 0
        }*/

        Cull Off

        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Lambert exclude_path:prepass exclude_path:deferred noforwardadd noshadow nodynlightmap nolppv noshadowmask

        fixed4 _Color;

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = _Color * IN.color;
            o.Alpha = 1.0f;
        }
ENDCG
    }

    FallBack "Diffuse"
}
