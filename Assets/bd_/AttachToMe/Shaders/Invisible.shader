Shader "bd_/noop"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            ColorMask 0
            ZWrite Off
        }
    }
}
