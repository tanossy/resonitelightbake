// Marker shader identifying materials that should be converted with a baked lightmap folded
// into the Resonite PBS_MultiUV_Metallic SecondaryAlbedo slot (UV1). Mirrors Unity's built-in
// "Standard" shader's property set 1:1 (see LightmapMaterialCache.GetVariantOrOriginal, which
// clones a Standard material and only swaps the `shader` reference), plus two extra properties
// carrying the baked lightmap texture and its ScaleOffset.
//
// This shader is never sent to Renderite - BakedLightmapStandardConverter (keyed on this
// shader's name via MaterialConverterAttribute) reads the material's properties directly via
// the Material API. The forward pass here exists only so the material doesn't render as
// magenta/invalid in the Unity scene view; it is Built-in Render Pipeline-only and will still
// show as invalid under URP/HDRP, with no effect on conversion.
Shader "ResoniteSDK/BakedLightmapStandard"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}

        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Mode ("Rendering Mode", Float) = 0.0

        _Glossiness ("Smoothness", Range(0.0, 1.0)) = 0.5
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap ("Metallic", 2D) = "white" {}

        _BumpScale ("Scale", Float) = 1.0
        _BumpMap ("Normal Map", 2D) = "bump" {}

        _OcclusionMap ("Occlusion", 2D) = "white" {}

        _EmissionColor ("Color", Color) = (0,0,0,1)
        _EmissionMap ("Emission", 2D) = "white" {}

        // Baked lightmap injected by LightmapMaterialCache from
        // LightmapSettings.lightmaps[renderer.lightmapIndex].lightmapColor
        // and renderer.lightmapScaleOffset.
        _BakedLightmap ("Baked Lightmap", 2D) = "white" {}
        _BakedLightmapST ("Lightmap ScaleOffset", Vector) = (1,1,0,0)

        // Desaturated (luma-only) companion of _BakedLightmap, same UV/ScaleOffset. Property
        // storage only - not sampled by this preview fragment shader, read back by
        // BakedLightmapStandardConverter for the additive-fill slot instead.
        _BakedLightmapGray ("Baked Lightmap (Desaturated)", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float2 uvLightmap : TEXCOORD1;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            sampler2D _BakedLightmap;
            float4 _BakedLightmapST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uvLightmap = v.uv1 * _BakedLightmapST.xy + _BakedLightmapST.zw;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;
                fixed4 lightmap = tex2D(_BakedLightmap, i.uvLightmap);
                albedo.rgb *= lightmap.rgb;
                return albedo;
            }
            ENDCG
        }
    }

    Fallback "Standard"
}
