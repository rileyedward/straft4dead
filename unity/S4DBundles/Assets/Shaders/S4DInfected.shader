// L4D2 common infected look (Phase 3), reimplemented for Unity's built-in pipeline.
// L4D2's "Infected" shader keeps no colour in the base texture: alpha is a grey detail/luminance map, blue marks
// skin (1) versus clothing (0), red and green are blood and grime masks (0.5 = none). Colour comes from
// ci_gradient_palette (256x16): rows 0-7 start with a skin ramp (first quarter), rows 8-15 hold a top-clothing
// ramp (first half) and a bottom-clothing ramp (second half). Each zombie picks its rows, which is what makes a
// horde look varied from a handful of textures. The palette mapping is our reading of the textures, not Valve's
// shader source, and is tuned by eye.
Shader "S4D/Infected"
{
    Properties
    {
        _MainTex ("Infected mask (A detail, B skin, R blood, G grime)", 2D) = "white" {}
        _Palette ("Gradient palette", 2D) = "white" {}
        _SkinRow ("Skin palette row (0-7)", Float) = 2
        _ClothRow ("Clothing palette row (8-15)", Float) = 9
        _TopSplit ("UV v above which clothing is the top", Range(0, 1)) = 0.42
        _Blood ("Blood amount", Range(0, 2)) = 1
        _Glossiness ("Smoothness", Range(0, 1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _Palette;
        float _SkinRow, _ClothRow, _TopSplit, _Blood, _Glossiness;

        struct Input { float2 uv_MainTex; };

        float3 Pal(float u, float row)
        {
            // PNG row 0 is the top of the image, which is v = 1 in Unity.
            return tex2D(_Palette, float2(saturate(u), 1.0 - (row + 0.5) / 16.0)).rgb;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float4 m = tex2D(_MainTex, IN.uv_MainTex);
            float3 skin = Pal(m.a * 0.25, _SkinRow);
            float3 top = Pal(m.a * 0.5, _ClothRow);
            float3 bottom = Pal(0.5 + m.a * 0.5, _ClothRow);
            float3 cloth = IN.uv_MainTex.y > _TopSplit ? top : bottom;
            float3 c = lerp(cloth, skin, step(0.5, m.b));
            float grime = saturate((m.g - 0.55) * 2.0) * 0.6;
            c = lerp(c, float3(0.18, 0.14, 0.10), grime);
            float blood = saturate((m.r - 0.55) * 3.0 * _Blood);
            c = lerp(c, float3(0.25, 0.02, 0.01), blood);
            o.Albedo = c;
            o.Smoothness = _Glossiness + blood * 0.4;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
