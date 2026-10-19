using System.Collections.Generic;
using UnityEngine;
using WargameRTS.Core;
using WargameRTS.Map;

namespace WargameRTS.Units
{
    /// <summary>
    /// Placeholder 3D model built from primitives, plus selection ring,
    /// terrain alignment and turret aiming. Replace with real models later.
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        const float TurretTurnRate = 70f;
        static Mesh ringMesh;

        readonly List<Renderer> bodyRenderers = new List<Renderer>();
        Transform body;
        Transform turret;
        Transform muzzle;
        GameObject selectionRing;
        Renderer ringRenderer;
        Vector3? aimPoint;
        bool visible = true;
        bool destroyed;

        public float FootprintRadius { get; private set; }

        public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position + Vector3.up * 2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ringMesh = null;

        public static UnitView Build(GameObject owner, UnitDefinition definition, Team team)
        {
            var view = owner.AddComponent<UnitView>();
            view.Construct(definition, team);
            return view;
        }

        void Construct(UnitDefinition definition, Team team)
        {
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);

            Color hull = team.ToVehicleColor();
            Color dark = hull * 0.55f;
            dark.a = 1f;
            Color marker = team.ToColor();

            switch (definition.Model)
            {
                case UnitModel.MediumTank:
                    FootprintRadius = 4.2f;
                    BuildTank(hull, dark, marker, 1f, 2.6f, 0.25f);
                    break;

                case UnitModel.HeavyTank:
                    FootprintRadius = 4.8f;
                    BuildTank(hull, dark, marker, 1.15f, 3f, 0.3f);
                    break;

                case UnitModel.LightTank:
                    FootprintRadius = 3.6f;
                    BuildTank(hull, dark, marker, 0.82f, 2.2f, 0.2f);
                    break;

                case UnitModel.TrackedIfv:
                    FootprintRadius = 3.8f;
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.15f, 0f), new Vector3(3.2f, 1.5f, 6.4f), hull);
                    Part(PrimitiveType.Cube, body, new Vector3(-1.6f, 0.5f, 0f), new Vector3(0.6f, 1.0f, 6.6f), dark);
                    Part(PrimitiveType.Cube, body, new Vector3(1.6f, 0.5f, 0f), new Vector3(0.6f, 1.0f, 6.6f), dark);
                    turret = Pivot(body, new Vector3(0f, 1.9f, 0.6f));
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.3f, 0f), new Vector3(1.6f, 0.6f, 1.8f), hull);
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.62f, -0.3f), new Vector3(0.7f, 0.05f, 0.7f), marker);
                    Barrel(turret, new Vector3(0f, 0.3f, 0.9f), 2.6f, 0.12f, dark);
                    break;

                case UnitModel.WheeledApc:
                    FootprintRadius = 3.6f;
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.3f, 0f), new Vector3(2.8f, 1.5f, 6.8f), hull);
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.2f, 3.3f), new Vector3(2.6f, 1.1f, 0.8f), hull,
                        new Vector3(-30f, 0f, 0f));
                    AddWheels(body, 1.45f, new[] { -2.3f, -0.8f, 0.8f, 2.3f }, dark);
                    turret = Pivot(body, new Vector3(0f, 2.1f, 0.8f));
                    Part(PrimitiveType.Cylinder, turret, new Vector3(0f, 0.15f, 0f), new Vector3(0.9f, 0.15f, 0.9f), hull);
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.32f, -0.2f), new Vector3(0.5f, 0.05f, 0.5f), marker);
                    Barrel(turret, new Vector3(0f, 0.2f, 0.3f), 1.5f, 0.08f, dark);
                    break;

                case UnitModel.AtgmCarrier:
                    FootprintRadius = 3.4f;
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.25f, 0f), new Vector3(2.8f, 1.4f, 5.6f), hull);
                    AddWheels(body, 1.45f, new[] { -1.8f, 0f, 1.8f }, dark);
                    turret = Pivot(body, new Vector3(0f, 1.95f, -0.6f));
                    Part(PrimitiveType.Cube, turret, new Vector3(-0.35f, 0.3f, 0.3f), new Vector3(0.4f, 0.4f, 1.8f), dark);
                    Part(PrimitiveType.Cube, turret, new Vector3(0.35f, 0.3f, 0.3f), new Vector3(0.4f, 0.4f, 1.8f), dark);
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.05f, -0.5f), new Vector3(0.6f, 0.06f, 0.6f), marker);
                    muzzle = Pivot(turret, new Vector3(0f, 0.3f, 1.3f));
                    break;

                case UnitModel.Spaag:
                    FootprintRadius = 3.8f;
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.1f, 0f), new Vector3(3.2f, 1.3f, 6.4f), hull);
                    Part(PrimitiveType.Cube, body, new Vector3(-1.6f, 0.5f, 0f), new Vector3(0.6f, 1.0f, 6.6f), dark);
                    Part(PrimitiveType.Cube, body, new Vector3(1.6f, 0.5f, 0f), new Vector3(0.6f, 1.0f, 6.6f), dark);
                    turret = Pivot(body, new Vector3(0f, 1.75f, -0.3f));
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.55f, 0f), new Vector3(2.4f, 1.1f, 2.6f), hull);
                    Part(PrimitiveType.Cylinder, turret, new Vector3(0f, 1.6f, -0.9f), new Vector3(1.2f, 0.05f, 1.2f), dark,
                        new Vector3(70f, 0f, 0f));
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 1.12f, 0.6f), new Vector3(0.6f, 0.05f, 0.6f), marker);
                    Barrel(turret, new Vector3(-0.8f, 0.6f, 1.2f), 2.4f, 0.1f, dark);
                    Barrel(turret, new Vector3(0.8f, 0.6f, 1.2f), 2.4f, 0.1f, dark);
                    break;

                case UnitModel.Howitzer:
                    FootprintRadius = 4.6f;
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.1f, 0f), new Vector3(3.4f, 1.3f, 7.0f), hull);
                    Part(PrimitiveType.Cube, body, new Vector3(-1.75f, 0.55f, 0f), new Vector3(0.8f, 1.1f, 7.3f), dark);
                    Part(PrimitiveType.Cube, body, new Vector3(1.75f, 0.55f, 0f), new Vector3(0.8f, 1.1f, 7.3f), dark);
                    turret = Pivot(body, new Vector3(0f, 1.75f, -1.2f));
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.75f, 0f), new Vector3(3.0f, 1.5f, 3.6f), hull);
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 1.52f, -0.8f), new Vector3(0.9f, 0.06f, 0.9f), marker);
                    var elevation = Pivot(turret, new Vector3(0f, 0.9f, 1.6f));
                    elevation.localRotation = Quaternion.Euler(-25f, 0f, 0f);
                    Barrel(elevation, Vector3.zero, 6.5f, 0.28f, dark);
                    break;

                case UnitModel.RocketLauncher:
                    FootprintRadius = 4.2f;
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.6f, 2.4f), new Vector3(2.6f, 1.8f, 2.2f), hull);
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.1f, -1.0f), new Vector3(2.6f, 0.5f, 5.0f), dark);
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 2.52f, 2.6f), new Vector3(0.7f, 0.05f, 0.7f), marker);
                    AddWheels(body, 1.35f, new[] { -2.6f, -1.2f, 2.4f }, dark);
                    turret = Pivot(body, new Vector3(0f, 1.5f, -1.4f));
                    var launcher = Pivot(turret, new Vector3(0f, 0.5f, 0f));
                    launcher.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                    Part(PrimitiveType.Cube, launcher, new Vector3(0f, 0f, 0f), new Vector3(2.2f, 1.0f, 3.4f), hull);
                    Part(PrimitiveType.Cube, launcher, new Vector3(0f, 0f, 1.72f), new Vector3(2.0f, 0.85f, 0.05f), dark);
                    muzzle = Pivot(launcher, new Vector3(0f, 0f, 1.8f));
                    break;

                case UnitModel.ReconCar:
                    FootprintRadius = 2.8f;
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 0.95f, 0f), new Vector3(2.1f, 0.9f, 4.4f), hull);
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.7f, -0.4f), new Vector3(1.9f, 0.7f, 2.0f), hull);
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 2.06f, -0.9f), new Vector3(0.6f, 0.05f, 0.6f), marker);
                    AddWheels(body, 1.1f, new[] { -1.4f, 1.4f }, dark);
                    turret = Pivot(body, new Vector3(0f, 2.1f, -0.2f));
                    Barrel(turret, new Vector3(0f, 0.15f, 0f), 1.2f, 0.08f, dark);
                    break;

                case UnitModel.InfantryTeam:
                    FootprintRadius = 4f;
                    BuildInfantry(Color.Lerp(hull, marker, 0.15f), new[]
                    {
                        new Vector3(0f, 0f, 1.8f), new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, -1.8f)
                    });
                    break;

                default: // InfantrySquad
                    FootprintRadius = 5f;
                    BuildInfantry(Color.Lerp(hull, marker, 0.25f), new[]
                    {
                        new Vector3(0f, 0f, 2.5f), new Vector3(-2f, 0f, 0.8f), new Vector3(2f, 0f, 0.8f),
                        new Vector3(-3.5f, 0f, -1.2f), new Vector3(3.5f, 0f, -1.2f), new Vector3(0f, 0f, -1.5f)
                    });
                    break;
            }

            BuildSelectionRing();
        }

        void BuildTank(Color hull, Color dark, Color marker, float scale, float barrelHalfLength, float barrelRadius)
        {
            Part(PrimitiveType.Cube, body, new Vector3(0f, 1.0f, 0f) * scale, new Vector3(3.4f, 1.1f, 7.0f) * scale, hull);
            Part(PrimitiveType.Cube, body, new Vector3(-1.75f, 0.55f, 0f) * scale, new Vector3(0.8f, 1.1f, 7.3f) * scale, dark);
            Part(PrimitiveType.Cube, body, new Vector3(1.75f, 0.55f, 0f) * scale, new Vector3(0.8f, 1.1f, 7.3f) * scale, dark);
            turret = Pivot(body, new Vector3(0f, 1.55f, -0.4f) * scale);
            Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.45f, 0f) * scale, new Vector3(2.6f, 0.9f, 3.2f) * scale, hull);
            Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.93f, -0.6f) * scale, new Vector3(0.9f, 0.06f, 0.9f) * scale, marker);
            Barrel(turret, new Vector3(0f, 0.45f, 1.6f) * scale, barrelHalfLength * 2f, barrelRadius, dark);
        }

        /// <summary>Cylinder barrel along +Z starting at <paramref name="start"/>; sets the muzzle.</summary>
        void Barrel(Transform parent, Vector3 start, float length, float radius, Color color)
        {
            Part(PrimitiveType.Cylinder, parent, start + new Vector3(0f, 0f, length * 0.5f),
                new Vector3(radius, length * 0.5f, radius), color, new Vector3(90f, 0f, 0f));
            muzzle = Pivot(parent, start + new Vector3(0f, 0f, length));
        }

        void BuildInfantry(Color uniform, Vector3[] offsets)
        {
            foreach (var offset in offsets)
                Part(PrimitiveType.Capsule, body, offset + new Vector3(0f, 0.9f, 0f), new Vector3(0.55f, 0.9f, 0.55f), uniform);
            muzzle = Pivot(body, new Vector3(0f, 1.4f, 2.8f));
        }

        void AddWheels(Transform parent, float halfWidth, float[] axles, Color color)
        {
            foreach (float z in axles)
            {
                Part(PrimitiveType.Cylinder, parent, new Vector3(-halfWidth, 0.55f, z), new Vector3(1.1f, 0.2f, 1.1f), color,
                    new Vector3(0f, 0f, 90f));
                Part(PrimitiveType.Cylinder, parent, new Vector3(halfWidth, 0.55f, z), new Vector3(1.1f, 0.2f, 1.1f), color,
                    new Vector3(0f, 0f, 90f));
            }
        }

        static Transform Pivot(Transform parent, Vector3 localPosition)
        {
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = localPosition;
            return pivot;
        }

        void Part(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, Color color,
            Vector3? localEuler = null)
        {
            var part = GameObject.CreatePrimitive(type);
            Destroy(part.GetComponent<Collider>());
            part.name = type.ToString();
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            if (localEuler.HasValue)
                part.transform.localRotation = Quaternion.Euler(localEuler.Value);

            var partRenderer = part.GetComponent<MeshRenderer>();
            partRenderer.sharedMaterial = GameMaterials.Lit(color);
            bodyRenderers.Add(partRenderer);
        }

        void BuildSelectionRing()
        {
            if (ringMesh == null)
                ringMesh = CreateRingMesh(1f, 0.86f, 48);

            selectionRing = new GameObject("SelectionRing");
            selectionRing.transform.SetParent(body, false);
            selectionRing.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            selectionRing.transform.localScale = Vector3.one * (FootprintRadius + 1f);
            selectionRing.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            ringRenderer = selectionRing.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = GameMaterials.Unlit(new Color(0.35f, 1f, 0.35f));
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            selectionRing.SetActive(false);
        }

        static Mesh CreateRingMesh(float outer, float inner, int segments)
        {
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * outer;
                vertices[i * 2 + 1] = direction * inner;

                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = i * 2 + 1;
                triangles[t + 2] = next * 2;
                triangles[t + 3] = next * 2;
                triangles[t + 4] = i * 2 + 1;
                triangles[t + 5] = next * 2 + 1;
            }

            var mesh = new Mesh { name = "SelectionRing", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        public void SetSelected(bool selected)
        {
            if (selectionRing != null)
                selectionRing.SetActive(selected && !destroyed);
        }

        public void SetVisible(bool isVisible)
        {
            if (destroyed)
                isVisible = true;
            if (visible == isVisible)
                return;

            visible = isVisible;
            foreach (var partRenderer in bodyRenderers)
                partRenderer.enabled = isVisible;
            ringRenderer.enabled = isVisible;
        }

        public void SetDestroyed()
        {
            SetVisible(true);
            destroyed = true;
            SetSelected(false);
            aimPoint = null;

            var wreck = GameMaterials.Lit(new Color(0.12f, 0.11f, 0.10f));
            foreach (var partRenderer in bodyRenderers)
                partRenderer.sharedMaterial = wreck;
        }

        public void AimAt(Vector3 worldPoint) => aimPoint = worldPoint;

        public void ClearAim() => aimPoint = null;

        void LateUpdate()
        {
            var map = GameMap.Instance;
            if (map != null && !destroyed)
            {
                Vector3 position = transform.position;
                body.position = new Vector3(position.x, map.HeightAt(position.x, position.z), position.z);
                body.rotation = Quaternion.FromToRotation(Vector3.up, map.NormalAt(position.x, position.z)) * transform.rotation;
            }

            if (turret == null || destroyed)
                return;

            Quaternion desired = Quaternion.identity;
            if (aimPoint.HasValue)
            {
                Vector3 local = body.InverseTransformDirection(aimPoint.Value - turret.position);
                local.y = 0f;
                if (local.sqrMagnitude > 0.01f)
                    desired = Quaternion.LookRotation(local);
            }
            turret.localRotation = Quaternion.RotateTowards(turret.localRotation, desired, TurretTurnRate * Time.deltaTime);
        }
    }
}
