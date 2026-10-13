// Чёрная дыра в виде системы (модель Resources/Models/BlackHole, сборка — BlackHoleModel.cs).
// Слои светящегося диска — поверхности вращения: v < 0,5 — плоский диск (v = 0 у внешнего края, 0,5 — у горизонта),
// v ≥ 0,5 — «купол», изогнутый над горизонтом. Диск светится по радиальной палитре (_Deep → _Warm → _Hot),
// купол — только у силуэта (френель): под низким углом камеры это дуги гравитационной линзы над и под горизонтом,
// сверху купол прозрачен и виден чёрный горизонт. Полосы исходной текстуры — яркостью (_Detail).
// _TexColor = 1 — вместо палитры цвет самой текстуры (внешнее кольцо, обломки). _Tint.a — общая яркость слоя,
// _Fade — проявление модели (на карте задаётся каждому экземпляру через MaterialPropertyBlock).
Shader "Astropolity/FX/BlackHole"
{
    Properties
    {
        _MainTex ("Bands", 2D) = "white" {}
        _Hot ("Hot", Color) = (1, 0.86, 0.62, 1)
        _Warm ("Warm", Color) = (1, 0.55, 0.2, 1)
        _Deep ("Deep", Color) = (0.5, 0.1, 0.03, 1)
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Disk intensity", Float) = 1
        _DomeGain ("Lens arc gain", Float) = 1.4
        _RimPow ("Lens arc sharpness", Float) = 3
        _Detail ("Band detail", Range(0, 1)) = 0.6
        _TexColor ("Use texture colour", Range(0, 1)) = 0
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Hot, _Warm, _Deep, _Tint;
            float _Intensity, _DomeGain, _RimPow, _Detail, _TexColor, _Fade;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                float3 wn  : TEXCOORD1;
                float3 wv  : TEXCOORD2;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wv = WorldSpaceViewDir(v.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float v = i.uv.y;
                float4 tx = tex2D(_MainTex, i.uv);
                float lum = dot(tx.rgb, float3(0.3, 0.59, 0.11));
                float detail = (1.0 - _Detail + _Detail * lum * 1.6) * tx.a;

                float facing = abs(dot(normalize(i.wn), normalize(i.wv)));
                float rim = pow(1.0 - saturate(facing), _RimPow);

                float dome = step(0.5, v);
                float t = saturate(v * 2.0);
                float3 ramp = lerp(_Deep.rgb, _Warm.rgb, smoothstep(0.0, 0.55, t));
                ramp = lerp(ramp, _Hot.rgb, smoothstep(0.55, 1.0, t));

                float3 disk = ramp * (1.0 - dome) * smoothstep(0.0, 0.2, t) * _Intensity;
                float3 lens = _Hot.rgb * dome * rim * _DomeGain;
                float3 col = (disk + lens) * detail;
                col = lerp(col, tx.rgb * tx.a * _Intensity, _TexColor);
                return float4(col * _Tint.rgb * (_Tint.a * _Fade), 1.0);
            }
            ENDCG
        }
    }
}
