using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Station3D
{
    /// <summary>
    /// Ссылки на префабы кита «3D Scifi Kit Starter Kit» (Creepy Cat) для 3D-станции.
    /// Заполняется автоматически в редакторе (Assets/Editor/Station3DKitSetup.cs) и лежит в Resources,
    /// чтобы попасть в билд. Нет кита — станция строится из примитивов.
    /// </summary>
    public class Station3DKit : ScriptableObject
    {
        public const string ResourcePath = "Station3DKit";

        [Tooltip("Основное помещение (Simple_Room_02 — комната с коридорами, или Simple_Room_01)")]
        public GameObject Room;

        [Tooltip("Мелкие декорации: ящики, трубы, знаки — расставляются вдоль стен")]
        public List<GameObject> Props = new List<GameObject>();

        [Tooltip("Замена материалов Built-in (Standard) на URP-копии")]
        public List<Material> SourceMaterials = new List<Material>();
        public List<Material> UrpMaterials = new List<Material>();

        private Dictionary<Material, Material> _map;

        public Material Remap(Material m)
        {
            if (m == null) return null;
            if (_map == null)
            {
                _map = new Dictionary<Material, Material>();
                int n = Mathf.Min(SourceMaterials.Count, UrpMaterials.Count);
                for (int i = 0; i < n; i++)
                    if (SourceMaterials[i] != null && UrpMaterials[i] != null) _map[SourceMaterials[i]] = UrpMaterials[i];
            }
            return _map.TryGetValue(m, out var r) ? r : m;
        }

        private static Station3DKit _cached;
        private static bool _loaded;

        public static Station3DKit Load()
        {
            if (!_loaded) { _cached = Resources.Load<Station3DKit>(ResourcePath); _loaded = true; }
            return _cached != null && _cached.Room != null ? _cached : null;
        }
    }
}
