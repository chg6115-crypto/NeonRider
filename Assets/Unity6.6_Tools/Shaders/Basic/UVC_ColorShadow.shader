Shader "UVC Shader/UVC_ColorShadowMatte"
{
    Properties
    {
        _SurfaceColor("Surface Color", Color) = (1,1,1,1)
        _ShadowTint("Shadow Tint", Color) = (0,0,0,0.5)
        _Alpha("Alpha", Float) = 1
        _AlphaClip("Alpha Clip Threshold", Float) = 0.5
        _AoRadius("AO Radius", Float) = 0.75
        [Toggle]_AoRadiusDebug("AO Radius Debug", Float) = 0
        [HideInInspector]_MainTex("Base (RGB)", 2D) = "white" {}
    }

        SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            float4 _SurfaceColor;
            float4 _ShadowTint;
            float _Alpha;
            float _AlphaClip;
            float _AoRadius;
            float _AoRadiusDebug;

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 color = _SurfaceColor.rgb;
                float alpha = _Alpha;

                // AO radius-based darkening
                float dist = length(i.positionWS / 16.0);
                float aoIntensity = saturate((dist - _AoRadius) / (0.9 - _AoRadius));
                aoIntensity = lerp(1, 0, aoIntensity);

                // Apply AO to color
                float3 aoColor = lerp(float3(0,0,0), float3(1,1,1), aoIntensity);
                color *= aoColor;

                // Shadow Tint overlay (simplified)
                color = lerp(color, _ShadowTint.rgb, _ShadowTint.a);

                if (_AoRadiusDebug > 0.5)
                    color = lerp(color, float3(0,0,1), 1 - aoIntensity);

                clip(alpha - _AlphaClip);

                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
