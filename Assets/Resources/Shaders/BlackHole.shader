// Чёрная дыра (сборка — BlackHoleModel.cs): трассировка лучей в искривлённом пространстве внутри сферы-оболочки.
// Единицы шейдера — радиусы Шварцшильда (rs = 1), оболочка — радиус 10 (примитив-сфера Unity радиуса 0,5 ×20).
// Луч гнётся приближённым уравнением геодезической (a = −1,5·h²·r/|r|⁵): диск за дырой встаёт дугой над и под
// горизонтом, вокруг тени — фотонное кольцо. Диск — в плоскости XZ объекта: турбулентная плазма с кеплеровским
// вращением (внутри быстрее), доплеровским усилением набегающей стороны и гравитационным красным смещением.
// Время — _BHTime (unscaledTime, задаёт BlackHoleFacing): дыра живёт и на паузе. _Fade — проявление на карте.
Shader "Astropolity/FX/BlackHole"
{
    Properties
    {
        _Hot ("Hot", Color) = (1, 0.9, 0.72, 1)
        _Warm ("Warm", Color) = (1, 0.48, 0.12, 1)
        _Deep ("Deep", Color) = (0.6, 0.1, 0.02, 1)
        _Brightness ("Brightness", Float) = 1.7
        _InnerR ("Disk inner radius (rs)", Float) = 2.6
        _OuterR ("Disk outer radius (rs)", Float) = 8.5
        _Spin ("Rotation speed", Float) = 2.5
        _Doppler ("Doppler beaming", Range(0, 1)) = 0.7
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Front
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _Hot, _Warm, _Deep;
            float _Brightness, _InnerR, _OuterR, _Spin, _Doppler, _Fade;
            float _BHTime;

            static const float Bound = 10.0;
            static const float Scale = 20.0;   // объект → rs

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 obj : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.obj = v.vertex.xyz * Scale;
                return o;
            }

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash(i), hash(i + float3(1, 0, 0)), f.x),
                                 lerp(hash(i + float3(0, 1, 0)), hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(hash(i + float3(0, 0, 1)), hash(i + float3(1, 0, 1)), f.x),
                                 lerp(hash(i + float3(0, 1, 1)), hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            // Турбулентность диска: по углу частота низкая, по радиусу высокая — тонкие струи вдоль орбит (как в «Интерстелларе»).
            // Угол — через cos/sin, без шва
            float diskNoise(float rd, float ang, float seed)
            {
                float3 p = float3(cos(ang) * 1.6, sin(ang) * 1.6, rd * 4.5 + seed);
                float s = 0.0, a = 0.5;
                [unroll] for (int o = 0; o < 4; o++)
                {
                    s += a * vnoise(p);
                    p = p * 2.03 + float3(1.7, 9.2, 3.1);
                    a *= 0.5;
                }
                return s;
            }

            void shadeDisk(float3 hp, float3 rayDir, inout float3 col, inout float alpha)
            {
                float rd = length(hp.xz);
                if (rd < _InnerR || rd > _OuterR) return;
                float x = (rd - _InnerR) / (_OuterR - _InnerR);

                // Кеплеровское вращение: ω ∝ r^−3/2. Узор вращается двумя фазами со сдвигом и перетекает
                // из одной в другую — иначе за минуты дифференциальное вращение закрутит его в мелкую рябь
                float omega = _Spin * 1.1 * pow(rd, -1.5);
                const float P = 14.0;
                float ph = frac(_BHTime / P);
                float ang = atan2(hp.z, hp.x);
                float n1 = diskNoise(rd, ang - omega * ph * P, 0.0);
                float n2 = diskNoise(rd, ang - omega * frac(ph + 0.5) * P, 37.0);
                float w = 1.0 - abs(2.0 * ph - 1.0);
                float n = lerp(n2, n1, w);
                float streak = 0.35 + 1.3 * smoothstep(0.25, 0.85, n);

                // Температура: горячо у внутреннего края, к внешнему остывает
                float edge = smoothstep(0.0, 0.05, x) * smoothstep(1.0, 0.55, x);
                float temp = pow(_InnerR / rd, 0.9);

                // Доплер: вещество летит по орбите против часовой стрелки (если смотреть с +Y);
                // к наблюдателю фотон летит против направления луча
                float beta = sqrt(0.5 / rd) * 0.75;
                float3 tdir = normalize(float3(-hp.z, 0.0, hp.x));
                float cosT = dot(tdir, -rayDir);
                float D = sqrt(1.0 - beta * beta) / (1.0 - beta * cosT);
                float beam = lerp(1.0, D * D * D, _Doppler);
                float grav = 1.0 - 1.0 / rd;   // гравитационное красное смещение (квадрат)

                float t = saturate(temp * lerp(1.0, D, _Doppler * 0.6));
                float3 c = lerp(_Deep.rgb, _Warm.rgb, smoothstep(0.25, 0.6, t));
                c = lerp(c, _Hot.rgb, smoothstep(0.6, 1.0, t));

                float3 emit = c * (temp * streak * beam * grav * edge);
                float a = edge * saturate(0.55 + 0.6 * n);
                col += (1.0 - alpha) * emit;
                alpha += (1.0 - alpha) * a;
            }

            // Водоворот внутри тени (художественный, в реальности там чернота): спиральные рукава тусклого вещества
            // закручиваются к центру и тонут в черноте; у края тени — тлеющий ободок. Координаты экранные:
            // угол вокруг центра дыры и доля прицельного параметра от радиуса тени (≈ 2,6 rs)
            float3 shadowVortex(float3 dir, float3 camO, float b)
            {
                float3 c0 = unity_OrthoParams.w > 0.5 ? dir : normalize(-camO);
                float3 rt = normalize(cross(float3(0, 1, 0), c0));
                float3 up = cross(c0, rt);
                float3 o = dir - dot(dir, c0) * c0;
                float phi = atan2(dot(o, up), dot(o, rt));
                float sb = clamp(b / 2.6, 0.001, 1.0);
                float ls = log(sb);
                float sp = phi * 3.0 + ls * 5.0 - _BHTime * 1.5;
                float nz = vnoise(float3(cos(sp) * 1.5, sin(sp) * 1.5, ls * 3.0 - _BHTime * 0.4));
                float band = 0.5 + 0.5 * cos(sp + nz * 3.0);
                float I = pow(sb, 5.0) * (0.1 + 0.9 * band * band * nz) * 0.5 + exp(-(1.0 - sb) * 30.0) * 0.35;
                return lerp(_Deep.rgb * 0.7, _Warm.rgb, pow(sb, 6.0)) * I;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 camO = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz * Scale;
                float3 dir, ro;
                if (unity_OrthoParams.w > 0.5)
                {
                    float3 fwdW = -UNITY_MATRIX_V[2].xyz;
                    dir = normalize(mul((float3x3)unity_WorldToObject, fwdW));
                    ro = i.obj - dir * (Bound * 3.0);
                }
                else
                {
                    dir = normalize(i.obj - camO);
                    ro = camO;
                }

                // Вход в оболочку (камера может быть и внутри)
                float b = dot(ro, dir);
                float c = dot(ro, ro) - Bound * Bound;
                float h = b * b - c;
                if (h < 0.0) discard;
                ro += dir * max(-b - sqrt(h), 0.0);

                float3 pos = ro, vel = dir;
                float3 L = cross(pos, vel);
                float h2 = dot(L, L);
                float3 col = 0;
                float alpha = 0.0;
                float rmin = Bound;
                float ring = 0.0;

                [loop] for (int s = 0; s < 180; s++)
                {
                    float r2 = dot(pos, pos);
                    float r = sqrt(r2);
                    rmin = min(rmin, r);
                    if (r < 1.0)   // за горизонтом — тень с водоворотом падающего вещества
                    {
                        col += (1.0 - alpha) * shadowVortex(dir, camO, sqrt(h2));
                        alpha = 1.0;
                        break;
                    }

                    float dt = clamp(0.09 * r, 0.03, 0.8);
                    float3 prev = pos;
                    vel += (-1.5 * h2 / (r2 * r2 * r)) * pos * dt;
                    pos += vel * dt;

                    // Фотонное кольцо: лучи, обходящие дыру у фотонной сферы (1,5 rs), чуть светятся
                    // (только вырвавшиеся — упавшие в горизонт остаются тенью)
                    float pr = r - 1.5;
                    ring += (1.0 - alpha) * 0.12 * dt * exp(-pr * pr * 60.0);

                    if (prev.y * pos.y < 0.0)
                    {
                        float f = prev.y / (prev.y - pos.y);
                        shadeDisk(lerp(prev, pos, f), normalize(vel), col, alpha);
                    }
                    if (alpha > 0.995) break;
                    if (r > Bound + 0.5 && dot(pos, vel) > 0.0) break;
                }

                col += _Hot.rgb * ring * (1.0 - alpha);
                // Мягкое тёплое гало вокруг тени
                col += (1.0 - alpha) * _Warm.rgb * 0.06 * exp(-(rmin - 1.0) * 0.6) * smoothstep(Bound, Bound * 0.6, rmin);

                col *= _Brightness;
                col = 1.0 - exp(-col);   // мягкий тонмаппинг: без плоских пересветов
                return float4(col, alpha) * _Fade;
            }
            ENDCG
        }
    }
}
