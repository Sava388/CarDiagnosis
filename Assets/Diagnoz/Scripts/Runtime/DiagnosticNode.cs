using UnityEngine;
namespace Diagnoz
{
    public enum NodeKind { Battery, Alternator, Engine, Brakes }
    [DisallowMultipleComponent]
    public sealed class DiagnosticNode : MonoBehaviour
    {
        [SerializeField] private NodeKind kind;
        [SerializeField] private string displayName;
        [SerializeField, Tooltip("Идентификатор узла в каталоге orders.json. Пусто = имя типа в нижнем регистре.")]
        private string nodeId;
        [SerializeField] private Renderer[] visuals;
        [SerializeField] private Material selectionMaterial;
        private Material[][] originals, selected;
        public NodeKind Kind => kind;
        public string DisplayName => displayName;
        // battery / alternator / engine / brakes — связь сцены с каталогом данных.
        public string Id => string.IsNullOrEmpty(nodeId) ? kind.ToString().ToLowerInvariant() : nodeId;
        public void Configure(NodeKind type, string label, Renderer[] renderers, Material highlight)
        { kind = type; displayName = label; visuals = renderers; selectionMaterial = highlight; }
        private void Awake() { Cache(); }
        private void Cache()
        {
            if (originals != null) return;
            if (visuals == null) visuals = new Renderer[0];
            originals = new Material[visuals.Length][]; selected = new Material[visuals.Length][];
            for (int i = 0; i < visuals.Length; i++)
            {
                if (visuals[i] == null) continue;
                originals[i] = visuals[i].sharedMaterials;
                selected[i] = new Material[originals[i].Length];
                for (int j = 0; j < originals[i].Length; j++)
                    selected[i][j] = selectionMaterial != null ? selectionMaterial : originals[i][j];
            }
        }
        public void SetSelected(bool value)
        {
            Cache();
            for (int i = 0; i < visuals.Length; i++)
                if (visuals[i] != null) visuals[i].sharedMaterials = value ? selected[i] : originals[i];
        }
        private void OnDisable() { if (originals != null) SetSelected(false); }
    }
}
