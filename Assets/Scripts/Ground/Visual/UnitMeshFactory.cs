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
    }

    /// <summary>
    /// Лоупольные модели бойцов и техники из примитивов в цветах фракции (как ShipMeshFactory для кораблей).
    /// Важен силуэт: тяжёлая пехота шире и с огромными наплечниками, снайпер тонкий с длинным стволом,
    /// сержант — со штандартом, генерал — огромный, в плаще и со светящимся клинком. Коллайдеров нет:
    /// выделение и попадания считаются по позициям.
    /// </summary>
    public static class UnitMeshFactory
    {
        public static UnitRig Build(Transform parent, UnitDef def, Color faction, bool sergeant, string factionKey)
        {
            if (def.Role == UnitRole.Swarm) return Drone(parent, def, faction);
            if (def.Vehicle)
            {
                switch (def.Role)
                {
                    case UnitRole.Tank: return Tank(parent, faction);
                    case UnitRole.Artillery: return Artillery(parent, faction);
                    case UnitRole.Walker: return Walker(parent, faction);
                    default: return Apc(parent, faction);
                }
            }
            return Infantry(parent, def, faction, sergeant, factionKey);
        }

        private static Material Armor(Color faction) => GroundMats.Lit(faction * 0.82f, 0.45f, 0.5f);
        private static Material Dark => GroundMats.Lit(new Color(0.13f, 0.14f, 0.16f), 0.65f, 0.45f);
        private static Material Cloth(Color faction) => GroundMats.Lit(Color.Lerp(faction, new Color(0.2f, 0.2f, 0.22f), 0.65f), 0f, 0.15f);

        private static Transform Pivot(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        // ================================================================ Пехота

        private static UnitRig Infantry(Transform parent, UnitDef def, Color faction, bool sergeant, string factionKey)
        {
            var rig = new UnitRig();
            float b = def.Bulk, s = def.Scale;
            var root = Pivot(parent, "Model", Vector3.zero);
            root.localScale = Vector3.one * s;
            rig.Root = root;
            rig.Height = 1.8f * s;
            var armor = def.Role == UnitRole.Militia ? GroundMats.Lit(Color.Lerp(faction, new Color(0.45f, 0.42f, 0.36f), 0.6f), 0f, 0.2f) : Armor(faction);
            rig.Armor = armor;
            var dark = Dark;
            var cloth = Cloth(faction);
            Color visorC = factionKey == "xarn" ? new Color(1f, 0.25f, 0.15f) : factionKey == "nol" ? new Color(0.6f, 0.95f, 1f)
                         : factionKey == "terraan" ? new Color(0.5f, 1f, 0.4f) : factionKey == "iridia" ? new Color(0.85f, 0.55f, 1f)
                         : factionKey == "aquila" ? new Color(1f, 0.8f, 0.3f) : new Color(0.4f, 0.9f, 1f);
            var visor = GroundMats.Glow(visorC * 1.8f);

            rig.Hips = Pivot(root, "Hips", new Vector3(0f, 0.92f, 0f));
            rig.LegL = Pivot(rig.Hips, "LegL", new Vector3(-0.13f * b, 0f, 0f));
            rig.LegR = Pivot(rig.Hips, "LegR", new Vector3(0.13f * b, 0f, 0f));
            GroundMats.Part(rig.LegL, PrimitiveType.Cube, new Vector3(0f, -0.45f, 0f), new Vector3(0.2f * b, 0.9f, 0.24f), cloth, "Leg");
            GroundMats.Part(rig.LegR, PrimitiveType.Cube, new Vector3(0f, -0.45f, 0f), new Vector3(0.2f * b, 0.9f, 0.24f), cloth, "Leg");
            GroundMats.Part(rig.LegL, PrimitiveType.Cube, new Vector3(0f, -0.84f, 0.05f), new Vector3(0.22f * b, 0.16f, 0.34f), dark, "Boot");
            GroundMats.Part(rig.LegR, PrimitiveType.Cube, new Vector3(0f, -0.84f, 0.05f), new Vector3(0.22f * b, 0.16f, 0.34f), dark, "Boot");

            rig.Torso = Pivot(rig.Hips, "Torso", Vector3.zero);
            GroundMats.Part(rig.Torso, PrimitiveType.Cube, new Vector3(0f, 0.33f, 0f), new Vector3(0.5f * b, 0.62f, 0.32f * Mathf.Sqrt(b)), armor, "Chest");
            GroundMats.Part(rig.Torso, PrimitiveType.Cube, new Vector3(0f, 0.36f, -0.22f), new Vector3(0.38f * b, 0.46f, 0.16f), dark, "Pack");
            float pad = def.Role == UnitRole.Heavy || def.Role == UnitRole.Hero ? 1.7f : 1f;
            if (def.Role != UnitRole.Militia && def.Role != UnitRole.Sniper)
            {
                GroundMats.Part(rig.Torso, PrimitiveType.Cube, new Vector3(-0.31f * b, 0.6f, 0f), new Vector3(0.24f * b * pad, 0.2f * pad, 0.36f * pad), armor, "PadL");
                GroundMats.Part(rig.Torso, PrimitiveType.Cube, new Vector3(0.31f * b, 0.6f, 0f), new Vector3(0.24f * b * pad, 0.2f * pad, 0.36f * pad), armor, "PadR");
            }
            rig.Head = Pivot(rig.Torso, "Head", new Vector3(0f, 0.82f, 0f));
            if (def.Role == UnitRole.Militia)
            {
                GroundMats.Part(rig.Head, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.26f, GroundMats.Lit(new Color(0.78f, 0.6f, 0.48f), 0f, 0.3f), "Face");
                GroundMats.Part(rig.Head, PrimitiveType.Cylinder, new Vector3(0f, 0.12f, 0f), new Vector3(0.28f, 0.04f, 0.28f), cloth, "Cap");
            }
            else
            {
                GroundMats.Part(rig.Head, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.3f, 0.32f, 0.32f), armor, "Helmet");
                GroundMats.Part(rig.Head, PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.14f), new Vector3(0.22f, 0.07f, 0.06f), visor, "Visor");
            }

            rig.ArmR = Pivot(rig.Torso, "ArmR", new Vector3(0.3f * b, 0.58f, 0f));
            rig.ArmL = Pivot(rig.Torso, "ArmL", new Vector3(-0.3f * b, 0.58f, 0f));
            GroundMats.Part(rig.ArmR, PrimitiveType.Cube, new Vector3(0f, -0.26f, 0.06f), new Vector3(0.13f * b, 0.52f, 0.14f), armor, "Arm");
            GroundMats.Part(rig.ArmL, PrimitiveType.Cube, new Vector3(0f, -0.26f, 0.06f), new Vector3(0.13f * b, 0.52f, 0.14f), armor, "Arm");
            rig.ArmR.localRotation = Quaternion.Euler(-70f, 0f, 0f);
            rig.ArmL.localRotation = Quaternion.Euler(-65f, 25f, 0f);

            // Оружие: от роли зависит силуэт
            rig.Gun = Pivot(rig.Torso, "Gun", new Vector3(0.14f * b, 0.42f, 0.3f));
            switch (def.Role)
            {
                case UnitRole.MachineGun:
                    GroundMats.Part(rig.Gun, PrimitiveType.Cube, new Vector3(0f, 0f, 0.3f), new Vector3(0.14f, 0.17f, 1.05f), dark, "HeavyGun");
                    GroundMats.Part(rig.Gun, PrimitiveType.Cube, new Vector3(0f, -0.14f, 0.1f), new Vector3(0.18f, 0.18f, 0.2f), armor, "Ammo");
                    break;
                case UnitRole.Rocket:
                    rig.Gun.localPosition = new Vector3(0.24f * b, 0.68f, 0.05f);
                    GroundMats.Part(rig.Gun, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.15f), new Vector3(0.17f, 0.6f, 0.17f), dark, "Tube", Quaternion.Euler(90f, 0f, 0f));
                    break;
                case UnitRole.Sniper:
                    GroundMats.Part(rig.Gun, PrimitiveType.Cube, new Vector3(0f, 0f, 0.45f), new Vector3(0.06f, 0.07f, 1.3f), dark, "LongRifle");
                    GroundMats.Part(rig.Gun, PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0.2f), new Vector3(0.05f, 0.12f, 0.05f), visor, "Scope", Quaternion.Euler(90f, 0f, 0f));
                    break;
                case UnitRole.Flamer:
                    GroundMats.Part(rig.Gun, PrimitiveType.Cube, new Vector3(0f, 0f, 0.3f), new Vector3(0.11f, 0.12f, 0.7f), dark, "Flamer");
                    GroundMats.Part(rig.Torso, PrimitiveType.Capsule, new Vector3(-0.1f, 0.4f, -0.34f), new Vector3(0.16f, 0.26f, 0.16f), GroundMats.Lit(new Color(0.8f, 0.3f, 0.1f), 0.4f, 0.5f), "Tank");
                    GroundMats.Part(rig.Torso, PrimitiveType.Capsule, new Vector3(0.1f, 0.4f, -0.34f), new Vector3(0.16f, 0.26f, 0.16f), GroundMats.Lit(new Color(0.8f, 0.3f, 0.1f), 0.4f, 0.5f), "Tank");
                    break;
                case UnitRole.Heavy:
                case UnitRole.Hero:
                    GroundMats.Part(rig.Gun, PrimitiveType.Cube, new Vector3(0f, 0f, 0.15f), new Vector3(0.1f, 0.12f, 0.4f), dark, "Pistol");
                    var blade = Pivot(rig.ArmL, "Blade", new Vector3(0f, -0.52f, 0.08f));
                    float len = def.Role == UnitRole.Hero ? 1.3f : 0.9f;
                    GroundMats.Part(blade, PrimitiveType.Cube, new Vector3(0f, -len * 0.5f, 0.1f), new Vector3(0.05f, len, 0.14f),
                        GroundMats.Glow(visorC * (def.Role == UnitRole.Hero ? 2.4f : 1.4f)), "Edge");
                    if (def.Role == UnitRole.Hero)
                        GroundMats.Part(rig.Torso, PrimitiveType.Cube, new Vector3(0f, 0.1f, -0.32f), new Vector3(0.6f * b, 1.2f, 0.05f), cloth, "Cape", Quaternion.Euler(12f, 0f, 0f));
                    break;
                case UnitRole.Seer:
                    GroundMats.Part(rig.Torso, PrimitiveType.Cylinder, new Vector3(0f, -0.3f, 0f), new Vector3(0.7f, 0.55f, 0.7f), cloth, "Robe");
                    GroundMats.Part(rig.Gun, PrimitiveType.Sphere, new Vector3(0f, 0.2f, 0.15f), Vector3.one * 0.26f, GroundMats.Glow(visorC * 2.6f), "Orb");
                    break;
                default:
                    GroundMats.Part(rig.Gun, PrimitiveType.Cube, new Vector3(0f, 0f, 0.25f), new Vector3(0.08f, 0.1f, 0.72f), dark, "Rifle");
                    GroundMats.Part(rig.Gun, PrimitiveType.Cube, new Vector3(0f, -0.1f, 0.12f), new Vector3(0.06f, 0.14f, 0.1f), dark, "Mag");
                    break;
            }
            rig.Muzzle = Pivot(rig.Gun, "Muzzle", new Vector3(0f, 0f, def.Role == UnitRole.Sniper ? 1.12f : def.Role == UnitRole.MachineGun ? 0.85f : 0.62f));

            // Сержант — штандарт с цветом фракции (виден издалека)
            if (sergeant && def.Role != UnitRole.Hero)
            {
                rig.Banner = Pivot(rig.Torso, "Banner", new Vector3(0.12f, 0.5f, -0.3f));
                GroundMats.Part(rig.Banner, PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f), new Vector3(0.03f, 0.65f, 0.03f), dark, "Pole");
                GroundMats.Part(rig.Banner, PrimitiveType.Cube, new Vector3(0f, 1.05f, -0.16f), new Vector3(0.02f, 0.32f, 0.3f), GroundMats.Glow(faction * 1.3f), "Flag");
            }
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }

        // ================================================================ Дроны Роя

        private static UnitRig Drone(Transform parent, UnitDef def, Color faction)
        {
            var rig = new UnitRig { Height = 1.2f };
            var root = Pivot(parent, "Model", Vector3.zero);
            rig.Root = root;
            rig.Hips = Pivot(root, "Hover", new Vector3(0f, 1.1f, 0f));
            rig.Torso = rig.Hips;
            var shell = GroundMats.Lit(new Color(0.75f, 0.8f, 0.86f), 0.8f, 0.7f);
            rig.Armor = shell;
            GroundMats.Part(rig.Hips, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.5f, 0.32f, 0.6f), shell, "Body");
            GroundMats.Part(rig.Hips, PrimitiveType.Cube, new Vector3(0f, 0f, 0.28f), new Vector3(0.2f, 0.06f, 0.08f), GroundMats.Glow(new Color(0.6f, 0.95f, 1f) * 2f), "Eye");
            GroundMats.Part(rig.Hips, PrimitiveType.Cube, new Vector3(0.38f, 0f, 0f), new Vector3(0.36f, 0.03f, 0.14f), Dark, "WingR");
            GroundMats.Part(rig.Hips, PrimitiveType.Cube, new Vector3(-0.38f, 0f, 0f), new Vector3(0.36f, 0.03f, 0.14f), Dark, "WingL");
            rig.Gun = Pivot(rig.Hips, "Gun", new Vector3(0f, -0.1f, 0.3f));
            rig.Muzzle = rig.Gun;
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }

        // ================================================================ Техника

        private static UnitRig Apc(Transform parent, Color faction)
        {
            var rig = new UnitRig { Height = 2.6f };
            var root = Pivot(parent, "Model", Vector3.zero);
            rig.Root = root;
            var armor = Armor(faction);
            rig.Armor = armor;
            var dark = Dark;
            GroundMats.Part(root, PrimitiveType.Cube, new Vector3(0f, 1.15f, 0f), new Vector3(2.6f, 1.2f, 4.6f), armor, "Hull");
            GroundMats.Part(root, PrimitiveType.Cube, new Vector3(0f, 1.3f, 2.45f), new Vector3(2.4f, 0.9f, 0.6f), armor, "Nose", Quaternion.Euler(35f, 0f, 0f));
            for (int i = 0; i < 4; i++)
            {
                float z = -1.6f + i * 1.07f;
                GroundMats.Part(root, PrimitiveType.Cylinder, new Vector3(-1.35f, 0.5f, z), new Vector3(0.9f, 0.18f, 0.9f), dark, "Wheel", Quaternion.Euler(0f, 0f, 90f));
                GroundMats.Part(root, PrimitiveType.Cylinder, new Vector3(1.35f, 0.5f, z), new Vector3(0.9f, 0.18f, 0.9f), dark, "Wheel", Quaternion.Euler(0f, 0f, 90f));
            }
            rig.Turret = Pivot(root, "Turret", new Vector3(0f, 1.95f, 0.4f));
            GroundMats.Part(rig.Turret, PrimitiveType.Cube, Vector3.zero, new Vector3(1.1f, 0.5f, 1.2f), armor, "TurretBody");
            rig.Barrel = Pivot(rig.Turret, "Barrel", new Vector3(0f, 0.05f, 0.5f));
            GroundMats.Part(rig.Barrel, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.75f), new Vector3(0.12f, 0.75f, 0.12f), dark, "Gun", Quaternion.Euler(90f, 0f, 0f));
            rig.Muzzle = Pivot(rig.Barrel, "Muzzle", new Vector3(0f, 0f, 1.5f));
            rig.Gun = rig.Barrel;
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }

        private static UnitRig Tank(Transform parent, Color faction)
        {
            var rig = new UnitRig { Height = 3f };
            var root = Pivot(parent, "Model", Vector3.zero);
            rig.Root = root;
            var armor = Armor(faction);
            rig.Armor = armor;
            var dark = Dark;
            GroundMats.Part(root, PrimitiveType.Cube, new Vector3(0f, 1.15f, 0f), new Vector3(3.1f, 1.1f, 5.6f), armor, "Hull");
            GroundMats.Part(root, PrimitiveType.Cube, new Vector3(0f, 1.0f, 2.9f), new Vector3(3f, 0.8f, 0.8f), armor, "Glacis", Quaternion.Euler(-30f, 0f, 0f));
            GroundMats.Part(root, PrimitiveType.Cube, new Vector3(-1.75f, 0.65f, 0f), new Vector3(0.75f, 1.2f, 6f), dark, "TrackL");
            GroundMats.Part(root, PrimitiveType.Cube, new Vector3(1.75f, 0.65f, 0f), new Vector3(0.75f, 1.2f, 6f), dark, "TrackR");
            rig.Turret = Pivot(root, "Turret", new Vector3(0f, 2.05f, -0.2f));
            GroundMats.Part(rig.Turret, PrimitiveType.Cube, Vector3.zero, new Vector3(2.2f, 0.85f, 2.6f), armor, "TurretBody");
            GroundMats.Part(rig.Turret, PrimitiveType.Cube, new Vector3(0f, 0.55f, -0.4f), new Vector3(0.8f, 0.3f, 0.8f), dark, "Hatch");
            rig.Barrel = Pivot(rig.Turret, "Barrel", new Vector3(0f, 0.05f, 1.2f));
            GroundMats.Part(rig.Barrel, PrimitiveType.Cylinder, new Vector3(0f, 0f, 1.6f), new Vector3(0.24f, 1.6f, 0.24f), dark, "Gun", Quaternion.Euler(90f, 0f, 0f));
            GroundMats.Part(rig.Barrel, PrimitiveType.Cylinder, new Vector3(0f, 0f, 3.1f), new Vector3(0.34f, 0.18f, 0.34f), dark, "Brake", Quaternion.Euler(90f, 0f, 0f));
            rig.Muzzle = Pivot(rig.Barrel, "Muzzle", new Vector3(0f, 0f, 3.3f));
            rig.Gun = rig.Barrel;
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }

        private static UnitRig Artillery(Transform parent, Color faction)
        {
            var rig = new UnitRig { Height = 3f };
            var root = Pivot(parent, "Model", Vector3.zero);
            rig.Root = root;
            var armor = Armor(faction);
            rig.Armor = armor;
            var dark = Dark;
            GroundMats.Part(root, PrimitiveType.Cube, new Vector3(0f, 1f, 0f), new Vector3(2.8f, 1f, 5f), armor, "Chassis");
            GroundMats.Part(root, PrimitiveType.Cube, new Vector3(-1.55f, 0.6f, 0f), new Vector3(0.6f, 1f, 5.2f), dark, "TrackL");
            GroundMats.Part(root, PrimitiveType.Cube, new Vector3(1.55f, 0.6f, 0f), new Vector3(0.6f, 1f, 5.2f), dark, "TrackR");
            rig.Turret = Pivot(root, "Turret", new Vector3(0f, 1.8f, -0.6f));
            GroundMats.Part(rig.Turret, PrimitiveType.Cube, Vector3.zero, new Vector3(2f, 1f, 2.2f), armor, "Mount");
            rig.Barrel = Pivot(rig.Turret, "Barrel", new Vector3(0f, 0.3f, 0.8f));
            rig.Barrel.localRotation = Quaternion.Euler(-38f, 0f, 0f);
            GroundMats.Part(rig.Barrel, PrimitiveType.Cylinder, new Vector3(0f, 0f, 2.4f), new Vector3(0.32f, 2.4f, 0.32f), dark, "Howitzer", Quaternion.Euler(90f, 0f, 0f));
            rig.Muzzle = Pivot(rig.Barrel, "Muzzle", new Vector3(0f, 0f, 4.8f));
            rig.Gun = rig.Barrel;
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }

        /// <summary>Титан-шагоход Ксарна: корпус-башня на двух ногах, пушка и клешня. Ростом с трёхэтажный дом.</summary>
        private static UnitRig Walker(Transform parent, Color faction)
        {
            var rig = new UnitRig { Height = 9f };
            var root = Pivot(parent, "Model", Vector3.zero);
            rig.Root = root;
            var armor = Armor(faction);
            rig.Armor = armor;
            var dark = Dark;
            rig.Hips = Pivot(root, "Hips", new Vector3(0f, 5f, 0f));
            rig.LegL = Pivot(rig.Hips, "LegL", new Vector3(-1.4f, 0f, 0f));
            rig.LegR = Pivot(rig.Hips, "LegR", new Vector3(1.4f, 0f, 0f));
            foreach (var leg in new[] { rig.LegL, rig.LegR })
            {
                GroundMats.Part(leg, PrimitiveType.Cube, new Vector3(0f, -2.5f, 0f), new Vector3(1.1f, 5f, 1.3f), dark, "Leg");
                GroundMats.Part(leg, PrimitiveType.Cube, new Vector3(0f, -4.9f, 0.4f), new Vector3(1.8f, 0.5f, 2.6f), armor, "Foot");
                GroundMats.Part(leg, PrimitiveType.Cube, new Vector3(0f, -1.6f, 0.6f), new Vector3(1.3f, 1.6f, 0.4f), armor, "Knee");
            }
            rig.Torso = Pivot(rig.Hips, "Torso", Vector3.zero);
            GroundMats.Part(rig.Torso, PrimitiveType.Cube, new Vector3(0f, 1.8f, 0f), new Vector3(4f, 3f, 3.2f), armor, "Body");
            GroundMats.Part(rig.Torso, PrimitiveType.Cube, new Vector3(0f, 2.4f, 1.62f), new Vector3(2.4f, 0.4f, 0.1f), GroundMats.Glow(new Color(1f, 0.25f, 0.15f) * 2f), "Eye");
            rig.Turret = rig.Torso;
            rig.Barrel = Pivot(rig.Torso, "Cannon", new Vector3(2.5f, 2f, 0.5f));
            GroundMats.Part(rig.Barrel, PrimitiveType.Cylinder, new Vector3(0f, 0f, 2f), new Vector3(0.7f, 2.4f, 0.7f), dark, "Gun", Quaternion.Euler(90f, 0f, 0f));
            rig.Muzzle = Pivot(rig.Barrel, "Muzzle", new Vector3(0f, 0f, 4.4f));
            rig.Gun = rig.Barrel;
            rig.ArmL = Pivot(rig.Torso, "Claw", new Vector3(-2.6f, 2f, 0.6f));
            GroundMats.Part(rig.ArmL, PrimitiveType.Cube, new Vector3(0f, -1.2f, 1f), new Vector3(0.9f, 2.6f, 0.9f), armor, "Arm");
            GroundMats.Part(rig.ArmL, PrimitiveType.Cube, new Vector3(0f, -2.6f, 1.6f), new Vector3(1.4f, 0.6f, 1.6f), dark, "Pincer");
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }
    }
}
