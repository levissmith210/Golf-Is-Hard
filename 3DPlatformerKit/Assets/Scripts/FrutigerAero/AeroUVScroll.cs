using UnityEngine;

/// <summary>Scrolls a texture on this renderer's material (water ripples, waterfalls, rainbow shimmer).</summary>
[DisallowMultipleComponent]
public class AeroUVScroll : MonoBehaviour
{
    public string textureProperty = "_BaseMap";
    public Vector2 speed = new Vector2(0f, 0.5f);
    private Material material;
    private Vector2 baseOffset;

    private void Start()
    {
        var r = GetComponent<Renderer>();
        if (r == null) { enabled = false; return; }
        material = r.material; // runtime instance, never touches the asset
        if (!material.HasProperty(textureProperty)) { enabled = false; return; }
        baseOffset = material.GetTextureOffset(textureProperty);
    }

    private void Update()
    {
        material.SetTextureOffset(textureProperty, baseOffset + speed * Time.time);
    }
}
