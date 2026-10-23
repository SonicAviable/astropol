Shader "Astropolity/UI/PlanetDisc"
{
    // Планета из рендер-текстуры голограммы на экране планеты.
    //  • Мягкий круглый край по силуэту: снаружи диска прозрачно, даже если текстура непрозрачна
    //    (камера голограммы может писать альфу 1 — тогда вокруг планеты был бы чёрный квадрат).
    //  • Атмосферная подсветка лимба цветом мира — тонкое свечение по краю диска.
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Radius ("Disc Radius (uv)", Float) = 0.456
        _Feather ("Edge Feather (uv)", Float) = 0.003
        _RimColor ("Rim Color", Color) = (0.4, 0.85, 0.95, 1)
        _RimPower ("Rim Power", Float) = 6
        _RimStrength ("Rim Strength", Float) = 0.45

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPosition : TEXCOORD1; };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _ClipRect;
            float _Radius, _Feather, _RimPower, _RimStrength;
            fixed4 _RimColor;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float d = distance(i.uv, float2(0.5, 0.5));
                float disc = 1.0 - smoothstep(_Radius - _Feather, _Radius + _Feather, d);
                float rim = pow(saturate(d / _Radius), _RimPower) * disc;
                c.rgb += _RimColor.rgb * rim * _RimStrength;
                c.a *= disc;
                c *= i.color;
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
