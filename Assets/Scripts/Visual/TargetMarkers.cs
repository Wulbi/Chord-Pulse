using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Corner-bracket reticles on the enemies the next pulse will reach first. Each reticle takes its enemy's colour,
/// squeezes in on every beat, slides over when its target changes, and gets thicker and blinks faster as the
/// enemy closes in on the core. Dimmed when the selected colour already matches (the shot is ready).
/// </summary>
public sealed class TargetMarkers : MonoBehaviour
{
    public struct Target
    {
        public Vector3 Position;
        public Color Color;
        public bool Ready;
    }

    const float HalfExtent = 0.33f;
    const float ArmLength = 0.13f;
    const float BaseWidth = 0.022f;
    const float DangerWidth = 0.03f;
    const float BeatSqueeze = 0.14f;
    const float SlideSharpness = 18f;
    const float ReadyAlpha = 0.4f;

    sealed class Reticle
    {
        public GameObject Root;
        public LineRenderer[] Corners;
        public Vector3 Position;
        public bool Visible;
    }

    readonly List<Reticle> _reticles = new List<Reticle>();
    Material _material;

    public void Init(Material material)
    {
        _material = material;
    }

    public void Hide()
    {
        foreach (var reticle in _reticles)
            SetVisible(reticle, false);
    }

    /// <param name="danger">0..1, how close the nearest target is to the core.</param>
    /// <param name="sinceBeat">Beats since the last beat (0..1), or 1 when the music is not running.</param>
    public void Show(List<Target> targets, float danger, float sinceBeat)
    {
        while (_reticles.Count < targets.Count)
            _reticles.Add(CreateReticle());

        float squeeze = 1f - BeatSqueeze * Mathf.Exp(-7f * sinceBeat);
        float half = HalfExtent * squeeze;
        float width = BaseWidth + DangerWidth * danger;
        // Blink harder and faster as the enemy closes in.
        float blinkRate = Mathf.Lerp(3f, 16f, danger);
        float blink = 1f - 0.5f * danger * (0.5f + 0.5f * Mathf.Sin(Time.time * blinkRate * Mathf.PI * 2f));
        float follow = 1f - Mathf.Exp(-SlideSharpness * Time.deltaTime);

        for (int i = 0; i < _reticles.Count; i++)
        {
            var reticle = _reticles[i];
            if (i >= targets.Count)
            {
                SetVisible(reticle, false);
                continue;
            }

            var target = targets[i];
            // A reticle that was hidden appears on its target; a shown one slides over to the new target.
            reticle.Position = reticle.Visible
                ? Vector3.Lerp(reticle.Position, target.Position, follow)
                : target.Position;
            SetVisible(reticle, true);

            var color = target.Color;
            color.a = (target.Ready ? ReadyAlpha : 1f) * blink;
            for (int c = 0; c < 4; c++)
                DrawCorner(reticle.Corners[c], c, reticle.Position, half, width, color);
        }
    }

    Reticle CreateReticle()
    {
        var root = new GameObject("Target Reticle");
        root.transform.SetParent(transform, false);
        var reticle = new Reticle { Root = root, Corners = new LineRenderer[4] };
        for (int c = 0; c < 4; c++)
        {
            var corner = new GameObject("Corner").AddComponent<LineRenderer>();
            corner.transform.SetParent(root.transform, false);
            corner.useWorldSpace = true;
            corner.positionCount = 3;
            corner.numCornerVertices = 2;
            corner.sharedMaterial = _material;
            corner.sortingOrder = 3;
            reticle.Corners[c] = corner;
        }
        root.SetActive(false);
        return reticle;
    }

    static void DrawCorner(LineRenderer corner, int index, Vector3 center, float half, float width, Color color)
    {
        float sx = index == 0 || index == 3 ? -1f : 1f;
        float sy = index < 2 ? 1f : -1f;
        var tip = center + new Vector3(sx * half, sy * half, 0f);
        corner.SetPosition(0, tip - new Vector3(sx * ArmLength, 0f, 0f));
        corner.SetPosition(1, tip);
        corner.SetPosition(2, tip - new Vector3(0f, sy * ArmLength, 0f));
        corner.startWidth = corner.endWidth = width;
        corner.startColor = corner.endColor = color;
    }

    static void SetVisible(Reticle reticle, bool visible)
    {
        if (reticle.Visible == visible)
            return;
        reticle.Visible = visible;
        reticle.Root.SetActive(visible);
    }
}
