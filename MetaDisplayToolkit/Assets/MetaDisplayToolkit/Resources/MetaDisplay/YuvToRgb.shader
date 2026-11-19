// Converts the glasses camera YUV 4:2:0 frames to RGB (BT.601, limited range).
// _Layout: 1 = I420 (Y, U, V planes), 2 = NV12 (Y + interleaved UV), 3 = NV21 (Y + interleaved VU).
Shader "Hidden/MetaDisplay/YuvToRgb"
{
    Properties
    {
        _MainTex ("Y", 2D) = "black" {}
        _UTex ("U", 2D) = "gray" {}
        _VTex ("V", 2D) = "gray" {}
        _UVTex ("UV", 2D) = "gray" {}
        _Layout ("Layout", Float) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _UTex;
            sampler2D _VTex;
            sampler2D _UVTex;
            float _Layout;

            fixed4 frag (v2f_img i) : SV_Target
            {
                // Buffer row 0 is the top of the image; in Unity row 0 is the bottom.
                float2 uv = float2(i.uv.x, 1.0 - i.uv.y);
                float y = tex2D(_MainTex, uv).r;
                float u;
                float v;
                if (_Layout < 1.5)
                {
                    u = tex2D(_UTex, uv).r;
                    v = tex2D(_VTex, uv).r;
                }
                else
                {
                    float2 c = tex2D(_UVTex, uv).rg;
                    u = _Layout < 2.5 ? c.x : c.y;
                    v = _Layout < 2.5 ? c.y : c.x;
                }
                y = 1.16438 * (y - 0.0627451);
                u -= 0.5;
                v -= 0.5;
                float3 rgb = float3(
                    y + 1.59603 * v,
                    y - 0.39176 * u - 0.81297 * v,
                    y + 2.01723 * u);
                rgb = saturate(rgb);
            #if !defined(UNITY_COLORSPACE_GAMMA)
                rgb = GammaToLinearSpace(rgb);
            #endif
                return fixed4(rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
