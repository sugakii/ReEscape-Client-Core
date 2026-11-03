Shader "ReEscape/UI/DeathSignal"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Progress ("Progress", Range(0,1)) = 0
        _UseVideo ("Use Video", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            float _Progress;
            float _UseVideo;
            sampler2D _MainTex;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            fixed4 frag(v2f i) : SV_Target
            {
                if(_UseVideo > 0.5)
                    return fixed4(tex2D(_MainTex,i.uv).rgb * (1-smoothstep(0.94,1,_Progress)),1);
                float frame=floor(_Progress*28);
                float grain=hash(floor(i.uv*float2(640,360))+frame*13.7);
                float band=step(0.87,hash(float2(floor(i.uv.y*36),frame)));
                float scan=0.65+0.35*step(0.5,frac(i.uv.y*360));
                float fade=1-smoothstep(0.72,1,_Progress);
                float signal=(grain*0.5+band*0.16)*scan*fade;
                return fixed4(signal*0.88,signal*0.96,signal,1);
            }
            ENDCG
        }
    }
}
