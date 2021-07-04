Shader "bd_/noop"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1999"}
        LOD 100

        Pass
        {
            ColorMask 0
            ZWrite Off

            CGPROGRAM
            
#pragma vertex vert
#pragma fragment frag

            void vert(out float4 pos : POSITION) {
                pos = float4(-2, -2, -2, 1); // offscreen
            }

            fixed4 frag() : SV_Target {
                return fixed4(0,0,0,0);
            }

            ENDCG
        }
    }
}
