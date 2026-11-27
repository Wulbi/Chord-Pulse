using UnityEngine;

/// <summary>The round white dot the visual layers draw with: a runtime texture, owned by whoever created it.</summary>
public static class DotSprite
{
    const int Size = 32;

    /// <summary>
    /// A filled dot with a short soft edge; <paramref name="glow"/> fades it from the centre instead (squared falloff).
    /// </summary>
    public static Sprite Create(string name, bool glow = false)
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[Size * Size];
        float r = (Size - 1) * 0.5f;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
                float a = Mathf.Clamp01(1f - d);
                pixels[y * Size + x] = new Color(1f, 1f, 1f, glow ? a * a : Mathf.Clamp01((1f - d) * 2.5f));
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
    }

    /// <summary>Destroys a dot from <see cref="Create"/> together with its texture (null is fine).</summary>
    public static void Destroy(Sprite dot)
    {
        if (dot == null)
            return;
        Object.Destroy(dot.texture);
        Object.Destroy(dot);
    }
}
