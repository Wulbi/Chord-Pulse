using UnityEngine;

/// <summary>
/// Arena visuals driven only by musical meaning from <see cref="SoundManager"/> (kick, snare, chord, energy,
/// build-up, fever start/end, breath), never by specific loops — so new music needs no new visual code.
/// It is also the single owner of the boundary ring and the background colour: other components ask for
/// changes through <see cref="Stage"/> and this merges and applies them once per frame.
/// </summary>
[DisallowMultipleComponent]
public sealed class MusicReactiveVisuals : MonoBehaviour
{
    public ArenaStage Stage { get; } = new ArenaStage();

    const int RingSegments = 96;

    // Background: a dark base tinted by the current chord; energy lifts it, breath calms it.
    static readonly Color BaseBackground = new Color(0.035f, 0.055f, 0.100f);
    static readonly Color[] ChordTints =
    {
        new Color(0.10f, 0.16f, 0.34f), // C  - blue
        new Color(0.20f, 0.10f, 0.32f), // Am - violet
        new Color(0.06f, 0.24f, 0.28f), // F  - teal
        new Color(0.30f, 0.16f, 0.10f)  // G  - amber
    };
    static readonly Color BreathBackground = new Color(0.04f, 0.06f, 0.08f);
    static readonly Color KickAccent = new Color(0.59f, 0.73f, 0.90f);
    static readonly Color SnareAccent = new Color(0.75f, 0.84f, 0.94f);
    static readonly Color BuildAccent = new Color(1f, 0.86f, 0.55f);

    const float ColorFollow = 3f;      // background tint smoothing (per second)
    const float CameraPunch = 0.012f;  // ortho size dip on kick
    const float BuildSqueeze = 0.06f;  // arena contraction at the end of the build bar
    const float FeverZoomOut = 0.025f;

    SoundManager _sound;
    Camera _camera;
    LineRenderer _boundary;
    LineRenderer _ripple;
    Color _boundaryColor;
    float _baseOrthoSize;
    float _arenaRadius;
    Color _background;
    float _flash;
    float _rippleAge = -1f;
    Color _rippleColor;
    bool _wasFever;
    Vector3[] _circle;
    bool _wobbled;

    public void Init(Camera playCamera, LineRenderer boundary, Color boundaryColor, float arenaRadius, Material lineMaterial)
    {
        _sound = SoundManager.Instance;
        _camera = playCamera;
        _boundary = boundary;
        _boundaryColor = boundaryColor;
        _arenaRadius = arenaRadius;
        _baseOrthoSize = playCamera.orthographicSize;
        _background = BaseBackground;
        Stage.BaseRadius = arenaRadius;
        _circle = new Vector3[boundary.positionCount];
        boundary.GetPositions(_circle);

        _ripple = new GameObject("Section Ripple").AddComponent<LineRenderer>();
        _ripple.transform.SetParent(transform, false);
        _ripple.useWorldSpace = false;
        _ripple.loop = true;
        _ripple.positionCount = RingSegments;
        _ripple.sharedMaterial = lineMaterial;
        _ripple.sortingOrder = -2;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i * (2f * Mathf.PI / RingSegments);
            _ripple.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f));
        }
        _ripple.enabled = false;
    }

    public void ResetVisuals()
    {
        _wasFever = false;
        _flash = 0f;
        _rippleAge = -1f;
        _ripple.enabled = false;
    }

    void LateUpdate()
    {
        if (_sound == null || _camera == null)
            return;

        double beatPosition = _sound.BeatPosition;
        int beat = _sound.CurrentBeat;
        bool running = beat >= 0;
        float sinceBeat = running ? (float)(beatPosition - beat) : 1f;

        bool fever = _sound.FeverActive;
        bool building = _sound.FeverBuilding;
        bool breath = _sound.IsBreath;

        // The ripple marks only fever's two edges (both on a phrase downbeat): a white burst with a flash as it
        // lands, a cool one as normal play resumes. Tempo changes no longer ripple: they coincide with guest loops
        // arriving and the ring read like a player pulse.
        if (fever && !_wasFever)
        {
            _flash = 1f;
            StartRipple(Color.white);
        }
        else if (!fever && _wasFever && running)
            StartRipple(KickAccent);
        _wasFever = fever;
        _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 2.5f);

        Stage.AppliedDesaturate = Mathf.Clamp01(Stage.Desaturate);
        Stage.AppliedDim = Mathf.Clamp01(Stage.Dim);
        UpdateBackground(running, fever, breath);
        UpdateBoundaryAndCamera(running, beat, sinceBeat, beatPosition, building, fever);
        UpdateRipple();
        Stage.ClearRequests();
    }

    void UpdateBackground(bool running, bool fever, bool breath)
    {
        Color target = BaseBackground;
        if (running)
        {
            float energy = _sound.ArrangementIntensity / (float)SoundManager.MaxIntensity;
            Color tint = ChordTints[_sound.ChordIndex];
            target = breath
                ? BreathBackground
                : Color.Lerp(BaseBackground, tint, 0.25f + 0.35f * energy + (fever ? 0.25f : 0f));
        }
        _background = Color.Lerp(_background, target, 1f - Mathf.Exp(-ColorFollow * Time.deltaTime));
        // Bed style palette (already cross-faded by its owner), then the danger drain, then the fever flash.
        var shown = Color.Lerp(_background, Stage.BackgroundTint, Mathf.Clamp01(Stage.BackgroundTintAmount));
        shown = Stage.Drain(shown);
        _camera.backgroundColor = Color.Lerp(shown, new Color(0.85f, 0.9f, 1f), _flash * 0.55f);
    }

    void UpdateBoundaryAndCamera(bool running, int beat, float sinceBeat, double beatPosition, bool building, bool fever)
    {
        float scale = 1f;
        float width = 0.025f;
        Color color = _boundaryColor;
        float ortho = _baseOrthoSize * (fever ? 1f + FeverZoomOut : 1f);

        if (running && building)
        {
            // Build-up bar: the arena tightens across the bar and flickers with the snare roll
            // (8ths for beats 1-2, 16ths for beats 3-4).
            float barProgress = (float)(beatPosition % SoundManager.BeatsPerBar) / SoundManager.BeatsPerBar;
            float sixteenth = (float)(beatPosition * 4.0);
            int hit = Mathf.FloorToInt(sixteenth) % 16;
            bool rollHit = hit >= 8 || hit % 2 == 0;
            float roll = rollHit ? Mathf.Exp(-14f * (sixteenth - Mathf.Floor(sixteenth))) : 0f;
            scale = 1f - BuildSqueeze * barProgress * barProgress;
            width = 0.025f + 0.03f * barProgress + 0.015f * roll;
            color = Color.Lerp(_boundaryColor, BuildAccent, 0.35f + 0.5f * barProgress * roll);
        }
        else if (running)
        {
            bool kick = _sound.KickOn(beat);
            float decay = kick ? 12f : 30f;
            float impact = Mathf.Exp(-decay * Mathf.Max(0f, sinceBeat));
            if (_sound.IsBreath)
                impact *= 0.5f;
            scale = 1f + (kick ? 0.025f * impact : 0f);
            width = 0.025f + (kick ? 0.025f : 0.012f) * impact;
            color = Color.Lerp(_boundaryColor, fever ? Color.white : kick ? KickAccent : SnareAccent, impact);
            if (kick)
                ortho *= 1f - CameraPunch * impact;
        }

        Stage.BeatImpact = running && !building
            ? Mathf.Exp(-(_sound.KickOn(beat) ? 12f : 30f) * Mathf.Max(0f, sinceBeat))
            : 0f;
        scale *= 1f + Stage.RingRadiusOffset;
        width += Stage.RingWidthAdd;
        color = Stage.Drain(color);
        Stage.RingScale = scale;
        Stage.RingWidth = width;
        Stage.RingColor = color;
        color.a *= Mathf.Clamp01(Stage.RingAlpha);

        _boundary.transform.localScale = new Vector3(scale, scale, 1f);
        _boundary.startWidth = _boundary.endWidth = width;
        _boundary.startColor = _boundary.endColor = color;
        ApplyWobble(beatPosition);
        _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, ortho, 1f - Mathf.Exp(-20f * Time.deltaTime));
    }

    // Acid-bass wobble: the ring's radius ripples with a few lobes that turn on 8th notes.
    void ApplyWobble(double beatPosition)
    {
        float amount = Stage.RingWobble;
        if (amount <= 0f)
        {
            if (_wobbled)
            {
                _boundary.SetPositions(_circle);
                _wobbled = false;
            }
            return;
        }
        _wobbled = true;
        float phase = Mathf.Floor((float)(beatPosition * 2.0)) * 0.9f;
        int count = _circle.Length;
        for (int i = 0; i < count; i++)
        {
            float a = i * (2f * Mathf.PI / count);
            float r = 1f + amount * (0.6f * Mathf.Sin(a * 5f + phase) + 0.4f * Mathf.Sin(a * 3f - phase * 1.7f));
            _boundary.SetPosition(i, _circle[i] * r);
        }
    }

    void StartRipple(Color color)
    {
        _rippleAge = 0f;
        _rippleColor = color;
        _ripple.enabled = true;
    }

    void UpdateRipple()
    {
        if (_rippleAge < 0f)
            return;
        _rippleAge += Time.deltaTime;
        const float duration = 0.8f;
        float t = _rippleAge / duration;
        if (t >= 1f)
        {
            _rippleAge = -1f;
            _ripple.enabled = false;
            return;
        }
        float radius = Mathf.Lerp(0.4f, _arenaRadius * 1.6f, 1f - (1f - t) * (1f - t));
        _ripple.transform.localScale = new Vector3(radius, radius, 1f);
        _ripple.startWidth = _ripple.endWidth = Mathf.Lerp(0.08f, 0.01f, t);
        var c = _rippleColor;
        c.a = 1f - t;
        _ripple.startColor = _ripple.endColor = c;
    }

    void OnDestroy()
    {
        if (_camera != null)
        {
            _camera.orthographicSize = _baseOrthoSize;
            _camera.backgroundColor = BaseBackground;
        }
    }
}
