using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The core breaks up as health goes: each lost point adds fractures across the ring and chips a piece out of it.
/// <list type="bullet">
/// <item>Cracks: thin dark jagged lines across the ring (children of the core, so they shake and burst with it).</item>
/// <item>Chips: gaps in the ring, made with the gradient's alpha keys, so they show the same in the selected colour
///       and in the fever rainbow (the colour keys stay the caller's). <see cref="AlphaKeys"/> is what the core's
///       gradient must use.</item>
/// <item>A hit: the new cracks draw in quickly, shards fly off where the chip breaks out, the core jolts.</item>
/// <item>Last life: the cracks glow ember-red on the kick and the ring flickers now and then: about to break.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class CoreDamageVisuals : MonoBehaviour
{
    const int CrackPoints = 6;
    const float CrackSeconds = 0.18f;
    const float JoltSeconds = 0.25f;
    const float JoltDistance = 0.06f;
    const float ChipWidth = 0.022f;   // half the gap, as a fraction of the ring (about 8° each side)
    const float ChipEdge = 0.004f;    // how sharply the ring breaks off at the gap's edges

    // Fractures, in the order they appear: angle (degrees, counter-clockwise from +x as the ring is drawn).
    static readonly float[] CrackAngles = { 38f, 212f, 128f, 300f, 82f, 255f };
    // Cracks shown with 0, 1, 2 ... points lost; chips likewise (each chip sits on one of the cracks).
    static readonly int[] CracksByDamage = { 0, 2, 5 };
    static readonly int[] ChipsByDamage = { 0, 1, 2 };
    static readonly Color CrackColor = new Color(0.02f, 0.03f, 0.07f, 0.95f);
    static readonly Color EmberColor = new Color(1f, 0.35f, 0.15f, 1f);
    static readonly GradientAlphaKey[] Whole = { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };

    sealed class Crack
    {
        public LineRenderer Line;
        public Vector3[] Points;
        public float Reveal;   // 0..1 drawn in
    }

    LineRenderer _core;
    Transform _coreTransform;
    float _radius;
    SoundManager _sound;
    Action<Vector3, Color> _shatter;
    Func<Color> _coreColor;
    readonly List<Crack> _cracks = new List<Crack>();
    GradientAlphaKey[] _alphaKeys = Whole;
    int _damage;
    float _joltAge = 99f;
    Vector3 _home;
    float _flicker;
    float _nextFlicker;

    /// <summary>Alpha keys for the core's gradient: whole, or with the chips cut out.</summary>
    public GradientAlphaKey[] AlphaKeys => _alphaKeys;
    /// <summary>Set when the alpha keys changed; the owner reapplies the core gradient and clears it.</summary>
    public bool AlphaDirty { get; set; }

    /// <param name="shatter">Spawns debris at a position in a colour (the game's shard effect).</param>
    /// <param name="coreColor">The core's colour right now (selected colour, or white in fever).</param>
    public void Init(LineRenderer core, float coreRadius, float ringWidth, Material material,
        Action<Vector3, Color> shatter, Func<Color> coreColor)
    {
        _core = core;
        _coreTransform = core.transform;
        _home = _coreTransform.localPosition;
        _radius = coreRadius;
        _shatter = shatter;
        _coreColor = coreColor;
        _sound = SoundManager.Instance;
        var random = new System.Random(7);
        float inner = coreRadius - ringWidth * 0.5f - 0.06f;
        float outer = coreRadius + ringWidth * 0.5f + 0.07f;
        foreach (float angle in CrackAngles)
        {
            var line = new GameObject("Core Crack").AddComponent<LineRenderer>();
            line.transform.SetParent(_coreTransform, false);
            line.useWorldSpace = false;
            line.positionCount = CrackPoints;
            line.sharedMaterial = material;
            line.sortingOrder = core.sortingOrder + 1;
            line.numCapVertices = 1;
            line.startWidth = 0.028f;
            line.endWidth = 0.012f;
            // A jagged path across the ring band, from outside in, wandering a few degrees either way.
            var points = new Vector3[CrackPoints];
            for (int p = 0; p < CrackPoints; p++)
            {
                float u = p / (float)(CrackPoints - 1);
                float r = Mathf.Lerp(outer, inner, u);
                float a = (angle + (float)(random.NextDouble() * 2 - 1) * 7f * (p == 0 ? 0.3f : 1f)) * Mathf.Deg2Rad;
                points[p] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r;
            }
            line.gameObject.SetActive(false);
            _cracks.Add(new Crack { Line = line, Points = points });
        }
    }

    /// <summary>Shows the damage for this health. A drop from the previous value plays the hit (cracks draw in, chip).</summary>
    public void SetHealth(int health, int maxHealth)
    {
        int damage = Mathf.Clamp(maxHealth - health, 0, CracksByDamage.Length - 1);
        if (damage == _damage)
            return;
        bool hit = damage > _damage;
        int chipsBefore = ChipsByDamage[_damage];
        _damage = damage;

        int show = CracksByDamage[damage];
        for (int i = 0; i < _cracks.Count; i++)
        {
            var crack = _cracks[i];
            bool on = i < show;
            if (on && !crack.Line.gameObject.activeSelf)
            {
                crack.Reveal = hit ? 0f : 1f;
                crack.Line.gameObject.SetActive(true);
                ApplyReveal(crack);
            }
            else if (!on && crack.Line.gameObject.activeSelf)
                crack.Line.gameObject.SetActive(false);
        }

        _alphaKeys = BuildAlphaKeys(ChipsByDamage[damage]);
        AlphaDirty = true;
        if (hit)
        {
            _joltAge = 0f;
            // Shards fly from each chip that just broke out.
            for (int c = chipsBefore; c < ChipsByDamage[damage]; c++)
            {
                float a = CrackAngles[c] * Mathf.Deg2Rad;
                // In the core's own space, so it follows the ring when it turns (fever).
                var at = _coreTransform.TransformPoint(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * _radius);
                _shatter?.Invoke(at, _coreColor != null ? _coreColor() : Color.white);
            }
        }
    }

    /// <summary>Hides every crack (the core itself burst, or a new round starts at full health).</summary>
    public void ResetVisuals()
    {
        _damage = 0;
        _alphaKeys = Whole;
        AlphaDirty = true;
        _joltAge = 99f;
        _flicker = 0f;
        foreach (var crack in _cracks)
            crack.Line.gameObject.SetActive(false);
        if (_coreTransform != null)
            _coreTransform.localPosition = _home;
    }

    public void HideCracks()
    {
        foreach (var crack in _cracks)
            crack.Line.gameObject.SetActive(false);
    }

    // Each chip is a flat stretch of zero alpha centred on its crack, with sharp edges: four keys per chip.
    // A gradient holds its first and last key's value beyond them, so no end keys are needed and two chips fit
    // Unity's 8-key limit.
    static GradientAlphaKey[] BuildAlphaKeys(int chips)
    {
        if (chips <= 0)
            return Whole;
        var centres = new List<float>();
        for (int c = 0; c < chips; c++)
            centres.Add(CrackAngles[c] / 360f);
        centres.Sort();
        var keys = new List<GradientAlphaKey>();
        foreach (float t in centres)
        {
            keys.Add(new GradientAlphaKey(1f, Mathf.Clamp01(t - ChipWidth - ChipEdge)));
            keys.Add(new GradientAlphaKey(0f, Mathf.Clamp01(t - ChipWidth)));
            keys.Add(new GradientAlphaKey(0f, Mathf.Clamp01(t + ChipWidth)));
            keys.Add(new GradientAlphaKey(1f, Mathf.Clamp01(t + ChipWidth + ChipEdge)));
        }
        return keys.ToArray();
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        // The cracks are part of the ring: they blink with it (hit invincibility) instead of floating on their own.
        bool ringShown = _core.enabled;
        foreach (var crack in _cracks)
            if (crack.Line.enabled != ringShown)
                crack.Line.enabled = ringShown;
        foreach (var crack in _cracks)
        {
            if (!crack.Line.gameObject.activeSelf || crack.Reveal >= 1f)
                continue;
            crack.Reveal = Mathf.Min(1f, crack.Reveal + dt / CrackSeconds);
            ApplyReveal(crack);
        }

        // Jolt on the hit: a short shake that settles back to the core's place.
        if (_joltAge < JoltSeconds)
        {
            _joltAge += dt;
            float k = JoltDistance * Mathf.Max(0f, 1f - _joltAge / JoltSeconds);
            _coreTransform.localPosition = _home + new Vector3(Mathf.Sin(_joltAge * 90f), Mathf.Cos(_joltAge * 70f), 0f) * k;
            if (_joltAge >= JoltSeconds)
                _coreTransform.localPosition = _home;
        }

        // Last life: cracks glow on the kick, and the ring flickers now and then.
        bool lastLife = _damage >= CracksByDamage.Length - 1;
        Color crackColor = CrackColor;
        if (lastLife && _sound != null && _sound.BeatPosition >= 0)
        {
            double beat = _sound.BeatPosition;
            int whole = (int)Math.Floor(beat);
            float glow = _sound.KickOn(whole) ? Mathf.Exp(-6f * (float)(beat - whole)) : 0f;
            crackColor = Color.Lerp(new Color(0.35f, 0.06f, 0.03f, 0.95f), EmberColor, glow);
        }
        foreach (var crack in _cracks)
            if (crack.Line.gameObject.activeSelf && crack.Line.startColor != crackColor)
                crack.Line.startColor = crack.Line.endColor = crackColor;

        if (lastLife)
        {
            if (Time.time >= _nextFlicker)
            {
                _flicker = 1f;
                _nextFlicker = Time.time + UnityEngine.Random.Range(0.7f, 1.8f);
            }
            _flicker = Mathf.MoveTowards(_flicker, 0f, dt * 10f);
            float width = _core.widthMultiplier;
            float target = 1f - 0.45f * _flicker;
            if (!Mathf.Approximately(width, target))
                _core.widthMultiplier = target;
        }
        else if (!Mathf.Approximately(_core.widthMultiplier, 1f))
            _core.widthMultiplier = 1f;
    }

    static void ApplyReveal(Crack crack)
    {
        // Draw the crack in from its outer end: hidden points sit on the last drawn one.
        float drawn = crack.Reveal * (CrackPoints - 1);
        for (int p = 0; p < CrackPoints; p++)
        {
            Vector3 point;
            if (p <= drawn)
                point = crack.Points[p];
            else
            {
                int last = Mathf.FloorToInt(drawn);
                point = Vector3.Lerp(crack.Points[last], crack.Points[Mathf.Min(last + 1, CrackPoints - 1)], drawn - last);
            }
            crack.Line.SetPosition(p, point);
        }
    }
}
