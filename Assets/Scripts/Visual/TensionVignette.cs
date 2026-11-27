using UnityEngine;

/// <summary>
/// Threat visuals (Docs/MusicVisualsPlan.md 2-4), following the values FMOD is actually playing so they move
/// with the music's own smoothing:
/// <list type="bullet">
/// <item>Tension 0.25+ (string pad): a vignette closes in from the screen edges, navy turning violet</item>
/// <item>Tension 0.45+ (off-beat sub pulse): the vignette tightens on every off-beat 8th</item>
/// <item>Tension 0.65+ (acid bass): the arena ring wobbles (requested through <see cref="ArenaStage"/>)</item>
/// <item>Danger (last life, bed low-passed): bed visuals lose colour and light; guests stay bright</item>
/// <item>Hurt (lows dip for ~0.6 s): a short camera shake and the ring shrinks then recovers</item>
/// </list>
/// The vignette starts just outside the arena (only Danger pulls it slightly further in). Fever forces tension
/// to 0 in the music, so this fades out too.
/// </summary>
[DefaultExecutionOrder(100), DisallowMultipleComponent]
public sealed class TensionVignette : MonoBehaviour
{
    const int SortingOrder = 20;
    const int TextureSize = 128;
    static readonly Color Calm = new Color(0.05f, 0.06f, 0.2f);
    static readonly Color Tense = new Color(0.28f, 0.05f, 0.3f);

    SoundManager _sound;
    ArenaStage _stage;
    Camera _camera;
    SpriteRenderer _vignette;
    Texture2D _texture;
    Sprite _sprite;
    Vector3 _cameraHome;
    float _lastHurt;
    float _previousHurt;
    float _tension;
    float _danger;
    bool _shaking;
    float _shakeStart;
    float _shakeSeconds;
    float _shakeStrength;

    public void Init(Camera playCamera, ArenaStage stage, float arenaRadius)
    {
        _sound = SoundManager.Instance;
        _camera = playCamera;
        _stage = stage;
        _cameraHome = playCamera.transform.localPosition;
        _vignette = new GameObject("Tension Vignette").AddComponent<SpriteRenderer>();
        _vignette.transform.SetParent(transform, false);
        _vignette.sortingOrder = SortingOrder;
        BuildTexture(arenaRadius);
        _vignette.sprite = _sprite;
        _vignette.color = Color.clear;
    }

    /// <summary>Shakes the camera for <paramref name="seconds"/>, starting at <paramref name="strength"/> world units and easing to 0.</summary>
    public void Shake(float seconds, float strength)
    {
        _shakeStart = Time.time;
        _shakeSeconds = seconds;
        _shakeStrength = strength;
    }

    public void ResetVisuals()
    {
        _lastHurt = 0f;
        _shakeSeconds = 0f;
        _shaking = false;
        if (_camera != null)
            _camera.transform.localPosition = _cameraHome;
        if (_vignette != null)
            _vignette.color = Color.clear;
    }

    // A square covering the whole screen whose alpha is 0 inside the arena and rises towards the corners.
    void BuildTexture(float arenaRadius)
    {
        float half = Mathf.Max(_camera.orthographicSize * _camera.aspect, _camera.orthographicSize) * 1.15f;
        float clear = arenaRadius + 0.3f;
        float corner = half * Mathf.Sqrt(2f);
        _texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            { name = "Tension Vignette", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[TextureSize * TextureSize];
        for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float wx = (x + 0.5f) / TextureSize * 2f * half - half;
                float wy = (y + 0.5f) / TextureSize * 2f * half - half;
                float d = Mathf.Sqrt(wx * wx + wy * wy);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(clear, corner, d));
                pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, a);
            }
        _texture.SetPixels(pixels);
        _texture.Apply();
        _sprite = Sprite.Create(_texture, new Rect(0, 0, TextureSize, TextureSize), new Vector2(0.5f, 0.5f),
            TextureSize / (2f * half));
    }

    void Update()
    {
        using var profile = VisualProfiler.TensionUpdate.Auto();
        if (_sound == null || _stage == null)
            return;
        // Read FMOD once per frame (each read is a native call); LateUpdate reuses these.
        _tension = _sound.HeardTension;
        _danger = _sound.HeardDanger;
        float tension = _tension;
        float danger = _danger;
        float hurt = _sound.HeardHurt;

        // Requests for the shared ring and background (applied by MusicReactiveVisuals this frame).
        _stage.RingWobble = Mathf.Max(_stage.RingWobble, 0.012f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 0.85f, tension)));
        _stage.Desaturate = Mathf.Max(_stage.Desaturate, 0.4f * danger);
        _stage.Dim = Mathf.Max(_stage.Dim, 0.4f * danger);
        _stage.RingRadiusOffset -= 0.035f * hurt;

        // A hit snaps Hurt to 1; start the shake on that rising edge.
        if (hurt > 0.9f && _previousHurt <= 0.9f)
            _lastHurt = Time.time;
        _previousHurt = hurt;
    }

    void LateUpdate()
    {
        using var profile = VisualProfiler.TensionLate.Auto();
        if (_sound == null || _vignette == null)
            return;
        float tension = _tension;
        float danger = _danger;

        // Sub pulse on the off-beat 8ths from 0.45: the vignette breathes in between the kicks.
        float pulse = 0f;
        double beat = _sound.BeatPosition;
        if (beat >= 0 && tension > 0.45f)
        {
            float sinceOff = (float)((beat + 0.5) % 1.0);
            pulse = Mathf.Exp(-8f * sinceOff) * Mathf.InverseLerp(0.45f, 0.6f, tension);
        }

        float strength = Mathf.InverseLerp(0.25f, 1f, tension);
        float alpha = 0.55f * strength + 0.2f * danger + 0.08f * pulse;
        var c = Color.Lerp(Calm, Tense, strength);
        c.a = Mathf.Clamp01(alpha);
        // Calm play: switch the full-screen sprite off instead of drawing it transparent (saves fill rate).
        bool visible = c.a > 0.003f;
        if (_vignette.enabled != visible)
            _vignette.enabled = visible;
        if (visible)
            _vignette.color = c;
        // Closing in = shrinking the sprite, which shrinks its clear centre. The texture is 15% larger than the
        // screen, so down to ~0.87 it still covers every edge.
        float scale = 1f - 0.1f * danger - 0.03f * pulse;
        _vignette.transform.localPosition = new Vector3(_cameraHome.x, _cameraHome.y, 0f);
        _vignette.transform.localScale = Vector3.one * scale;

        // Hurt: a 0.15 s shake from the music's Hurt dip, or a longer gameplay shake (Shake), whichever is stronger now.
        float sinceHurt = Time.time - _lastHurt;
        float hurtShake = _lastHurt > 0f && sinceHurt < 0.15f ? 0.12f * (1f - sinceHurt / 0.15f) : 0f;
        float sinceShake = Time.time - _shakeStart;
        float gameShake = _shakeSeconds > 0f && sinceShake < _shakeSeconds
            ? _shakeStrength * (1f - sinceShake / _shakeSeconds) : 0f;
        if (hurtShake > 0f || gameShake > 0f)
        {
            float k = Mathf.Max(hurtShake, gameShake);
            _camera.transform.localPosition = _cameraHome + new Vector3(Random.Range(-k, k), Random.Range(-k, k), 0f);
            _shaking = true;
        }
        else if (_shaking)
        {
            // Put the camera back once; do not rewrite its transform every frame.
            _camera.transform.localPosition = _cameraHome;
            _shaking = false;
        }
    }

    void OnDestroy()
    {
        if (_camera != null)
            _camera.transform.localPosition = _cameraHome;
        if (_sprite != null)
            Destroy(_sprite);
        if (_texture != null)
            Destroy(_texture);
    }
}
