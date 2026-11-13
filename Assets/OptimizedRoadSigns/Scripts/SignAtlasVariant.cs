using UnityEngine;
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer))]
public sealed class SignAtlasVariant : MonoBehaviour {
    public Vector4 atlasRect = new Vector4(0,0,1,1);
    static readonly int AtlasRect = Shader.PropertyToID("_AtlasRect");
    MaterialPropertyBlock block;
    void OnEnable() { Apply(); }
    void OnValidate() { Apply(); }
    public void Apply() {
        var r = GetComponent<MeshRenderer>();
        if (!r) return;
        if (block == null) block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block); block.SetVector(AtlasRect, atlasRect); r.SetPropertyBlock(block);
    }
}
