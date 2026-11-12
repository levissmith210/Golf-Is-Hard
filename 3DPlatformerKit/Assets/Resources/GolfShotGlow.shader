Shader "Golf/Shot Glow"
{
    Properties { _Color ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            fixed4 _Color;
            Output vert(Input v)
            {
                Output o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(Output i) : SV_Target
            {
                // Feather the ribbon edges; particles use the same soft luminous shape.
                fixed4 c = i.color;
                c.a *= smoothstep(0, 0.22, i.uv.y) * smoothstep(0, 0.22, 1 - i.uv.y);
                return c;
            }
            ENDCG
        }
    }
}
