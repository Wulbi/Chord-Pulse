using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "MISS" pop-ups in the world: the word pops in where the combo broke (on the wrong-colour enemy that blocked the
/// pulse, or on the core when the pulse hit nothing), jitters for a moment, drifts up and fades.
/// Pooled TextMesh objects with Unity's built-in font, so nothing needs to be set up in the scene.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissPopups : MonoBehaviour
{
    const string Text = "MISS";
    const int SortingOrder = 25;   // above the music visuals and enemies, below the game-over overlay
    const float Seconds = 0.75f;
    const float PopSeconds = 0.12f;
    const float Rise = 0.45f;
    const float Shake = 0.05f;
    const float CharacterSize = 0.065f;
    const int FontSize = 72;
    const float EdgeMargin = 0.6f;
    // Bright and pale, so it never reads as one of the red enemies.
    static readonly Color MissColor = new Color(1f, 0.78f, 0.78f, 1f);

    sealed class Popup
    {
        public TextMesh Text;
        public MeshRenderer Renderer;
        public Vector3 Start;
        public float Age;
        public bool Active;
    }

    readonly List<Popup> _popups = new List<Popup>();
    Camera _camera;
    Font _font;

    public void Init(Camera playCamera)
    {
        _camera = playCamera;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i = 0; i < 4; i++)
            Hide(Create());
    }

    /// <summary>Shows "MISS" at a world position (kept inside the screen).</summary>
    public void Show(Vector3 worldPosition)
    {
        var popup = Take();
        popup.Start = ClampToScreen(worldPosition);
        popup.Age = 0f;
        popup.Active = true;
        popup.Text.transform.position = popup.Start;
        popup.Text.transform.localScale = Vector3.zero;
        popup.Text.color = MissColor;
        popup.Text.gameObject.SetActive(true);
    }

    public void ResetVisuals()
    {
        foreach (var popup in _popups)
            Hide(popup);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        foreach (var popup in _popups)
        {
            if (!popup.Active)
                continue;
            popup.Age += dt;
            float t = popup.Age / Seconds;
            if (t >= 1f)
            {
                Hide(popup);
                continue;
            }
            // Pop: overshoot then settle. Shake: a quick side-to-side jitter that dies out. Then drift up and fade.
            float pop = popup.Age < PopSeconds
                ? Mathf.Lerp(0.4f, 1.25f, popup.Age / PopSeconds)
                : Mathf.Lerp(1.25f, 1f, Mathf.Clamp01((popup.Age - PopSeconds) / 0.1f));
            float shake = Mathf.Sin(popup.Age * 70f) * Shake * Mathf.Max(0f, 1f - popup.Age / 0.25f);
            float rise = Rise * (1f - (1f - t) * (1f - t));
            var tr = popup.Text.transform;
            tr.position = popup.Start + new Vector3(shake, rise, 0f);
            tr.localScale = Vector3.one * pop;
            var c = MissColor;
            c.a = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
            popup.Text.color = c;
        }
    }

    Vector3 ClampToScreen(Vector3 position)
    {
        if (_camera == null)
            return position;
        var centre = _camera.transform.position;
        float halfHeight = _camera.orthographicSize - EdgeMargin;
        float halfWidth = _camera.orthographicSize * _camera.aspect - EdgeMargin * 1.5f;
        position.x = Mathf.Clamp(position.x, centre.x - halfWidth, centre.x + halfWidth);
        position.y = Mathf.Clamp(position.y, centre.y - halfHeight, centre.y + halfHeight - Rise);
        position.z = 0f;
        return position;
    }

    Popup Take()
    {
        foreach (var popup in _popups)
            if (!popup.Active)
                return popup;
        return Create();
    }

    Popup Create()
    {
        var go = new GameObject("Miss Popup");
        go.transform.SetParent(transform, false);
        var text = go.AddComponent<TextMesh>();
        text.text = Text;
        text.font = _font;
        text.fontSize = FontSize;
        text.characterSize = CharacterSize;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontStyle = FontStyle.Bold;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = _font.material;
        renderer.sortingOrder = SortingOrder;
        var popup = new Popup { Text = text, Renderer = renderer };
        _popups.Add(popup);
        return popup;
    }

    static void Hide(Popup popup)
    {
        popup.Active = false;
        popup.Text.gameObject.SetActive(false);
    }
}
