using UnityEngine;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// Art direction mockups only (A-01): a halo shell (Mockups/ArtMockupHalo.shader) that follows a body
    /// after the host has placed it, with the body's mesh, light direction and local space.
    /// </summary>
    [DefaultExecutionOrder(30)]
    public sealed class MockupHalo : MonoBehaviour
    {
        private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
        private static readonly int BodyWorldToLocalId = Shader.PropertyToID("_BodyWorldToLocal");
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");

        private BodyView body;
        private MeshFilter bodyFilter;
        private MeshRenderer bodyRenderer;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        /// <summary>The halo's material (owned by the caller).</summary>
        public Material Material { get; private set; }

        /// <summary>Shell thickness in body radii.</summary>
        public float Thickness { get; set; }

        public void Initialize(BodyView view, Material material)
        {
            body = view;
            bodyFilter = view.GetComponent<MeshFilter>();
            bodyRenderer = view.GetComponent<MeshRenderer>();
            Material = material;
            meshFilter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private void LateUpdate()
        {
            if (body == null)
            {
                return;
            }
            var t = body.transform;
            transform.SetPositionAndRotation(t.position, t.rotation);
            transform.localScale = t.localScale * (1f + Thickness);
            meshFilter.sharedMesh = bodyFilter.sharedMesh;
            meshRenderer.enabled = bodyRenderer.enabled && Thickness > 0f;
            Material.SetVector(LightDirectionId, body.LightDirection);
            Material.SetMatrix(BodyWorldToLocalId, t.worldToLocalMatrix);
            Material.SetFloat(ThicknessId, Thickness);
        }
    }
}
