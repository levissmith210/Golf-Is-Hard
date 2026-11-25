Shader "Golf/Reward Bubble"
{
    Properties
    {
        _Color ("Glass tint", Color) = (0.45,0.94,1,0.16)
        _RimColor ("Pearlescent rim", Color) = (1,0.86,0.36,0.8)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct Output { float4 position : SV_POSITION; float3 normal : TEXCOORD0; float3 view : TEXCOORD1; };
            fixed4 _Color;
            fixed4 _RimColor;
            Output vert(Input v)
            {
                Output o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = _WorldSpaceCameraPos.xyz - mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            fixed4 frag(Output i) : SV_Target
            {
                float3 normal = normalize(i.normal);
                float rim = pow(1 - saturate(dot(normal, normalize(i.view))), 2.4);
                float shine = pow(saturate(dot(normal, normalize(float3(-0.45,0.7,-0.5)))), 28);
                fixed4 color;
                color.rgb = lerp(_Color.rgb, _RimColor.rgb, rim) + shine * 0.45;
                color.a = saturate(_Color.a + rim * _RimColor.a + shine * 0.35);
                return color;
            }
            ENDCG
        }
    }
}
