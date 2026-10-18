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

            switch (definition.Category)
            {
                case UnitCategory.Tank:
                    FootprintRadius = 4.2f;
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.0f, 0f), new Vector3(3.4f, 1.1f, 7.0f), hull);
                    Part(PrimitiveType.Cube, body, new Vector3(-1.75f, 0.55f, 0f), new Vector3(0.8f, 1.1f, 7.3f), dark);
                    Part(PrimitiveType.Cube, body, new Vector3(1.75f, 0.55f, 0f), new Vector3(0.8f, 1.1f, 7.3f), dark);
                    turret = Pivot(body, new Vector3(0f, 1.55f, -0.4f));
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.45f, 0f), new Vector3(2.6f, 0.9f, 3.2f), hull);
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.93f, -0.6f), new Vector3(0.9f, 0.06f, 0.9f), marker);
                    Part(PrimitiveType.Cylinder, turret, new Vector3(0f, 0.45f, 4.2f), new Vector3(0.25f, 2.6f, 0.25f), dark,
                        new Vector3(90f, 0f, 0f));
                    muzzle = Pivot(turret, new Vector3(0f, 0.45f, 6.8f));
                    break;

                case UnitCategory.InfantryFightingVehicle:
                    FootprintRadius = 3.8f;
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.15f, 0f), new Vector3(3.2f, 1.5f, 6.4f), hull);
                    Part(PrimitiveType.Cube, body, new Vector3(-1.6f, 0.5f, 0f), new Vector3(0.6f, 1.0f, 6.6f), dark);
                    Part(PrimitiveType.Cube, body, new Vector3(1.6f, 0.5f, 0f), new Vector3(0.6f, 1.0f, 6.6f), dark);
                    turret = Pivot(body, new Vector3(0f, 1.9f, 0.6f));
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.3f, 0f), new Vector3(1.6f, 0.6f, 1.8f), hull);
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.62f, -0.3f), new Vector3(0.7f, 0.05f, 0.7f), marker);
                    Part(PrimitiveType.Cylinder, turret, new Vector3(0f, 0.3f, 2.2f), new Vector3(0.12f, 1.3f, 0.12f), dark,
                        new Vector3(90f, 0f, 0f));
                    muzzle = Pivot(turret, new Vector3(0f, 0.3f, 3.5f));
                    break;

                case UnitCategory.AntiTank:
                    FootprintRadius = 3.4f;
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.25f, 0f), new Vector3(2.8f, 1.4f, 5.6f), hull);
                    AddWheels(body, 1.45f, new[] { -1.8f, 0f, 1.8f }, dark);
                    turret = Pivot(body, new Vector3(0f, 1.95f, -0.6f));
                    Part(PrimitiveType.Cube, turret, new Vector3(-0.35f, 0.3f, 0.3f), new Vector3(0.4f, 0.4f, 1.8f), dark);
                    Part(PrimitiveType.Cube, turret, new Vector3(0.35f, 0.3f, 0.3f), new Vector3(0.4f, 0.4f, 1.8f), dark);
                    Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.05f, -0.5f), new Vector3(0.6f, 0.06f, 0.6f), marker);
                    muzzle = Pivot(turret, new Vector3(0f, 0.3f, 1.3f));
                    break;

                case UnitCategory.Recon:
                    FootprintRadius = 2.8f;
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 0.95f, 0f), new Vector3(2.1f, 0.9f, 4.4f), hull);
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 1.7f, -0.4f), new Vector3(1.9f, 0.7f, 2.0f), hull);
                    Part(PrimitiveType.Cube, body, new Vector3(0f, 2.06f, -0.9f), new Vector3(0.6f, 0.05f, 0.6f), marker);
                    AddWheels(body, 1.1f, new[] { -1.4f, 1.4f }, dark);
                    turret = Pivot(body, new Vector3(0f, 2.1f, -0.2f));
                    Part(PrimitiveType.Cylinder, turret, new Vector3(0f, 0.15f, 0.6f), new Vector3(0.08f, 0.6f, 0.08f), dark,
                        new Vector3(90f, 0f, 0f));
                    muzzle = Pivot(turret, new Vector3(0f, 0.15f, 1.2f));
                    break;

                default: // Infantry squad
                    FootprintRadius = 5f;
                    var uniform = Color.Lerp(hull, marker, 0.25f);
                    var offsets = new[]
                    {
                        new Vector3(0f, 0f, 2.5f), new Vector3(-2f, 0f, 0.8f), new Vector3(2f, 0f, 0.8f),
                        new Vector3(-3.5f, 0f, -1.2f), new Vector3(3.5f, 0f, -1.2f), new Vector3(0f, 0f, -1.5f)
                    };
                    foreach (var offset in offsets)
                        Part(PrimitiveType.Capsule, body, offset + new Vector3(0f, 0.9f, 0f), new Vector3(0.55f, 0.9f, 0.55f), uniform);
                    muzzle = Pivot(body, new Vector3(0f, 1.4f, 2.8f));
                    break;
            }

            BuildSelectionRing();
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
