using UniverseSim.Unity.Native;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// One catalog body on screen (P5-03): a sphere placed, oriented and scaled from the snapshot, lit from
    /// the star's true direction (P5-07). Bodies without a radius (barycenters) have no mesh but keep their
    /// transform for labels and picking.
    /// </summary>
    public sealed class BodyView : MonoBehaviour
    {
        private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
        private static readonly int RingParamsId = Shader.PropertyToID("_RingParams");
        private static readonly int RingProfileId = Shader.PropertyToID("_RingProfile");

        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;

        /// <summary>The catalog body.</summary>
        public BodyInfo Info { get; private set; }

        /// <summary>True when the latest snapshot gave a position.</summary>
        public bool HasPosition { get; private set; }

        /// <summary>Snapshot flags (NativeConstants.Snapshot*).</summary>
        public uint Flags { get; private set; }

        /// <summary>Mean radius in render units (0 without a radius).</summary>
        public float RenderRadius { get; private set; }

        /// <summary>Largest of the three render radii, render units.</summary>
        public float MaxRenderRadius { get; private set; }

        /// <summary>For a moon, the view of its planet (set by the host); null otherwise.</summary>
        public BodyView Planet { get; set; }

        /// <summary>Unit vector toward the light in render space, from the latest snapshot (zero without one).</summary>
        public Vector4 LightDirection { get; private set; }

        private Texture ringProfile;
        private float ringInnerFraction;
        private double ringOuterKm;

        /// <summary>
        /// Gives the body the shadow of its rings (RingSystems): the optical depth profile over
        /// [<paramref name="innerFraction"/>, 1] of the outer edge at <paramref name="outerKm"/>.
        /// </summary>
        public void SetRings(Texture profile, float innerFraction, double outerKm)
        {
            ringProfile = profile;
            ringInnerFraction = innerFraction;
            ringOuterKm = outerKm;
        }

        /// <summary>
        /// False for a moon drawn within <paramref name="minPixels"/> of its planet on screen, where its label
        /// and orbit would only clutter the planet. Presentation only: the moon's position is unchanged.
        /// </summary>
        public bool StandsApart(Camera cam, float minPixels)
        {
            if (Planet == null || !Planet.HasPosition)
            {
                return true;
            }
            var a = cam.WorldToScreenPoint(transform.position);
            var b = cam.WorldToScreenPoint(Planet.transform.position);
            return a.z > 0f && b.z > 0f
                ? ((Vector2)a - (Vector2)b).sqrMagnitude >= minPixels * minPixels
                : a.z > 0f; // the planet is behind the camera: the moon is far apart from it on screen
        }

        public void Initialize(BodyInfo info, Mesh mesh, Material material)
        {
            Info = info;
            if (!info.HasMeanRadius)
            {
                return;
            }
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            block = new MaterialPropertyBlock();
        }

        /// <summary>Places the body from its snapshot. Allocates nothing.</summary>
        public void Apply(in UsimBodySnapshot s)
        {
            Flags = s.Flags;
            HasPosition = (s.Flags & NativeConstants.SnapshotNoPosition) == 0;
            var t = transform;
            t.localPosition = new Vector3((float)s.RenderPosition.X, (float)s.RenderPosition.Y, (float)s.RenderPosition.Z);
            // The core gives the quaternion in host axes, scalar first; Unity's constructor takes (x, y, z, w).
            t.localRotation = (s.Flags & NativeConstants.SnapshotNoOrientation) != 0
                ? Quaternion.identity
                : new Quaternion((float)s.Orientation.X, (float)s.Orientation.Y, (float)s.Orientation.Z, (float)s.Orientation.W);
            RenderRadius = (float)s.RenderRadius;
            var radii = new Vector3((float)s.RenderRadii.X, (float)s.RenderRadii.Y, (float)s.RenderRadii.Z);
            MaxRenderRadius = Mathf.Max(radii.x, Mathf.Max(radii.y, radii.z));
            // The sphere mesh has radius 1, so the scale is the radius along each local axis.
            t.localScale = radii;

            if (meshRenderer == null)
            {
                return;
            }
            meshRenderer.enabled = HasPosition && MaxRenderRadius > 0f;
            var light = new Vector4((float)s.LightDirection.X, (float)s.LightDirection.Y, (float)s.LightDirection.Z, 0f);
            LightDirection = light;
            block.SetVector(LightDirectionId, light);
            if (ringProfile != null && Info.MeanRadiusKm > 0.0)
            {
                // The outer edge in render units, scaled like the body (scale mode and distance compression).
                var outer = (float)(ringOuterKm * s.RenderRadius / Info.MeanRadiusKm);
                block.SetVector(RingParamsId, new Vector4(ringInnerFraction, outer, 1f, 0f));
                block.SetTexture(RingProfileId, ringProfile);
            }
            meshRenderer.SetPropertyBlock(block);
        }
    }
}
