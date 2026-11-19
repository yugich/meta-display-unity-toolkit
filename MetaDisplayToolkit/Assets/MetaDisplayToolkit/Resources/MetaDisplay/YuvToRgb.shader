// Converte os frames YUV 4:2:0 da câmera dos óculos em RGB (BT.601, faixa limitada).
// _Layout: 1 = I420 (planos Y, U, V), 2 = NV12 (Y + UV intercalado), 3 = NV21 (Y + VU intercalado).
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
                // A linha 0 do buffer é o topo da imagem; no Unity a linha 0 é a base.
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
