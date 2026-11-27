using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Game-over sequence (about 2.5 s of real time) played before the Restart screen:
/// <list type="number">
/// <item>Hit stop (0-0.1 s): time freezes on the final hit, white flash.</item>
/// <item>Rumble (0.1-1.1 s): slow motion; every object on the map trembles harder and harder, the camera shakes,
///       the arena ring warps.</item>
/// <item>Shockwave (1.1-1.9 s): the core bursts, a ring rolls out from it and bursts every object it reaches;
///       when it reaches the arena edge the boundary ring breaks into flying arcs.</item>
/// <item>Settle (1.9-2.5 s): debris fades, the screen darkens, then <c>onFinished</c> (the Restart screen).</item>
/// </list>
/// After 0.5 s Space / Enter / click skips to the end (everything left bursts at once). The game owns the objects:
/// it hands over <see cref="Target"/>s (what to shake, how far from the core, how to burst it) and draws the debris.
/// Runs on unscaled time because it sets <see cref="Time.timeScale"/> itself.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameOverSequence : MonoBehaviour
{
    public const float HitStopSeconds = 0.1f;
    public const float RumbleSeconds = 1.0f;
    public const float WaveSeconds = 0.8f;
    public const float SettleSeconds = 0.6f;
    public const float TotalSeconds = HitStopSeconds + RumbleSeconds + WaveSeconds + SettleSeconds;
    const float SkipAfter = 0.5f;
    const float SlowMotion = 0.35f;
    const float MaxTremble = 0.12f;
    const float MaxCameraShake = 0.18f;
    const float FlashSeconds = 0.18f;
    const float DarkAlpha = 0.55f;
    const int BoundaryPieces = 24;
    const int PiecePoints = 5;
    const float PieceSeconds = 0.7f;
    const int OverlayOrder = 30;

    /// <summary>One thing on the map: shaken during the rumble, burst when the shockwave reaches its distance.</summary>
    public struct Target
    {
        public Transform Transform;
        /// <summary>Distance from the core; the shockwave bursts it on arrival (0 = the core, first).</summary>
        public float Distance;
        public Action Burst;
    }

    enum Phase { Idle, HitStop, Rumble, Wave, Settle, Done }

    sealed class Shaken
    {
        public Transform Transform;
        public Vector3 Home;
        public float Phase;
    }

    ArenaStage _stage;
    Camera _camera;
    Vector3 _cameraHome;
    LineRenderer _boundary;
    Color _boundaryColor;
    float _arenaRadius;
    float _maxRadius;
    SpriteRenderer _overlay;
    LineRenderer _wave;
    readonly List<LineRenderer> _pieces = new List<LineRenderer>();
    readonly Vector3[] _pieceVelocity = new Vector3[BoundaryPieces];

    readonly List<Target> _targets = new List<Target>();
    readonly List<Shaken> _shaken = new List<Shaken>();
    readonly List<bool> _burst = new List<bool>();
    Phase _phase = Phase.Idle;
    float _time;
    float _waveStart;
    float _piecesAge = -1f;
    bool _boundaryBroken;
    bool _skipping;
    Action _onFinished;

    public bool Playing => _phase != Phase.Idle && _phase != Phase.Done;

    public void Init(Camera playCamera, ArenaStage stage, LineRenderer boundary, Color boundaryColor, float arenaRadius, Material material)
    {
        _camera = playCamera;
        _cameraHome = playCamera.transform.localPosition;
        _stage = stage;
        _boundary = boundary;
        _boundaryColor = boundaryColor;
        _arenaRadius = arenaRadius;
        float halfHeight = playCamera.orthographicSize;
        float halfWidth = halfHeight * playCamera.aspect;
        _maxRadius = Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight) + 0.5f;

        // Full-screen overlay for the white flash and the final darkening (UI canvases still draw on top).
        _overlay = new GameObject("Game Over Overlay").AddComponent<SpriteRenderer>();
        _overlay.transform.SetParent(transform, false);
        _overlay.sprite = WhiteSprite();
        _overlay.sortingOrder = OverlayOrder;
        _overlay.transform.localPosition = new Vector3(_cameraHome.x, _cameraHome.y, 0f);
        _overlay.transform.localScale = new Vector3(halfWidth * 2.4f, halfHeight * 2.4f, 1f);
        _overlay.enabled = false;

        _wave = MakeLine("Game Over Shockwave", 96, material);
        _wave.loop = true;
        for (int i = 0; i < 96; i++)
        {
            float a = i * (2f * Mathf.PI / 96);
            _wave.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }

        // Boundary debris: arcs of the arena ring, built once.
        float span = 2f * Mathf.PI / BoundaryPieces;
        for (int k = 0; k < BoundaryPieces; k++)
        {
            var piece = MakeLine("Boundary Piece", PiecePoints, material);
            float start = k * span;
            for (int p = 0; p < PiecePoints; p++)
            {
                float a = start + span * 0.85f * p / (PiecePoints - 1);
                piece.SetPosition(p, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * arenaRadius);
            }
            _pieces.Add(piece);
        }
    }

    /// <summary>Starts the sequence; <paramref name="onFinished"/> runs once, at the end or after a skip.</summary>
    public void Play(List<Target> targets, Action onFinished)
    {
        ResetVisuals();
        _onFinished = onFinished;
        _targets.AddRange(targets);
        // The shockwave bursts in distance order, so walk the list sorted.
        _targets.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        foreach (var target in _targets)
        {
            _burst.Add(false);
            _shaken.Add(target.Transform != null
                ? new Shaken { Transform = target.Transform, Home = target.Transform.localPosition, Phase = UnityEngine.Random.value * 10f }
                : null);
        }
        _phase = Phase.HitStop;
        _time = 0f;
        Time.timeScale = 0f;
        _overlay.enabled = true;
        _overlay.color = new Color(1f, 1f, 1f, 0.6f);
    }

    /// <summary>Back to normal for a new round (also safe when nothing played).</summary>
    public void ResetVisuals()
    {
        Time.timeScale = 1f;
        _phase = Phase.Idle;
        // Put every trembled object back where it was. Without this the core ring kept its last tremble offset after
        // a full game over, so on Restart it sat off-centre from the shape inside it - and since Play records the
        // current position as home, the offset grew with every game over.
        foreach (var s in _shaken)
            RestoreHome(s);
        _targets.Clear();
        _shaken.Clear();
        _burst.Clear();
        _onFinished = null;
        _skipping = false;
        _boundaryBroken = false;
        _piecesAge = -1f;
        if (_camera != null)
            _camera.transform.localPosition = _cameraHome;
        if (_boundary != null)
        {
            _boundary.enabled = true;
            _boundary.transform.localPosition = Vector3.zero;
        }
        if (_overlay != null)
            _overlay.enabled = false;
        if (_wave != null)
            _wave.gameObject.SetActive(false);
        foreach (var piece in _pieces)
            piece.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!Playing)
            return;
        float dt = Time.unscaledDeltaTime;
        _time += dt;

        if (!_skipping && _time >= SkipAfter && SkipPressed())
        {
            _skipping = true;
            BurstUpTo(float.MaxValue);
            BreakBoundary();
            _phase = Phase.Settle;
            _time = Mathf.Max(_time, TotalSeconds - SettleSeconds * 0.5f);
        }

        switch (_phase)
        {
            case Phase.HitStop:
                if (_time >= HitStopSeconds)
                {
                    _phase = Phase.Rumble;
                    Time.timeScale = SlowMotion;
                }
                break;
            case Phase.Rumble:
                if (_time >= HitStopSeconds + RumbleSeconds)
                {
                    _phase = Phase.Wave;
                    _waveStart = _time;
                    Time.timeScale = 1f;   // the bursts at full speed
                    _wave.gameObject.SetActive(true);
                }
                break;
            case Phase.Wave:
                if (_time >= HitStopSeconds + RumbleSeconds + WaveSeconds)
                {
                    BurstUpTo(float.MaxValue);
                    BreakBoundary();
                    _wave.gameObject.SetActive(false);
                    _phase = Phase.Settle;
                }
                break;
            case Phase.Settle:
                if (_time >= TotalSeconds)
                    Finish();
                break;
        }

        Animate(dt);
    }

    void Animate(float dt)
    {
        // 0..1 over the rumble, held at 1 through the wave.
        float rumble = _phase == Phase.HitStop ? 0f : Mathf.Clamp01((_time - HitStopSeconds) / RumbleSeconds);
        float shaking = _phase == Phase.Rumble || _phase == Phase.Wave ? rumble * rumble : 0f;

        // Tremble every object that has not burst yet; the camera shakes along.
        for (int i = 0; i < _shaken.Count; i++)
        {
            var s = _shaken[i];
            if (s == null || _burst[i] || s.Transform == null)
                continue;
            float k = MaxTremble * shaking;
            s.Transform.localPosition = s.Home + new Vector3(
                Mathf.Sin(_time * 53f + s.Phase) * k, Mathf.Sin(_time * 41f + s.Phase * 1.7f) * k, 0f);
        }
        if (_camera != null)
        {
            float c = MaxCameraShake * shaking;
            _camera.transform.localPosition = c > 0f
                ? _cameraHome + new Vector3(UnityEngine.Random.Range(-c, c), UnityEngine.Random.Range(-c, c), 0f)
                : _cameraHome;
        }
        if (_stage != null && shaking > 0f)
            _stage.RingWobble = Mathf.Max(_stage.RingWobble, 0.03f * shaking);

        // Shockwave: rolls out from the core and bursts what it reaches.
        if (_phase == Phase.Wave)
        {
            float t = Mathf.Clamp01((_time - _waveStart) / WaveSeconds);
            float radius = Mathf.Lerp(0.05f, _maxRadius, 1f - (1f - t) * (1f - t));
            _wave.transform.localScale = new Vector3(radius, radius, 1f);
            _wave.startWidth = _wave.endWidth = Mathf.Lerp(0.25f, 0.04f, t);
            _wave.startColor = _wave.endColor = new Color(1f, 1f, 1f, 1f - t * 0.7f);
            BurstUpTo(radius);
            if (radius >= _arenaRadius)
                BreakBoundary();
        }

        AnimatePieces(dt);

        // Overlay: white flash on the hit, dark at the end.
        if (_time < FlashSeconds)
            _overlay.color = new Color(1f, 1f, 1f, 0.6f * (1f - _time / FlashSeconds));
        else
        {
            float settle = Mathf.Clamp01((_time - (TotalSeconds - SettleSeconds)) / SettleSeconds);
            _overlay.color = new Color(0f, 0f, 0f, DarkAlpha * settle);
        }
    }

    void BurstUpTo(float radius)
    {
        for (int i = 0; i < _targets.Count; i++)
        {
            if (_burst[i] || _targets[i].Distance > radius)
                continue;
            _burst[i] = true;
            // The tremble stops here; leave the object at home, not at its last shaken offset.
            RestoreHome(_shaken[i]);
            _targets[i].Burst?.Invoke();
        }
    }

    static void RestoreHome(Shaken s)
    {
        // Unity's null check also catches transforms destroyed during the sequence (pulse rings).
        if (s != null && s.Transform != null)
            s.Transform.localPosition = s.Home;
    }

    void BreakBoundary()
    {
        if (_boundaryBroken)
            return;
        _boundaryBroken = true;
        _boundary.enabled = false;
        _piecesAge = 0f;
        for (int k = 0; k < _pieces.Count; k++)
        {
            var piece = _pieces[k];
            piece.gameObject.SetActive(true);
            piece.transform.localPosition = Vector3.zero;
            piece.transform.localRotation = Quaternion.identity;
            piece.startWidth = piece.endWidth = 0.05f;
            float a = (k + 0.5f) * (2f * Mathf.PI / BoundaryPieces);
            _pieceVelocity[k] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * UnityEngine.Random.Range(2.5f, 4.5f);
        }
    }

    void AnimatePieces(float dt)
    {
        if (_piecesAge < 0f)
            return;
        _piecesAge += dt;
        float t = _piecesAge / PieceSeconds;
        bool alive = t < 1f;
        for (int k = 0; k < _pieces.Count; k++)
        {
            var piece = _pieces[k];
            if (!alive)
            {
                piece.gameObject.SetActive(false);
                continue;
            }
            piece.transform.localPosition += _pieceVelocity[k] * dt * (1f - t);
            piece.transform.localRotation = Quaternion.Euler(0f, 0f, (k % 2 == 0 ? 1f : -1f) * 40f * t);
            var c = Color.Lerp(Color.white, _boundaryColor, t);
            c.a = 1f - t;
            piece.startColor = piece.endColor = c;
        }
        if (!alive)
            _piecesAge = -1f;
    }

    void Finish()
    {
        _phase = Phase.Done;
        Time.timeScale = 1f;
        if (_camera != null)
            _camera.transform.localPosition = _cameraHome;
        var done = _onFinished;
        _onFinished = null;
        done?.Invoke();
    }

    static bool SkipPressed()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        return (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
               || (mouse != null && mouse.leftButton.wasPressedThisFrame);
    }

    LineRenderer MakeLine(string objectName, int points, Material material)
    {
        var line = new GameObject(objectName).AddComponent<LineRenderer>();
        line.transform.SetParent(transform, false);
        line.useWorldSpace = false;
        line.positionCount = points;
        line.sharedMaterial = material;
        line.sortingOrder = 3;
        line.numCapVertices = 2;
        line.gameObject.SetActive(false);
        return line;
    }

    static Sprite WhiteSprite()
    {
        var texture = Texture2D.whiteTexture;
        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
        if (_camera != null)
            _camera.transform.localPosition = _cameraHome;
        if (_overlay != null && _overlay.sprite != null)
            Destroy(_overlay.sprite);
    }
}
