// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

// Based on the VRChat Mobile Diffuse shader, plus:
// * Backface culling off
// * Only use material colors

// Simplified Diffuse shader. Differences from regular Diffuse one:
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
