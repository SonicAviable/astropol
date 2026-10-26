using UnityEngine;

namespace StellarisClone.Ground
{
    /// <summary>Части модели, которые двигает процедурная анимация.</summary>
    public sealed class UnitRig
    {
        public Transform Root, Hips, Torso, Head, LegL, LegR, ArmL, ArmR, Gun, Muzzle, Turret, Barrel, Banner;
        public Renderer[] Renderers;
        /// <summary>Основной материал брони (для обугливания огнём — подменяется на тёмный).</summary>
        public Material Armor;
        public float Height = 1.8f;
        /// <summary>Положения покоя (от них считаются шаг, отдача и откат ствола).</summary>
        public float HipsY = 0.95f;
        public Vector3 GunRest, BarrelRest;
    }

    /// <summary>
    /// Процедурные модели бойцов и техники в «героическом» масштабе, как в Dawn of War: массивная броня
    /// со скруглёнными рёбрами (блики на гранях), огромные наплечники, шлем с горящими линзами, ранец,
    /// отделка фракционным металлом (золото Астреи, бронза Ксарна, хром «Ноля»…), оружие с деталями.
    /// Техника: корпус со скосами, гусеницы под щитками с катками, башня с маской орудия, выхлопы, ящики, фары.
    /// Силуэт по роли: тяжёлая пехота шире, снайпер тонкий с длинным стволом, сержант — с гребнем и штандартом,
    /// генерал — огромный, в плаще, со светящимся клинком. Коллайдеров нет: попадания считаются по позициям.
    /// </summary>
    public static class UnitMeshFactory
    {
        /// <summary>Пехота крупнее реального роста — читается с RTS-камеры.</summary>
        public const float Heroic = 1.22f;

        public static UnitRig Build(Transform parent, UnitDef def, Color faction, bool sergeant, string factionKey)
        {
            if (def.Role == UnitRole.Swarm) return Drone(parent, faction);
            if (def.Vehicle)
            {
                switch (def.Role)
                {
                    case UnitRole.Tank: return Tank(parent, faction, factionKey);
                    case UnitRole.Artillery: return Artillery(parent, faction, factionKey);
                    case UnitRole.Walker: return Walker(parent, faction, factionKey);
                    default: return Apc(parent, faction, factionKey);
                }
            }
            return Infantry(parent, def, faction, sergeant, factionKey);
        }

        // ================================================================ Палитра

        /// <summary>Цвет брони: оттенок фракции, но не выбеленный — насыщенный, средней яркости (как краска на керамите).</summary>
        public static Color ArmorColor(Color faction)
        {
            Color.RGBToHSV(faction, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp(s * 1.05f, 0.4f, 0.9f), Mathf.Clamp(v * 0.62f, 0.3f, 0.56f));
        }

        private static Material Armor(Color faction) => GroundMats.Lit(ArmorColor(faction), 0.32f, 0.58f);
        private static Material ArmorDark(Color faction) => GroundMats.Lit(ArmorColor(faction) * 0.55f, 0.35f, 0.5f);
        private static Material Dark => GroundMats.Lit(new Color(0.085f, 0.09f, 0.1f), 0.75f, 0.5f);
        private static Material Under => GroundMats.Lit(new Color(0.05f, 0.05f, 0.055f), 0.2f, 0.3f);
        private static Material Gunmetal => GroundMats.Lit(new Color(0.16f, 0.165f, 0.18f), 0.85f, 0.62f);

        /// <summary>Отделка: металл фракции.</summary>
        private static Material Trim(string key) => key switch
        {
            "xarn" => GroundMats.Lit(new Color(0.55f, 0.28f, 0.14f), 0.9f, 0.6f),
            "aquila" => GroundMats.Lit(new Color(0.72f, 0.56f, 0.3f), 0.9f, 0.65f),
            "iridia" => GroundMats.Lit(new Color(0.7f, 0.66f, 0.82f), 0.95f, 0.75f),
            "terraan" => GroundMats.Lit(new Color(0.72f, 0.67f, 0.52f), 0.05f, 0.35f),
            "nol" => GroundMats.Lit(new Color(0.82f, 0.84f, 0.87f), 1f, 0.82f),
            _ => GroundMats.Lit(new Color(0.8f, 0.6f, 0.24f), 0.95f, 0.7f)
        };

        private static Color Lens(string key) => key switch
        {
            "xarn" => new Color(1f, 0.22f, 0.12f),
            "nol" => new Color(0.6f, 0.95f, 1f),
            "terraan" => new Color(0.55f, 1f, 0.4f),
            "iridia" => new Color(0.85f, 0.5f, 1f),
            "aquila" => new Color(1f, 0.78f, 0.3f),
            _ => new Color(0.35f, 0.9f, 1f)
        };

        private static Transform Pivot(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        private static Transform B(Transform p, Vector3 pos, Vector3 size, Material m, string name = "Box", float bevel = 0.035f, Quaternion? rot = null)
            => GroundMats.Box(p, pos, size, m, name, rot, bevel);

        private static Transform S(Transform p, Vector3 pos, Vector3 scale, Material m, string name = "Sphere", Quaternion? rot = null)
            => GroundMats.Part(p, PrimitiveType.Sphere, pos, scale, m, name, rot);

        private static Transform C(Transform p, Vector3 pos, Vector3 scale, Material m, string name = "Cyl", Quaternion? rot = null)
            => GroundMats.Part(p, PrimitiveType.Cylinder, pos, scale, m, name, rot);

        private static readonly Quaternion AlongZ = Quaternion.Euler(90f, 0f, 0f);

        // ================================================================ Пехота

        private static UnitRig Infantry(Transform parent, UnitDef def, Color faction, bool sergeant, string factionKey)
        {
            var rig = new UnitRig();
            float b = def.Bulk, s = def.Scale * Heroic;
            bool militia = def.Role == UnitRole.Militia;
            bool hero = def.Role == UnitRole.Hero;
            bool heavy = def.Role == UnitRole.Heavy || hero;
            var root = Pivot(parent, "Model", Vector3.zero);
            root.localScale = Vector3.one * s;
            rig.Root = root;
            rig.Height = 1.95f * s;
            rig.HipsY = 0.95f;

            var armor = militia ? GroundMats.Lit(Color.Lerp(ArmorColor(faction), new Color(0.36f, 0.34f, 0.28f), 0.65f), 0f, 0.25f) : Armor(faction);
            rig.Armor = armor;
            var armorDk = militia ? GroundMats.Lit(new Color(0.22f, 0.21f, 0.18f), 0f, 0.2f) : ArmorDark(faction);
            var trim = militia ? Dark : Trim(factionKey);
            var dark = Dark;
            var under = Under;
            var gun = Gunmetal;
            Color lensC = Lens(factionKey);
            var lens = GroundMats.Glow(lensC * 2.4f);

            // ---- Ноги ----
            rig.Hips = Pivot(root, "Hips", new Vector3(0f, rig.HipsY, 0f));
            rig.LegL = Pivot(rig.Hips, "LegL", new Vector3(-0.14f * b, 0f, 0f));
            rig.LegR = Pivot(rig.Hips, "LegR", new Vector3(0.14f * b, 0f, 0f));
            foreach (var leg in new[] { rig.LegL, rig.LegR })
            {
                B(leg, new Vector3(0f, -0.22f, 0f), new Vector3(0.22f * b, 0.44f, 0.26f), militia ? under : armor, "Thigh", 0.05f);
                B(leg, new Vector3(0f, -0.47f, 0.1f), new Vector3(0.2f * b, 0.17f, 0.12f), militia ? dark : armorDk, "Knee", 0.05f);
                B(leg, new Vector3(0f, -0.68f, 0.02f), new Vector3(0.21f * b, 0.4f, 0.25f), militia ? under : armor, "Shin", 0.05f);
                B(leg, new Vector3(0f, -0.9f, 0.07f), new Vector3(0.25f * b, 0.12f, 0.38f), dark, "Boot", 0.04f);
            }

            // ---- Таз и пояс ----
            B(rig.Hips, new Vector3(0f, 0f, 0f), new Vector3(0.44f * b, 0.2f, 0.3f), under, "Pelvis");
            B(rig.Hips, new Vector3(0f, 0.08f, 0f), new Vector3(0.5f * b, 0.08f, 0.34f), dark, "Belt", 0.02f);
            if (!militia)
            {
                B(rig.Hips, new Vector3(0f, 0.08f, 0.175f), new Vector3(0.11f, 0.09f, 0.03f), trim, "Buckle", 0.015f);
                B(rig.Hips, new Vector3(-0.15f * b, -0.12f, 0.06f), new Vector3(0.18f * b, 0.2f, 0.26f), armor, "TassetL", 0.04f, Quaternion.Euler(0f, 0f, 8f));
                B(rig.Hips, new Vector3(0.15f * b, -0.12f, 0.06f), new Vector3(0.18f * b, 0.2f, 0.26f), armor, "TassetR", 0.04f, Quaternion.Euler(0f, 0f, -8f));
            }

            // ---- Корпус ----
            rig.Torso = Pivot(rig.Hips, "Torso", new Vector3(0f, 0.05f, 0f));
            B(rig.Torso, new Vector3(0f, 0.14f, 0f), new Vector3(0.4f * b, 0.2f, 0.28f), under, "Abdomen");
            float chestD = 0.4f * Mathf.Sqrt(b);
            B(rig.Torso, new Vector3(0f, 0.42f, 0.02f), new Vector3(0.58f * b, 0.42f, chestD), armor, "Chest", 0.08f);
            if (militia)
            {
                // Бронежилет с подсумками
                for (int i = -1; i <= 1; i++)
                    B(rig.Torso, new Vector3(i * 0.13f, 0.3f, chestD * 0.5f + 0.03f), new Vector3(0.1f, 0.12f, 0.07f), dark, "Pouch", 0.02f);
            }
            else
            {
                // Нагрудный знак — «крылья» и ядро из металла фракции
                B(rig.Torso, new Vector3(0f, 0.5f, chestD * 0.5f + 0.012f), new Vector3(0.3f * b, 0.07f, 0.04f), trim, "Wings", 0.015f);
                B(rig.Torso, new Vector3(0f, 0.44f, chestD * 0.5f + 0.02f), new Vector3(0.08f, 0.12f, 0.05f), trim, "Core", 0.02f);
                B(rig.Torso, new Vector3(0f, 0.66f, 0f), new Vector3(0.34f, 0.1f, 0.3f), dark, "Gorget", 0.03f);
            }
            // Ранец с выхлопами
            var pack = B(rig.Torso, new Vector3(0f, 0.46f, -chestD * 0.5f - 0.11f), new Vector3(0.48f * b, 0.48f, 0.22f), militia ? dark : armorDk, "Pack", 0.06f);
            if (!militia)
            {
                C(rig.Torso, new Vector3(-0.13f * b, 0.76f, -chestD * 0.5f - 0.16f), new Vector3(0.1f, 0.09f, 0.1f), gun, "Vent");
                C(rig.Torso, new Vector3(0.13f * b, 0.76f, -chestD * 0.5f - 0.16f), new Vector3(0.1f, 0.09f, 0.1f), gun, "Vent");
            }

            // Наплечники: купол из цвета брони с окантовкой металлом
            if (!militia && def.Role != UnitRole.Sniper && def.Role != UnitRole.Seer)
            {
                float pad = heavy ? 1.45f : 1f;
                foreach (float side in new[] { -1f, 1f })
                {
                    var p = Pivot(rig.Torso, side < 0 ? "PadL" : "PadR", new Vector3(side * (0.36f * b + 0.04f * pad), 0.6f, 0f));
                    p.localRotation = Quaternion.Euler(0f, 0f, side * -14f);
                    S(p, Vector3.zero, new Vector3(0.36f, 0.32f, 0.42f) * pad, armor, "Pauldron");
                    B(p, new Vector3(0f, -0.08f * pad, 0f), new Vector3(0.37f, 0.06f, 0.43f) * pad, trim, "PadRim", 0.025f);
                    if (sergeant || hero) B(p, new Vector3(side * 0.12f * pad, 0.05f * pad, 0f), new Vector3(0.03f, 0.12f, 0.16f) * pad, trim, "Mark", 0.01f);
                }
            }

            // ---- Голова ----
            rig.Head = Pivot(rig.Torso, "Head", new Vector3(0f, 0.8f, 0.02f));
            if (militia)
            {
                S(rig.Head, Vector3.zero, new Vector3(0.24f, 0.27f, 0.26f), GroundMats.Lit(new Color(0.62f, 0.47f, 0.37f), 0f, 0.35f), "Face");
                S(rig.Head, new Vector3(0f, 0.07f, -0.01f), new Vector3(0.3f, 0.2f, 0.32f), armorDk, "Helmet");
                B(rig.Head, new Vector3(0f, 0.02f, 0.13f), new Vector3(0.18f, 0.04f, 0.05f), dark, "Goggles", 0.015f);
            }
            else
            {
                S(rig.Head, Vector3.zero, new Vector3(0.28f, 0.3f, 0.3f), armor, "Helmet");
                B(rig.Head, new Vector3(0f, -0.05f, 0.1f), new Vector3(0.2f, 0.17f, 0.14f), armor, "Faceplate", 0.05f);
                B(rig.Head, new Vector3(0f, -0.1f, 0.17f), new Vector3(0.1f, 0.06f, 0.04f), dark, "Grille", 0.015f);
                B(rig.Head, new Vector3(-0.058f, 0.005f, 0.158f), new Vector3(0.07f, 0.04f, 0.03f), lens, "LensL", 0.01f);
                B(rig.Head, new Vector3(0.058f, 0.005f, 0.158f), new Vector3(0.07f, 0.04f, 0.03f), lens, "LensR", 0.01f);
                if (sergeant || hero)
                    B(rig.Head, new Vector3(0f, 0.17f, -0.03f), new Vector3(0.05f, 0.1f, 0.34f), hero ? trim : GroundMats.Lit(new Color(0.55f, 0.06f, 0.05f), 0f, 0.2f), "Crest", 0.02f);
            }

            // ---- Руки (в позе «оружие у плеча») ----
            rig.ArmR = Pivot(rig.Torso, "ArmR", new Vector3(0.34f * b, 0.58f, 0f));
            rig.ArmL = Pivot(rig.Torso, "ArmL", new Vector3(-0.34f * b, 0.58f, 0f));
            foreach (var arm in new[] { rig.ArmR, rig.ArmL })
            {
                B(arm, new Vector3(0f, -0.16f, 0f), new Vector3(0.15f * b, 0.3f, 0.16f), militia ? under : armor, "Upper", 0.04f);
                B(arm, new Vector3(0f, -0.42f, 0.02f), new Vector3(0.17f * b, 0.27f, 0.19f), militia ? under : armorDk, "Gauntlet", 0.045f);
                B(arm, new Vector3(0f, -0.58f, 0.03f), new Vector3(0.12f, 0.1f, 0.13f), dark, "Hand", 0.03f);
            }
            rig.ArmR.localRotation = Quaternion.Euler(-70f, 0f, 0f);
            rig.ArmL.localRotation = Quaternion.Euler(-62f, 28f, 0f);

            // ---- Оружие ----
            rig.Gun = Pivot(rig.Torso, "Gun", new Vector3(0.13f * b, 0.42f, 0.3f));
            float muzzle;
            switch (def.Role)
            {
                case UnitRole.MachineGun:
                    B(rig.Gun, new Vector3(0f, 0f, 0.22f), new Vector3(0.16f, 0.22f, 0.66f), gun, "Receiver", 0.03f);
                    C(rig.Gun, new Vector3(0f, 0.03f, 0.72f), new Vector3(0.11f, 0.26f, 0.11f), dark, "Barrel", AlongZ);
                    C(rig.Gun, new Vector3(0f, 0.03f, 0.98f), new Vector3(0.14f, 0.04f, 0.14f), gun, "Brake", AlongZ);
                    B(rig.Gun, new Vector3(-0.13f, -0.1f, 0.12f), new Vector3(0.16f, 0.24f, 0.24f), trim, "Drum", 0.04f);
                    B(rig.Gun, new Vector3(0f, 0.15f, 0.08f), new Vector3(0.05f, 0.06f, 0.2f), gun, "Sight", 0.01f);
                    muzzle = 1.02f;
                    break;
                case UnitRole.Rocket:
                    rig.Gun.localPosition = new Vector3(0.24f * b, 0.7f, 0.05f);
                    C(rig.Gun, new Vector3(0f, 0f, 0.15f), new Vector3(0.2f, 0.62f, 0.2f), armorDk, "Tube", AlongZ);
                    C(rig.Gun, new Vector3(0f, 0f, 0.74f), new Vector3(0.24f, 0.06f, 0.24f), trim, "Mouth", AlongZ);
                    B(rig.Gun, new Vector3(0f, -0.14f, 0.18f), new Vector3(0.06f, 0.16f, 0.1f), dark, "Grip", 0.015f);
                    B(rig.Gun, new Vector3(-0.13f, 0.04f, 0.3f), new Vector3(0.05f, 0.09f, 0.14f), lens, "Scope", 0.01f);
                    muzzle = 0.8f;
                    break;
                case UnitRole.Sniper:
                    B(rig.Gun, new Vector3(0f, 0f, 0.3f), new Vector3(0.08f, 0.12f, 0.7f), gun, "Stock", 0.02f);
                    C(rig.Gun, new Vector3(0f, 0.02f, 0.96f), new Vector3(0.045f, 0.36f, 0.045f), dark, "LongBarrel", AlongZ);
                    C(rig.Gun, new Vector3(0f, 0.12f, 0.28f), new Vector3(0.07f, 0.16f, 0.07f), dark, "Scope", AlongZ);
                    B(rig.Gun, new Vector3(0f, 0.12f, 0.45f), new Vector3(0.06f, 0.06f, 0.02f), lens, "Lens", 0.005f);
                    muzzle = 1.32f;
                    break;
                case UnitRole.Flamer:
                    B(rig.Gun, new Vector3(0f, 0f, 0.24f), new Vector3(0.12f, 0.15f, 0.54f), gun, "Flamer", 0.03f);
                    C(rig.Gun, new Vector3(0f, 0f, 0.6f), new Vector3(0.12f, 0.1f, 0.12f), dark, "Nozzle", AlongZ);
                    S(rig.Gun, new Vector3(0f, -0.02f, 0.68f), Vector3.one * 0.07f, GroundMats.Glow(new Color(1f, 0.5f, 0.15f) * 2.5f), "Pilot");
                    var tankMat = GroundMats.Lit(new Color(0.5f, 0.16f, 0.06f), 0.5f, 0.55f);
                    GroundMats.Part(pack, PrimitiveType.Capsule, new Vector3(-0.1f, 0f, -0.14f), new Vector3(0.16f, 0.24f, 0.16f), tankMat, "FuelL");
                    GroundMats.Part(pack, PrimitiveType.Capsule, new Vector3(0.1f, 0f, -0.14f), new Vector3(0.16f, 0.24f, 0.16f), tankMat, "FuelR");
                    muzzle = 0.72f;
                    break;
                case UnitRole.Heavy:
                case UnitRole.Hero:
                {
                    B(rig.Gun, new Vector3(0f, 0f, 0.12f), new Vector3(0.11f, 0.15f, 0.34f), gun, "Pistol", 0.03f);
                    C(rig.Gun, new Vector3(0f, 0.02f, 0.32f), new Vector3(0.06f, 0.06f, 0.06f), dark, "Muzzle", AlongZ);
                    // Силовой клинок: гарда металлом фракции, светящееся лезвие
                    var blade = Pivot(rig.ArmL, "Blade", new Vector3(0f, -0.6f, 0.08f));
                    float len = hero ? 1.35f : 0.95f;
                    B(blade, new Vector3(0f, 0f, 0.05f), new Vector3(0.22f, 0.05f, 0.09f), trim, "Guard", 0.015f);
                    B(blade, new Vector3(0f, -len * 0.5f, 0.06f), new Vector3(0.045f, len, 0.15f), GroundMats.Glow(lensC * (hero ? 3f : 1.9f)), "Edge", 0.012f);
                    if (hero)
                    {
                        // Плащ, лавровый венец-гребень и знамя за спиной
                        B(rig.Torso, new Vector3(0f, 0.05f, -chestD * 0.5f - 0.24f), new Vector3(0.66f * b, 1.25f, 0.05f), GroundMats.Lit(Color.Lerp(faction, Color.black, 0.55f), 0f, 0.2f), "Cape", 0.02f, Quaternion.Euler(12f, 0f, 0f));
                        B(rig.Torso, new Vector3(0f, 0.92f, -chestD * 0.5f - 0.1f), new Vector3(0.42f, 0.38f, 0.06f), trim, "Halo", 0.03f);
                    }
                    muzzle = 0.36f;
                    break;
                }
                case UnitRole.Seer:
                    B(rig.Torso, new Vector3(0f, -0.25f, 0f), new Vector3(0.64f, 0.7f, 0.5f), GroundMats.Lit(Color.Lerp(faction, Color.black, 0.5f), 0f, 0.25f), "Robe", 0.12f);
                    C(rig.Gun, new Vector3(0f, -0.4f, 0.05f), new Vector3(0.05f, 0.8f, 0.05f), trim, "Staff");
                    S(rig.Gun, new Vector3(0f, 0.45f, 0.05f), Vector3.one * 0.24f, GroundMats.Glow(lensC * 3f), "Orb");
                    muzzle = 0.45f;
                    break;
                default:
                    // Болтер: короб, ствол с дульным тормозом, магазин, прицел
                    B(rig.Gun, new Vector3(0f, 0f, 0.16f), new Vector3(0.12f, 0.18f, 0.48f), gun, "Receiver", 0.03f);
                    B(rig.Gun, new Vector3(0.062f, 0.01f, 0.16f), new Vector3(0.012f, 0.1f, 0.36f), militia ? dark : armor, "Casing", 0.005f);
                    C(rig.Gun, new Vector3(0f, 0.03f, 0.5f), new Vector3(0.07f, 0.14f, 0.07f), dark, "Barrel", AlongZ);
                    C(rig.Gun, new Vector3(0f, 0.03f, 0.63f), new Vector3(0.095f, 0.03f, 0.095f), gun, "Brake", AlongZ);
                    B(rig.Gun, new Vector3(0f, -0.15f, 0.12f), new Vector3(0.08f, 0.18f, 0.1f), dark, "Mag", 0.02f, Quaternion.Euler(-10f, 0f, 0f));
                    B(rig.Gun, new Vector3(0f, 0.12f, 0.08f), new Vector3(0.04f, 0.05f, 0.14f), militia ? dark : trim, "Sight", 0.01f);
                    muzzle = 0.68f;
                    break;
            }
            rig.Muzzle = Pivot(rig.Gun, "Muzzle", new Vector3(0f, 0.03f, muzzle));
            rig.GunRest = rig.Gun.localPosition;

            // Сержант — штандарт фракции за спиной (виден издалека)
            if (sergeant && !hero)
            {
                rig.Banner = Pivot(rig.Torso, "Banner", new Vector3(0.14f, 0.55f, -chestD * 0.5f - 0.2f));
                C(rig.Banner, new Vector3(0f, 0.65f, 0f), new Vector3(0.035f, 0.7f, 0.035f), dark, "Pole");
                B(rig.Banner, new Vector3(0f, 1.2f, -0.18f), new Vector3(0.025f, 0.42f, 0.34f), GroundMats.Lit(ArmorColor(faction), 0f, 0.25f, faction * 0.35f), "Flag", 0.01f);
                B(rig.Banner, new Vector3(0f, 1.42f, -0.18f), new Vector3(0.05f, 0.04f, 0.38f), trim, "Bar", 0.01f);
                S(rig.Banner, new Vector3(0f, 1.38f, 0f), Vector3.one * 0.08f, trim, "Finial");
            }
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }

        // ================================================================ Дроны Роя

        private static UnitRig Drone(Transform parent, Color faction)
        {
            var rig = new UnitRig { Height = 1.2f, HipsY = 1.1f };
            var root = Pivot(parent, "Model", Vector3.zero);
            rig.Root = root;
            rig.Hips = Pivot(root, "Hover", new Vector3(0f, 1.1f, 0f));
            rig.Torso = rig.Hips;
            var shell = GroundMats.Lit(new Color(0.72f, 0.75f, 0.8f), 0.9f, 0.78f);
            rig.Armor = shell;
            var eye = GroundMats.Glow(new Color(0.6f, 0.95f, 1f) * 2.6f);
            S(rig.Hips, Vector3.zero, new Vector3(0.5f, 0.3f, 0.62f), shell, "Body");
            B(rig.Hips, new Vector3(0f, 0.12f, -0.05f), new Vector3(0.24f, 0.08f, 0.36f), Dark, "Spine", 0.02f);
            B(rig.Hips, new Vector3(0f, 0f, 0.29f), new Vector3(0.22f, 0.05f, 0.06f), eye, "Eye", 0.01f);
            foreach (float side in new[] { -1f, 1f })
            {
                B(rig.Hips, new Vector3(side * 0.42f, 0.02f, -0.04f), new Vector3(0.4f, 0.03f, 0.18f), Dark, "Blade", 0.01f, Quaternion.Euler(0f, side * 12f, side * -8f));
                C(rig.Hips, new Vector3(side * 0.62f, 0.02f, -0.08f), new Vector3(0.12f, 0.03f, 0.12f), eye, "Thruster");
            }
            rig.Gun = Pivot(rig.Hips, "Gun", new Vector3(0f, -0.1f, 0.3f));
            B(rig.Gun, Vector3.zero, new Vector3(0.05f, 0.05f, 0.22f), Gunmetal, "Emitter", 0.01f);
            rig.Muzzle = Pivot(rig.Gun, "Muzzle", new Vector3(0f, 0f, 0.14f));
            rig.GunRest = rig.Gun.localPosition;
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }

        // ================================================================ Техника

        /// <summary>Гусеница под щитком: короб ленты, катки, ленивец и звёздочка.</summary>
        private static void Track(Transform root, float x, float len, float h, Material armor, Material dark, Material gun)
        {
            B(root, new Vector3(x, h * 0.5f + 0.05f, 0f), new Vector3(0.8f, h, len), dark, "Track", 0.25f);
            int wheels = Mathf.Max(4, Mathf.RoundToInt(len / 1.15f));
            float sign = Mathf.Sign(x);
            for (int i = 0; i < wheels; i++)
            {
                float z = -len * 0.5f + 0.65f + i * (len - 1.3f) / (wheels - 1);
                C(root, new Vector3(x + sign * 0.41f, 0.55f, z), new Vector3(0.78f, 0.06f, 0.78f), gun, "Wheel", Quaternion.Euler(0f, 0f, 90f));
                C(root, new Vector3(x + sign * 0.45f, 0.55f, z), new Vector3(0.26f, 0.03f, 0.26f), dark, "Hub", Quaternion.Euler(0f, 0f, 90f));
            }
            B(root, new Vector3(x, h + 0.12f, 0f), new Vector3(0.98f, 0.12f, len + 0.25f), armor, "Guard", 0.04f);
            B(root, new Vector3(x + sign * 0.5f, h - 0.15f, 0f), new Vector3(0.06f, 0.5f, len * 0.9f), armor, "Skirt", 0.02f);
        }

        private static UnitRig Apc(Transform parent, Color faction, string key)
        {
            var rig = new UnitRig { Height = 2.8f };
            var root = Pivot(parent, "Model", Vector3.zero);
            rig.Root = root;
            var armor = Armor(faction);
            var armorDk = ArmorDark(faction);
            rig.Armor = armor;
            var dark = Dark;
            var gun = Gunmetal;
            var trim = Trim(key);
            var lamp = GroundMats.Glow(new Color(1f, 0.92f, 0.75f) * 2.5f);
            Track(root, -1.35f, 4.9f, 1.05f, armor, dark, gun);
            Track(root, 1.35f, 4.9f, 1.05f, armor, dark, gun);
            // Коробчатый корпус со скошенным носом, люк десанта сзади
            B(root, new Vector3(0f, 1.45f, -0.25f), new Vector3(2.1f, 1.3f, 4.1f), armor, "Hull", 0.12f);
            B(root, new Vector3(0f, 1.3f, 2.05f), new Vector3(2.0f, 1.0f, 0.9f), armor, "Nose", 0.1f, Quaternion.Euler(38f, 0f, 0f));
            B(root, new Vector3(0f, 2.12f, -0.4f), new Vector3(1.7f, 0.12f, 3.2f), armorDk, "Roof", 0.04f);
            B(root, new Vector3(0f, 1.35f, -2.33f), new Vector3(1.5f, 1.1f, 0.12f), armorDk, "Ramp", 0.04f);
            B(root, new Vector3(0f, 1.62f, 1.75f), new Vector3(1.2f, 0.12f, 0.08f), trim, "Stripe", 0.02f);
            B(root, new Vector3(-0.7f, 1.2f, 2.28f), new Vector3(0.22f, 0.14f, 0.1f), lamp, "LampL", 0.03f);
            B(root, new Vector3(0.7f, 1.2f, 2.28f), new Vector3(0.22f, 0.14f, 0.1f), lamp, "LampR", 0.03f);
            C(root, new Vector3(-0.8f, 2.3f, -1.9f), new Vector3(0.18f, 0.32f, 0.18f), gun, "Exhaust");
            C(root, new Vector3(0.8f, 2.3f, -1.9f), new Vector3(0.18f, 0.32f, 0.18f), gun, "Exhaust");
            B(root, new Vector3(0.65f, 2.3f, -1.1f), new Vector3(0.5f, 0.3f, 0.7f), dark, "Stowage", 0.05f);
            // Башенка со спаренным пулемётом
            rig.Turret = Pivot(root, "Turret", new Vector3(0f, 2.3f, 0.5f));
            C(rig.Turret, new Vector3(0f, 0.05f, 0f), new Vector3(1.0f, 0.12f, 1.0f), armorDk, "Ring");
            B(rig.Turret, new Vector3(0f, 0.32f, 0f), new Vector3(0.9f, 0.42f, 0.9f), armor, "Cupola", 0.08f);
            rig.Barrel = Pivot(rig.Turret, "Barrel", new Vector3(0f, 0.35f, 0.5f));
            C(rig.Barrel, new Vector3(-0.1f, 0f, 0.45f), new Vector3(0.08f, 0.45f, 0.08f), dark, "GunL", AlongZ);
            C(rig.Barrel, new Vector3(0.1f, 0f, 0.45f), new Vector3(0.08f, 0.45f, 0.08f), dark, "GunR", AlongZ);
            rig.Muzzle = Pivot(rig.Barrel, "Muzzle", new Vector3(0f, 0f, 0.95f));
            rig.Gun = rig.Barrel;
            rig.BarrelRest = rig.Barrel.localPosition;
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }

        private static UnitRig Tank(Transform parent, Color faction, string key)
        {
            var rig = new UnitRig { Height = 3.2f };
            var root = Pivot(parent, "Model", Vector3.zero);
            rig.Root = root;
            var armor = Armor(faction);
            var armorDk = ArmorDark(faction);
            rig.Armor = armor;
            var dark = Dark;
            var gun = Gunmetal;
            var trim = Trim(key);
            var lamp = GroundMats.Glow(new Color(1f, 0.92f, 0.75f) * 2.5f);
            Track(root, -1.7f, 6f, 1.2f, armor, dark, gun);
            Track(root, 1.7f, 6f, 1.2f, armor, dark, gun);
            // Корпус: низкий, широкий, лобовой лист под углом
            B(root, new Vector3(0f, 1.35f, -0.2f), new Vector3(2.6f, 1.0f, 4.9f), armor, "Hull", 0.14f);
            B(root, new Vector3(0f, 1.15f, 2.55f), new Vector3(2.55f, 0.9f, 1.1f), armor, "Glacis", 0.1f, Quaternion.Euler(-32f, 0f, 0f));
            B(root, new Vector3(0f, 1.95f, -1.2f), new Vector3(2.4f, 0.3f, 2.4f), armorDk, "Deck", 0.06f);
            for (int i = 0; i < 4; i++)
                B(root, new Vector3(-0.6f + i * 0.4f, 2.12f, -2.0f), new Vector3(0.28f, 0.05f, 1.0f), dark, "Grille", 0.01f);
            C(root, new Vector3(-0.95f, 2.25f, -2.55f), new Vector3(0.22f, 0.4f, 0.22f), gun, "Exhaust");
            C(root, new Vector3(0.95f, 2.25f, -2.55f), new Vector3(0.22f, 0.4f, 0.22f), gun, "Exhaust");
            B(root, new Vector3(-0.8f, 1.5f, 3.0f), new Vector3(0.26f, 0.16f, 0.1f), lamp, "LampL", 0.03f, Quaternion.Euler(-32f, 0f, 0f));
            B(root, new Vector3(0.8f, 1.5f, 3.0f), new Vector3(0.26f, 0.16f, 0.1f), lamp, "LampR", 0.03f, Quaternion.Euler(-32f, 0f, 0f));
            B(root, new Vector3(1.1f, 2.05f, 0.9f), new Vector3(0.45f, 0.35f, 0.9f), dark, "Box", 0.05f);
            // Башня: литая, с маской орудия и командирской башенкой
            rig.Turret = Pivot(root, "Turret", new Vector3(0f, 2.2f, 0.1f));
            B(rig.Turret, new Vector3(0f, 0.32f, -0.2f), new Vector3(2.05f, 0.72f, 2.6f), armor, "TurretBody", 0.22f);
            B(rig.Turret, new Vector3(0f, 0.3f, -1.62f), new Vector3(1.7f, 0.55f, 0.7f), armorDk, "Bustle", 0.08f);
            B(rig.Turret, new Vector3(0f, 0.32f, 1.17f), new Vector3(0.95f, 0.62f, 0.4f), gun, "Mantlet", 0.08f);
            B(rig.Turret, new Vector3(1.04f, 0.32f, -0.2f), new Vector3(0.04f, 0.16f, 1.8f), trim, "Stripe", 0.01f);
            B(rig.Turret, new Vector3(-1.04f, 0.32f, -0.2f), new Vector3(0.04f, 0.16f, 1.8f), trim, "Stripe", 0.01f);
            C(rig.Turret, new Vector3(-0.5f, 0.78f, -0.55f), new Vector3(0.6f, 0.14f, 0.6f), armorDk, "Cupola");
            C(rig.Turret, new Vector3(0.6f, 1.1f, -1.3f), new Vector3(0.025f, 0.55f, 0.025f), dark, "Antenna");
            rig.Barrel = Pivot(rig.Turret, "Barrel", new Vector3(0f, 0.34f, 1.3f));
            C(rig.Barrel, new Vector3(0f, 0f, 1.35f), new Vector3(0.24f, 1.35f, 0.24f), gun, "Gun", AlongZ);
            C(rig.Barrel, new Vector3(0f, 0f, 1.0f), new Vector3(0.32f, 0.22f, 0.32f), dark, "Evacuator", AlongZ);
            B(rig.Barrel, new Vector3(0f, 0f, 2.75f), new Vector3(0.48f, 0.3f, 0.42f), dark, "Brake", 0.05f);
            rig.Muzzle = Pivot(rig.Barrel, "Muzzle", new Vector3(0f, 0f, 3.0f));
            rig.Gun = rig.Barrel;
            rig.BarrelRest = rig.Barrel.localPosition;
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }

        private static UnitRig Artillery(Transform parent, Color faction, string key)
        {
            var rig = new UnitRig { Height = 3.4f };
            var root = Pivot(parent, "Model", Vector3.zero);
            rig.Root = root;
            var armor = Armor(faction);
            var armorDk = ArmorDark(faction);
            rig.Armor = armor;
            var dark = Dark;
            var gun = Gunmetal;
            var trim = Trim(key);
            Track(root, -1.55f, 5.4f, 1.05f, armor, dark, gun);
            Track(root, 1.55f, 5.4f, 1.05f, armor, dark, gun);
            B(root, new Vector3(0f, 1.3f, 0.2f), new Vector3(2.3f, 0.9f, 4.6f), armor, "Chassis", 0.12f);
            B(root, new Vector3(0f, 1.15f, 2.5f), new Vector3(2.2f, 0.7f, 0.8f), armor, "Nose", 0.08f, Quaternion.Euler(-35f, 0f, 0f));
            // Сошники сзади
            B(root, new Vector3(-0.8f, 0.6f, -2.8f), new Vector3(0.2f, 0.9f, 0.9f), dark, "Spade", 0.03f, Quaternion.Euler(30f, 0f, 0f));
            B(root, new Vector3(0.8f, 0.6f, -2.8f), new Vector3(0.2f, 0.9f, 0.9f), dark, "Spade", 0.03f, Quaternion.Euler(30f, 0f, 0f));
            rig.Turret = Pivot(root, "Turret", new Vector3(0f, 1.85f, -0.5f));
            B(rig.Turret, new Vector3(0f, 0.35f, 0f), new Vector3(1.9f, 0.8f, 2.2f), armorDk, "Mount", 0.1f);
            B(rig.Turret, new Vector3(0f, 0.8f, 0.2f), new Vector3(0.5f, 0.12f, 1.6f), trim, "Stripe", 0.02f);
            rig.Barrel = Pivot(rig.Turret, "Barrel", new Vector3(0f, 0.65f, 0.8f));
            rig.Barrel.localRotation = Quaternion.Euler(-38f, 0f, 0f);
            C(rig.Barrel, new Vector3(0f, 0f, 2.3f), new Vector3(0.36f, 2.3f, 0.36f), gun, "Howitzer", AlongZ);
            C(rig.Barrel, new Vector3(0.3f, -0.12f, 0.9f), new Vector3(0.14f, 0.8f, 0.14f), dark, "Recoil", AlongZ);
            C(rig.Barrel, new Vector3(-0.3f, -0.12f, 0.9f), new Vector3(0.14f, 0.8f, 0.14f), dark, "Recoil", AlongZ);
            B(rig.Barrel, new Vector3(0f, 0f, 4.55f), new Vector3(0.6f, 0.42f, 0.5f), dark, "Brake", 0.06f);
            rig.Muzzle = Pivot(rig.Barrel, "Muzzle", new Vector3(0f, 0f, 4.85f));
            rig.Gun = rig.Barrel;
            rig.BarrelRest = rig.Barrel.localPosition;
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }

        /// <summary>Титан-шагоход Ксарна: бронированный корпус-кабина на двух поршневых ногах, пушка и клешня. Ростом с трёхэтажный дом.</summary>
        private static UnitRig Walker(Transform parent, Color faction, string key)
        {
            var rig = new UnitRig { Height = 9f, HipsY = 5f };
            var root = Pivot(parent, "Model", Vector3.zero);
            rig.Root = root;
            var armor = Armor(faction);
            var armorDk = ArmorDark(faction);
            rig.Armor = armor;
            var dark = Dark;
            var gun = Gunmetal;
            var trim = Trim(key);
            var eye = GroundMats.Glow(Lens(key) * 2.6f);
            rig.Hips = Pivot(root, "Hips", new Vector3(0f, 5f, 0f));
            B(rig.Hips, Vector3.zero, new Vector3(2.4f, 1.0f, 1.8f), dark, "Pelvis", 0.2f);
            rig.LegL = Pivot(rig.Hips, "LegL", new Vector3(-1.5f, 0f, 0f));
            rig.LegR = Pivot(rig.Hips, "LegR", new Vector3(1.5f, 0f, 0f));
            foreach (var leg in new[] { rig.LegL, rig.LegR })
            {
                B(leg, new Vector3(0f, -1.2f, 0.2f), new Vector3(1.1f, 2.4f, 1.3f), armor, "Thigh", 0.2f, Quaternion.Euler(-10f, 0f, 0f));
                B(leg, new Vector3(0f, -2.5f, 0.55f), new Vector3(1.3f, 0.9f, 1.0f), armorDk, "Knee", 0.2f);
                B(leg, new Vector3(0f, -3.6f, 0.15f), new Vector3(0.95f, 2.3f, 1.1f), armor, "Shin", 0.18f, Quaternion.Euler(8f, 0f, 0f));
                C(leg, new Vector3(0.6f, -3.3f, -0.4f), new Vector3(0.2f, 1.1f, 0.2f), gun, "Piston");
                B(leg, new Vector3(0f, -4.8f, 0.45f), new Vector3(1.9f, 0.45f, 2.7f), dark, "Foot", 0.15f);
                B(leg, new Vector3(0f, -4.75f, 1.75f), new Vector3(1.7f, 0.35f, 0.4f), trim, "Toe", 0.08f);
            }
            rig.Torso = Pivot(rig.Hips, "Torso", new Vector3(0f, 0.4f, 0f));
            B(rig.Torso, new Vector3(0f, 1.7f, 0f), new Vector3(4.2f, 2.9f, 3.4f), armor, "Body", 0.35f);
            B(rig.Torso, new Vector3(0f, 2.0f, 1.65f), new Vector3(3.2f, 1.4f, 0.6f), armorDk, "Brow", 0.2f, Quaternion.Euler(-15f, 0f, 0f));
            B(rig.Torso, new Vector3(0f, 1.95f, 1.98f), new Vector3(2.2f, 0.32f, 0.1f), eye, "Viewport", 0.05f);
            B(rig.Torso, new Vector3(0f, 3.25f, -0.2f), new Vector3(2.6f, 0.3f, 2.4f), trim, "Crown", 0.1f);
            C(rig.Torso, new Vector3(-1.1f, 3.6f, -1.3f), new Vector3(0.45f, 0.7f, 0.45f), gun, "Stack");
            C(rig.Torso, new Vector3(1.1f, 3.6f, -1.3f), new Vector3(0.45f, 0.7f, 0.45f), gun, "Stack");
            rig.Turret = rig.Torso;
            // Наплечные бронеплиты
            B(rig.Torso, new Vector3(2.4f, 2.7f, 0.2f), new Vector3(1.4f, 0.5f, 2.2f), armorDk, "PadR", 0.15f, Quaternion.Euler(0f, 0f, -18f));
            B(rig.Torso, new Vector3(-2.4f, 2.7f, 0.2f), new Vector3(1.4f, 0.5f, 2.2f), armorDk, "PadL", 0.15f, Quaternion.Euler(0f, 0f, 18f));
            rig.Barrel = Pivot(rig.Torso, "Cannon", new Vector3(2.6f, 1.6f, 0.5f));
            B(rig.Barrel, new Vector3(0f, 0f, 0.4f), new Vector3(1.0f, 1.0f, 1.8f), gun, "Breech", 0.15f);
            C(rig.Barrel, new Vector3(0f, 0f, 2.4f), new Vector3(0.6f, 1.6f, 0.6f), dark, "Gun", AlongZ);
            B(rig.Barrel, new Vector3(0f, 0f, 4.1f), new Vector3(0.95f, 0.8f, 0.6f), gun, "Brake", 0.1f);
            rig.Muzzle = Pivot(rig.Barrel, "Muzzle", new Vector3(0f, 0f, 4.5f));
            rig.Gun = rig.Barrel;
            rig.BarrelRest = rig.Barrel.localPosition;
            rig.ArmL = Pivot(rig.Torso, "Claw", new Vector3(-2.7f, 1.8f, 0.6f));
            B(rig.ArmL, new Vector3(0f, -1.2f, 0.9f), new Vector3(0.95f, 2.6f, 0.95f), armor, "Arm", 0.15f);
            B(rig.ArmL, new Vector3(-0.35f, -2.75f, 1.5f), new Vector3(0.35f, 1.0f, 1.4f), dark, "PincerA", 0.08f, Quaternion.Euler(0f, 0f, 12f));
            B(rig.ArmL, new Vector3(0.35f, -2.75f, 1.5f), new Vector3(0.35f, 1.0f, 1.4f), dark, "PincerB", 0.08f, Quaternion.Euler(0f, 0f, -12f));
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }
    }
}
