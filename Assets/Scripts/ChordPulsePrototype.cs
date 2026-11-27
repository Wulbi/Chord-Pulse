using System.Collections.Generic;
using GameLogic.Core;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class ChordPulsePrototype : MonoBehaviour
{
    const string EnemyPoolKey = "ChordPulseEnemy";
    const float CoreRadius = 0.42f;
    const float WaveWidth = 0.028f;
    const int CircleSegments = 96;
    // Pulse cooldown arc inside the core, and the current color's shape at its center.
    const float CooldownArcRadius = CoreRadius - 0.1f;
    const float CooldownArcWidth = 0.04f;
    const float CoreIconSize = CoreRadius * 0.8f;
    static readonly Color BoundaryColor = new Color(0.27f, 0.34f, 0.46f);

    static readonly Color[] Colors =
    {
        new Color(1f, 0.29f, 0.35f),
        new Color(0.29f, 0.72f, 1f),
        new Color(1f, 0.83f, 0.25f)
    };

    const int MaxHealth = 3;
    static readonly Color HiddenColor = new Color(0.62f, 0.65f, 0.71f);

    // Chord pair layout, in the enemy root's local space (one unit = one basic enemy across).
    // Centers sit this many shape diameters apart, so the two shapes never touch.
    const float ChordShapeSpacing = 1.15f;
    // Bar thickness in world units; it sits slightly behind the shapes so it never cuts across them.
    const float ChordBarThickness = 0.035f;
    const float ChordBarDepth = 0.05f;
    // Neutral, not a color: the bar only says "pair".
    static readonly Color ChordBarColor = new Color(0.9f, 0.92f, 0.95f);
    // Child order on the enemy template (see CreateChordParts).
    const int ChordLeftChild = 0;
    const int ChordRightChild = 1;
    const int ChordBarChild = 2;

    // Splitter: a hollow outline of its color's shape, with two small filled copies side by side inside.
    // Layout in the enemy root's local space (one unit = the splitter across). The triangle's inside is
    // wider near its base, so its pair sits lower.
    const float SplitterInnerScale = 0.3f;
    static readonly float[] SplitterInnerSpacing = { 0.15f, 0.19f, 0.19f };
    static readonly float[] SplitterInnerHeight = { -0.17f, 0f, 0f };
    // Outline stroke, in shape half-widths (a shape spans -1..1).
    const float OutlineThickness = 0.15f;
    // Split children end up this far (world units) to either side of the parent's line.
    const float SplitSpreadOffset = 0.22f;

    // Charger: its color's shape squeezed across and stretched along the direction of travel,
    // with two faint afterimages behind it (away from the core).
    const float ChargerWidth = 0.65f;
    const float ChargerLength = 1.1f;
    // Afterimage layout in the root's local space (one unit = the charger's length).
    const float TrailSpacing = 0.8f;
    const float TrailDepth = 0.05f;
    static readonly float[] TrailAlpha = { 0.4f, 0.18f };
    static readonly float[] TrailWidth = { 0.85f, 0.7f };
    // Each boost spreads the afterimages and stretches them by this much more.
    const float TrailBoostGrowth = 0.45f;
    // Boosted chargers tremble sideways; amplitude (world units) per boost, frequencies in radians per second.
    const float ShakePerBoost = 0.018f;
    const float ShakeFrequencyA = 71f;
    const float ShakeFrequencyB = 113f;
    const int SplitterInnerLeftChild = 3;
    const int SplitterInnerRightChild = 4;
    const int TrailNearChild = 5;
    const int TrailFarChild = 6;

    // A pulse crosses from the core to the arena edge in this many beats, at any tempo,
    // so one press resolves inside a single beat and its kills share one chord.
    const float PulseBeats = 0.75f;
    const float ArenaInset = 0.45f;
    // Fever charge: pulses in a row that kill something (outside fever); reaching it queues fever for the next loop.
    const int PulseComboForFever = 10;

    [SerializeField] Camera playCamera;
    [SerializeField] GamePanel gamePanel;


    sealed class Enemy
    {
        public EnemyState State;
        public GameObject Instance;
        /// <summary>Unit vector from the core toward the spawn point.</summary>
        public Vector2 Direction;
        public SpriteRenderer Body;
        public SpriteRenderer LeftShape;
        public SpriteRenderer RightShape;
        public SpriteRenderer Bar;
        public SpriteRenderer InnerLeft;
        public SpriteRenderer InnerRight;
        public SpriteRenderer TrailNear;
        public SpriteRenderer TrailFar;
        /// <summary>Phase of a boosted charger's tremble, so chargers do not shake in step.</summary>
        public float ShakePhase;
        /// <summary>Chord pair: the color drawn on the left shape (-1 otherwise). Tells which side a strip popped.</summary>
        public int LeftColor = -1;
    }

    sealed class Wave
    {
        public LineRenderer Ring;
        public PulseState Pulse;
        /// <summary>Beat step of this pulse's kill chord (-1 until its first kill).</summary>
        public int ChordStep = -1;
    }

    /// <summary>
    /// A killed enemy waiting for its kill chord. The kill counts at once (the rules already dropped its state:
    /// no damage, no tension, score and combo given), but it bursts on the beat the chord sounds. Meanwhile it is
    /// frozen, hollow and charging up, so it reads as "about to pop", never as still alive.
    /// </summary>
    sealed class DyingEnemy
    {
        public Enemy View;
        /// <summary>Beat step the kill chord sounds on (-1 = burst at once).</summary>
        public int BurstStep;
        public float Age;
        public Vector3 Origin;
        public Vector3 BaseScale;
        /// <summary>Shapes to shatter and their colours, captured at the kill before the look turns hollow.</summary>
        public int Parts;
        public readonly SpriteRenderer[] PartRenderers = new SpriteRenderer[2];
        public readonly Color[] PartColors = new Color[2];
    }

    // Kill → burst timing and look.
    const float DyingPushDistance = 0.12f;   // nudged outward along its path by the pulse, so it visibly stops coming
    const float DyingPushSeconds = 0.12f;
    const float DyingFlashSeconds = 0.07f;
    const float DyingMaxWait = 1.2f;         // safety: never hang around longer than this, whatever the clock says
    const float DyingSqueeze = 0.18f;        // shrinks this much as the beat approaches...
    const float DyingTremble = 0.035f;       // ...and trembles harder
    readonly List<DyingEnemy> _dying = new List<DyingEnemy>();
    readonly Stack<DyingEnemy> _dyingPool = new Stack<DyingEnemy>();

    // Rules see only the states; each state maps back to its view for drawing and pooling.
    readonly List<EnemyState> _enemyStates = new List<EnemyState>();
    readonly Dictionary<EnemyState, Enemy> _enemyViews = new Dictionary<EnemyState, Enemy>();
    readonly List<Wave> _waves = new List<Wave>();
    readonly List<PulseEvent> _pulseEvents = new List<PulseEvent>();
    readonly PulseResolver _pulseResolver = new PulseResolver();
    readonly PulseCooldown _pulseCooldown = new PulseCooldown();
    readonly SpawnDirector _spawnDirector = new SpawnDirector(new System.Random());

    // Fever: the cracked core ring itself turns (one turn per bar), carrying its cracks and chips round.
    const float FeverTurnsPerBar = 1f;
    // Beat flash: the core whitens on every beat and fades within this long; the kick flashes harder.
    const float BeatFlashSeconds = 0.09f;
    const float RingFlash = 0.55f;
    const float IconFlash = 0.85f;
    readonly GradientColorKey[] _rainbowKeys = new GradientColorKey[8];
    readonly Gradient _rainbow = new Gradient();
    // The same hue wheel for fever pulses, without the core's damage chips: a pulse is always a whole circle.
    readonly Gradient _pulseRainbow = new Gradient();
    static readonly GradientAlphaKey[] OpaqueAlpha = { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };

    readonly List<TargetMarkers.Target> _targets = new List<TargetMarkers.Target>();
    TargetMarkers _targetMarkers;
    GameOverSequence _gameOverSequence;
    CoreDamageVisuals _coreDamage;
    MissPopups _missPopups;
    readonly List<GameOverSequence.Target> _gameOverTargets = new List<GameOverSequence.Target>();

    PoolManager _poolManager;
    GameManager _gameManager;
    SoundManager _soundManager;
    GameObject _enemyTemplate;
    Material _ringMaterial;
    Texture2D _enemyShapeTexture;
    // Filled shapes per color, the gray square (index Colors.Length), then the hollow outlines per color.
    readonly Sprite[] _enemyShapes = new Sprite[Colors.Length * 2 + 1];
    LineRenderer _core;
    LineRenderer _cooldownArc;
    SpriteRenderer _coreIcon;
    float _coreSpin;
    readonly Gradient _coreSolid = new Gradient();
    readonly GradientColorKey[] _coreSolidKeys = new GradientColorKey[2];
    Color _coreAppliedColor = Color.clear;
    LineRenderer _boundary;
    MusicReactiveVisuals _visuals;
    BedStyleVisuals _bedStyleVisuals;
    BedLayerVisuals _bedLayerVisuals;
    TensionVignette _tensionVignette;
    FeatureLoopVisuals _featureVisuals;
    ParticleSystem _shards;
    Mesh _shardMesh;
    float _arenaRadius;
    int _selectedColor;
    float _spawnAngle;
    int _spawnGroup;
    readonly CoreHitRule _coreHits = new CoreHitRule();
    // Core hit: invincible for this long (the core blinks), the screen shakes and the field is cleared.
    const float InvincibleSeconds = 1.5f;
    const float InvincibleBlinkRate = 8f;   // blinks per second
    const float HitShakeSeconds = 0.4f;
    const float HitShakeStrength = 0.25f;   // world units at the start, easing to 0
    float _invincibleUntil;
    bool _clearAfterHit;
    int _score;
    int _health;
    int _pulseCombo;
    int _maxPulseCombo;   // highest pulse combo of the round (game-over screen)
    int _feverCharge;
    int _lastHudBeat;
    bool _wasFeverActive;
    int _roundStartFrame = -1;
    double _prevBeatPosition = double.NegativeInfinity;

    void Awake()
    {
        _gameManager = GameManager.Instance;
        _soundManager = SoundManager.Instance;
        _gameManager.OnGameRestarted += StartRound;
        _gameManager.OnGameOver += HandleGameOver;
    }

    void Start()
    {
        _ringMaterial = new Material(Shader.Find("Sprites/Default"));
        _core = CreateRing("Central Core", Colors[0], CoreRadius, 0.12f);
        _arenaRadius = playCamera.orthographicSize * Mathf.Min(1f, playCamera.aspect) - ArenaInset;
        _boundary = CreateRing("Arena Boundary", BoundaryColor, _arenaRadius, 0.025f);
        _boundary.sortingOrder = -1;
        _visuals = gameObject.AddComponent<MusicReactiveVisuals>();
        _visuals.Init(playCamera, _boundary, BoundaryColor, _arenaRadius, _ringMaterial);
        _bedStyleVisuals = gameObject.AddComponent<BedStyleVisuals>();
        _bedStyleVisuals.Init(playCamera, _visuals.Stage, _ringMaterial);
        _bedLayerVisuals = gameObject.AddComponent<BedLayerVisuals>();
        _bedLayerVisuals.Init(_visuals.Stage, _ringMaterial);
        _tensionVignette = gameObject.AddComponent<TensionVignette>();
        _tensionVignette.Init(playCamera, _visuals.Stage, _arenaRadius);
        _featureVisuals = gameObject.AddComponent<FeatureLoopVisuals>();
        _featureVisuals.Init(playCamera, _arenaRadius, _ringMaterial);
        _gameOverSequence = gameObject.AddComponent<GameOverSequence>();
        _gameOverSequence.Init(playCamera, _visuals.Stage, _boundary, BoundaryColor, _arenaRadius, _ringMaterial);
        CreateShatterEffect();
        _missPopups = gameObject.AddComponent<MissPopups>();
        _missPopups.Init(playCamera);
        _coreDamage = gameObject.AddComponent<CoreDamageVisuals>();
        _coreDamage.Init(_core, CoreRadius, 0.12f, _ringMaterial, Shatter,
            () => _soundManager.FeverActive ? Color.white : Colors[_selectedColor]);
        _targetMarkers = gameObject.AddComponent<TargetMarkers>();
        _targetMarkers.Init(_ringMaterial);

        CreateEnemyShapes();
        CreateCoreIndicators();
        _enemyTemplate = new GameObject("Chord Pulse Enemy Template");
        _enemyTemplate.transform.SetParent(transform, false);
        _enemyTemplate.SetActive(false);
        _enemyTemplate.transform.localScale = Vector3.one * (GameplayTuning.BaseEnemyHalfSize * 2f);
        _enemyTemplate.AddComponent<SpriteRenderer>();
        CreateChordParts(_enemyTemplate.transform);
        CreateKindParts(_enemyTemplate.transform);
        _enemyTemplate.AddComponent<Poolable>();

        _poolManager = PoolManager.Instance;
        _poolManager.RegisterPool(EnemyPoolKey, _enemyTemplate, GameplayTuning.EnemyPoolSize);

        // First launch only: a controls card, and the round (with its music) starts once it is dismissed.
        var help = HelpPanel.Create(UIManager.Instance.CanvasTransform);
        UIManager.Instance.RegisterPanel(help);
        help.Show(StartRound);
    }

    void StartRound()
    {
        _poolManager.ReturnAll(EnemyPoolKey);
        _enemyStates.Clear();
        _enemyViews.Clear();
        foreach (var dying in _dying)
            _dyingPool.Push(dying);
        _dying.Clear();
        foreach (var wave in _waves)
            Destroy(wave.Ring.gameObject);
        _waves.Clear();
        _pulseEvents.Clear();
        _shards.Clear();
        _spawnDirector.Reset();
        _pulseCooldown.Reset();
        _prevBeatPosition = double.NegativeInfinity;

        _selectedColor = 0;
        _spawnAngle = Random.Range(0f, 360f);
        _spawnGroup = 0;
        _coreHits.Reset();
        _invincibleUntil = 0f;
        _clearAfterHit = false;
        _score = 0;
        _health = MaxHealth;
        _pulseCombo = 0;
        _maxPulseCombo = 0;
        _feverCharge = 0;
        _lastHudBeat = -1;
        _wasFeverActive = false;
        _roundStartFrame = Time.frameCount;
        _gameOverSequence.ResetVisuals();
        _coreDamage.ResetVisuals();
        _missPopups.ResetVisuals();
        // The core, its colour shape and the cooldown arc all sit on the arena centre; a game over trembles the core,
        // so re-centre them explicitly (they must never drift apart).
        _core.transform.localPosition = Vector3.zero;
        _core.transform.localRotation = Quaternion.identity;
        _coreSpin = 0f;
        _coreIcon.transform.localPosition = Vector3.zero;
        _cooldownArc.transform.localPosition = Vector3.zero;
        _core.enabled = true;
        _visuals.ResetVisuals();
        _bedStyleVisuals.ResetVisuals();
        _bedLayerVisuals.ResetVisuals();
        _tensionVignette.ResetVisuals();
        _featureVisuals.ResetVisuals();
        _soundManager.StartMusic();
        SetCoreColor(Colors[0]);
        _coreIcon.enabled = true;
        ShowSelectedColor();
        _cooldownArc.enabled = false;
        UIManager.Instance.PushPanel(UIPanelType.GAME_PANEL);

        UpdateHud();
    }

    void Update()
    {
        if (!gamePanel.IsPlaying || !_gameManager.IsPlaying)
        {
            if (_targetMarkers != null)
                _targetMarkers.Hide();
            return;
        }

        int beat = _soundManager.CurrentBeat;
        // The round's difficulty steers the music's section choice, pattern-variation weights and guest-loop length (the
        // layers follow the combo ladder, not this).
        _soundManager.Intensity = SpawnDirector.Difficulty(_soundManager.RoundSeconds);
        _soundManager.Tension = TensionMeter.Compute(_enemyStates, CoreRadius, _arenaRadius, _health, MaxHealth);
        _soundManager.Danger = _health == 1;
        _soundManager.ComboCount = _pulseCombo;
        int step = _soundManager.CurrentStep;
        double beatPosition = _soundManager.BeatPosition;
        // The music can restart mid-round (output sample rate change): the beat jumps back, so unlock the rules.
        if (beatPosition < _prevBeatPosition)
        {
            _pulseCooldown.Reset();
            _spawnDirector.Reset();
        }
        _prevBeatPosition = beatPosition;
        bool fever = _soundManager.FeverActive;
        if (fever != _wasFeverActive)
        {
            _wasFeverActive = fever;
            // Fever pulses kill every color, so the shape of the selected one would mislead.
            _coreIcon.enabled = !fever;
            if (!fever)
            {
                _feverCharge = 0;
                SetCoreColor(Colors[_selectedColor]);
            }
            UpdateHud();
        }
        UpdateCoreLook(fever, beatPosition);
        if (beat != _lastHudBeat)
        {
            _lastHudBeat = beat;
            UpdateHud();
        }

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.aKey.wasPressedThisFrame)
        {
            _selectedColor = (_selectedColor + 1) % Colors.Length;
            if (!fever)
                SetCoreColor(Colors[_selectedColor]);
            ShowSelectedColor();
            UpdateHud();
        }

        // The Space that closed the help card started this round; it is not a shot.
        // Outside fever a press fires only off cooldown; one just before it ends is held and fires when it does.
        bool fire = keyboard != null && keyboard.spaceKey.wasPressedThisFrame && Time.frameCount != _roundStartFrame
                    && _pulseCooldown.Press(beatPosition, fever);
        if (_pulseCooldown.Tick(beatPosition, fever))
            fire = true;
        if (fire)
            FirePulse(fever);
        DrawCooldownArc(_pulseCooldown.Progress(beatPosition, fever));

        float deltaTime = Time.deltaTime;
        AdvanceWaves(deltaTime);
        UpdateDying(deltaTime);
        for (int i = _enemyStates.Count - 1; i >= 0; i--)
        {
            var state = _enemyStates[i];
            var enemy = _enemyViews[state];
            state.Distance = Mathf.Max(0f, state.Distance - state.Speed * deltaTime);
            if (state.Distance <= CoreRadius + state.HalfSize)
            {
                _enemyStates.RemoveAt(i);
                _enemyViews.Remove(state);
                // Invincible after a hit: it just breaks on the core, no damage (ShatterEnemyView pools it).
                if (Time.time < _invincibleUntil)
                {
                    ShatterEnemyView(enemy);
                    continue;
                }
                _poolManager.ReturnToPool(enemy.Instance);
                // The rest of a group that already cost a health point: gone, no second penalty.
                if (!_coreHits.CostsHealth(state.Group))
                    continue;
                _health--;
                _coreDamage.SetHealth(_health, MaxHealth);
                ResetPulseCombo();
                _soundManager.Hurt();
                UpdateHud();
                if (_health == 0)
                {
                    _gameManager.GameOver();
                    return;
                }
                _invincibleUntil = Time.time + InvincibleSeconds;
                _clearAfterHit = true;
                continue;
            }

            var position = enemy.Direction * state.Distance;
            // A boosted charger trembles sideways around its path; the offset is recomputed, never accumulated.
            if (state.Boosts > 0)
            {
                float t = Time.time;
                float shake = Mathf.Sin(t * ShakeFrequencyA + enemy.ShakePhase) * 0.6f
                              + Mathf.Sin(t * ShakeFrequencyB + enemy.ShakePhase * 1.7f) * 0.4f;
                position += new Vector2(-enemy.Direction.y, enemy.Direction.x) * (shake * ShakePerBoost * state.Boosts);
            }
            enemy.Instance.transform.position = transform.position + (Vector3)position;
        }
        if (_clearAfterHit)
        {
            _clearAfterHit = false;
            ClearEnemiesAfterHit();
        }
        DrawInvincibleCore();

        // Again after this frame's wave resolutions and core hits (the last places the combo changes): SoundManager reads
        // it in its next Update, one frame later, not two.
        _soundManager.ComboCount = _pulseCombo;

        var spawnContext = new SpawnContext
        {
            Step = step,
            Fever = fever,
            Breath = _soundManager.IsBreath,
            Section = _soundManager.SectionIndex,
            RoundSeconds = _soundManager.RoundSeconds
        };
        if (_spawnDirector.TryNext(spawnContext, out var order))
        {
            // A slot of 2-3: plain enemies of the same colour and speed from other directions, so one pulse takes the
            // whole group. Only the first one can be gray or a special kind. They share a group id: reaching the core
            // together costs one health (CoreHitRule).
            int group = order.Count > 1 ? ++_spawnGroup : 0;
            SpawnFromNewDirection(order, group, false);
            var mate = order.Mate();
            for (int i = 1; i < order.Count; i++)
                SpawnFromNewDirection(mate, group, true);
        }

        UpdateTargetMarkers(fever);
    }

    void FirePulse(bool fever)
    {
        var ring = CreateRing("Pulse", fever ? Color.white : Colors[_selectedColor], CoreRadius, WaveWidth);
        if (fever)
            ring.colorGradient = _pulseRainbow;
        _waves.Add(new Wave
        {
            Ring = ring,
            Pulse = new PulseState { ColorIndex = _selectedColor, Radius = CoreRadius, IsFever = fever }
        });
    }

    // The core's selected color told by shape as well as by color.
    void ShowSelectedColor()
    {
        _coreIcon.sprite = _enemyShapes[_selectedColor];
        _coreIcon.color = Colors[_selectedColor];
        _cooldownArc.startColor = _cooldownArc.endColor = Colors[_selectedColor];
    }

    // Elapsed cooldown as an open arc that grows clockwise from 12 o'clock; hidden once ready.
    // Progress is carried by the arc's length only, never by its color.
    void DrawCooldownArc(float progress)
    {
        if (progress >= 1f)
        {
            _cooldownArc.enabled = false;
            return;
        }

        _cooldownArc.enabled = true;
        int points = Mathf.Max(2, Mathf.CeilToInt(progress * CircleSegments) + 1);
        _cooldownArc.positionCount = points;
        float sweep = progress * 2f * Mathf.PI;
        for (int i = 0; i < points; i++)
        {
            float angle = 0.5f * Mathf.PI - sweep * i / (points - 1);
            _cooldownArc.SetPosition(i,
                new Vector3(Mathf.Cos(angle) * CooldownArcRadius, Mathf.Sin(angle) * CooldownArcRadius, 0f));
        }
    }

    // Marks the enemies the next pulse reaches first: the nearest one, plus any others close enough to be hit
    // by the same pulse front. Hidden in fever, where every pulse kills everything.
    void UpdateTargetMarkers(bool fever)
    {
        _targets.Clear();
        if (fever || _enemyStates.Count == 0)
        {
            _targetMarkers.Hide();
            return;
        }

        // "First" is decided exactly as the pulse resolver decides it: where a pulse fired now would meet each enemy
        // (size and speed included), with the same tie band. Shoot a marked enemy's color and that enemy dies.
        float waveSpeed = PulseSpeed;
        if (!(waveSpeed > 0f) || float.IsInfinity(waveSpeed))
        {
            _targetMarkers.Hide();
            return;
        }
        float first = float.MaxValue;
        EnemyState nearestState = null;
        foreach (var state in _enemyStates)
        {
            float contact = PulseResolver.ContactRadius(state, CoreRadius, waveSpeed);
            if (contact >= 0f && contact < first)
            {
                first = contact;
                nearestState = state;
            }
        }
        if (nearestState == null)
        {
            _targetMarkers.Hide();
            return;
        }
        foreach (var state in _enemyStates)
        {
            float contact = PulseResolver.ContactRadius(state, CoreRadius, waveSpeed);
            if (contact < 0f || contact > first + PulseResolver.SimultaneousContactTolerance)
                continue;
            var view = _enemyViews[state];
            // A stripped pair keeps only one shape, off to one side of the root: mark that shape.
            Vector3 markerPosition = view.Instance.transform.position;
            if (view.LeftShape.enabled && !view.RightShape.enabled)
                markerPosition = view.LeftShape.transform.position;
            else if (view.RightShape.enabled && !view.LeftShape.enabled)
                markerPosition = view.RightShape.transform.position;
            int markerColor = state.ColorIndex;
            if (state.IsChordPair && state.SecondColorIndex == _selectedColor)
                markerColor = _selectedColor;
            _targets.Add(new TargetMarkers.Target
            {
                Position = markerPosition,
                Color = state.IsHidden ? HiddenColor : Colors[markerColor],
                // A gray enemy takes any color; others need the matching one (an intact chord pair: either of its two).
                Ready = state.IsHidden || state.ColorIndex == _selectedColor
                        || (state.IsChordPair && state.SecondColorIndex == _selectedColor)
            });
        }

        float touch = CoreRadius + nearestState.HalfSize;
        float outer = Mathf.Max(touch + 0.01f, _arenaRadius * GameplayTuning.TensionOuterRadius);
        float danger = 1f - Mathf.Clamp01((nearestState.Distance - touch) / (outer - touch));
        int beat = _soundManager.CurrentBeat;
        float sinceBeat = beat >= 0 ? (float)(_soundManager.BeatPosition - beat) : 1f;
        _targetMarkers.Show(_targets, danger, sinceBeat);
    }

    // Two counters move together but are separate:
    // - pulse combo (shown on screen): +1 for every pulse that kills, fever pulses included; never touched by fever.
    // - fever charge: the same count, but only outside fever. Reaching PulseComboForFever queues fever; it is frozen
    //   while fever is queued or playing and starts again from 0 once fever ends.
    void AddPulseCombo(bool feverPulse)
    {
        _pulseCombo++;
        if (_pulseCombo > _maxPulseCombo)
            _maxPulseCombo = _pulseCombo;
        if (!feverPulse && !_soundManager.FeverActive && !_soundManager.FeverPending &&
            ++_feverCharge >= PulseComboForFever)
            _soundManager.QueueFever();
    }

    // This pulse missed (the combo breaks): "MISS" on the wrong-colour enemy that blocked it, or on the core when the
    // pulse hit nothing (or that enemy is already gone).
    void ShowMiss(PulseState pulse)
    {
        var position = _core.transform.position;
        if (pulse.WrongColorEnemy != null && _enemyViews.TryGetValue(pulse.WrongColorEnemy, out var view))
            position = view.Instance.transform.position;
        _missPopups.Show(position);
    }

    void ResetPulseCombo()
    {
        _pulseCombo = 0;
        if (!_soundManager.FeverActive && !_soundManager.FeverPending)
            _feverCharge = 0;
        UpdateHud();
    }

    // Fever: the core turns into a full hue wheel that spins around the ring.
    // The fever rainbow is fixed on the ring (the ring itself turns, see UpdateCoreLook); flash whitens it.
    void DrawRainbowCore(float flash)
    {
        for (int i = 0; i < _rainbowKeys.Length; i++)
        {
            float t = i / (_rainbowKeys.Length - 1f);
            var hue = Color.HSVToRGB(t, 0.75f, 1f);
            _rainbowKeys[i] = new GradientColorKey(Color.Lerp(hue, Color.white, RingFlash * flash), t);
        }
        // Alpha from the core damage: its chips show as gaps in the rainbow too.
        _rainbow.SetKeys(_rainbowKeys, _coreDamage.AlphaKeys);
        _pulseRainbow.SetKeys(_rainbowKeys, OpaqueAlpha);
        _coreDamage.AlphaDirty = false;
        _core.colorGradient = _rainbow;
    }

    // The core's colour as a whole new gradient (startColor/endColor only recolor a gradient's first and last keys,
    // so after the rainbow they would leave its middle keys showing), keeping the chips its damage cut out of the
    // ring as alpha keys.
    void SetCoreColor(Color color)
    {
        // Called every frame for the beat flash: reuse one gradient and skip when nothing changed.
        if (color == _coreAppliedColor && !_coreDamage.AlphaDirty)
            return;
        _coreAppliedColor = color;
        _coreSolidKeys[0] = new GradientColorKey(color, 0f);
        _coreSolidKeys[1] = new GradientColorKey(color, 1f);
        _coreSolid.SetKeys(_coreSolidKeys, _coreDamage.AlphaKeys);
        _core.colorGradient = _coreSolid;
        _coreDamage.AlphaDirty = false;
    }

    // Every frame: the beat flash (ring and centre shape whiten on each beat, harder on the kick) and, in fever,
    // the rainbow ring turning as a whole.
    void UpdateCoreLook(bool fever, double beatPosition)
    {
        float flash = 0f;
        if (beatPosition >= 0)
        {
            int beat = (int)System.Math.Floor(beatPosition);
            float since = (float)((beatPosition - beat) * _soundManager.BeatSeconds);
            flash = Mathf.Exp(-since / BeatFlashSeconds) * (_soundManager.KickOn(beat) ? 1f : 0.6f);
            if (flash < 0.01f)
                flash = 0f;
        }
        if (fever)
        {
            float barSeconds = (float)_soundManager.BeatSeconds * SoundManager.BeatsPerBar;
            _coreSpin = Mathf.Repeat(_coreSpin - 360f * FeverTurnsPerBar / Mathf.Max(0.1f, barSeconds) * Time.deltaTime, 360f);
            _core.transform.localRotation = Quaternion.Euler(0f, 0f, _coreSpin);
            DrawRainbowCore(flash);
            _coreAppliedColor = Color.clear;   // the solid colour must be rewritten after fever
        }
        else
            SetCoreColor(Color.Lerp(Colors[_selectedColor], Color.white, RingFlash * flash));
        _coreIcon.color = Color.Lerp(Colors[_selectedColor], Color.white, IconFlash * flash);
    }

    float PulseMaxRadius => _arenaRadius + GameplayTuning.BaseEnemyHalfSize + PulseResolver.WaveHitPadding * 2f;
    float PulseSpeed => (PulseMaxRadius - CoreRadius) / (PulseBeats * (float)_soundManager.BeatSeconds);

    void AdvanceWaves(float deltaTime)
    {
        float maxRadius = PulseMaxRadius;
        float waveSpeed = PulseSpeed;

        for (int i = 0; i < _waves.Count;)
        {
            var wave = _waves[i];
            var pulse = wave.Pulse;
            _pulseEvents.Clear();
            var outcome = _pulseResolver.Advance(pulse, _enemyStates, waveSpeed, maxRadius, deltaTime, _pulseEvents);

            for (int e = 0; e < _pulseEvents.Count; e++)
            {
                var pulseEvent = _pulseEvents[e];
                if (pulseEvent.Type == PulseEventType.Split)
                {
                    // The resolver already added the child state; it needs a view. The parent's Kill comes after
                    // both Splits, so its view still holds the direction. No kill sound, score or combo here.
                    // The two Splits of one parent come back to back: the first child goes one way, the second the other.
                    bool second = e > 0 && _pulseEvents[e - 1].Type == PulseEventType.Split
                                        && _pulseEvents[e - 1].Parent == pulseEvent.Parent;
                    SpawnSplitChild(pulseEvent.Enemy, _enemyViews[pulseEvent.Parent], second ? -1f : 1f);
                    continue;
                }
                var enemy = _enemyViews[pulseEvent.Enemy];
                if (pulseEvent.Type == PulseEventType.Boost)
                {
                    // A wrong color sped a charger up (the resolver already raised Speed and Boosts):
                    // longer afterimages here, the tremble in Update. Its color never changes.
                    ApplyEnemyAppearance(enemy);
                    continue;
                }
                if (pulseEvent.Type == PulseEventType.Reveal)
                {
                    // A gray enemy has two hits: a colored pulse only strips the gray and shows its real color.
                    ApplyEnemyAppearance(enemy);
                    continue;
                }
                if (pulseEvent.Type == PulseEventType.Strip)
                {
                    // One half of a chord pair pops: no kill sound, score or combo; the other shape stays where it is.
                    // The resolver already moved the survivor into ColorIndex and the popped color into StrippedColorIndex.
                    int popped = pulseEvent.Enemy.StrippedColorIndex;
                    var poppedShape = popped == enemy.LeftColor ? enemy.LeftShape : enemy.RightShape;
                    Shatter(poppedShape.transform.position, Colors[popped]);
                    ApplyEnemyAppearance(enemy);
                    continue;
                }

                // The resolver already removed the state from _enemyStates.
                _enemyViews.Remove(pulseEvent.Enemy);
                // Every kill of this pulse joins the chord of its first kill: one press, one chord.
                int chordStep = _soundManager.QueueKillChord(enemy.State.ColorIndex, wave.ChordStep);
                if (wave.ChordStep < 0)
                    wave.ChordStep = chordStep;
                // It bursts on that chord's beat, not now (frozen and hollow until then).
                StartDying(enemy, wave.ChordStep);
                // A chord pair's finish (or a fever kill of an intact one) sounds both of its colors in that chord.
                int partnerColor = enemy.State.IsChordPair ? enemy.State.SecondColorIndex : enemy.State.StrippedColorIndex;
                if (partnerColor >= 0)
                    _soundManager.QueueKillChord(partnerColor, wave.ChordStep);
                // The n-th kill of one pulse is worth n points.
                _score += pulseEvent.Points;
                if (pulseEvent.Points == 1)
                    AddPulseCombo(pulse.IsFever);
                UpdateHud();
            }

            if (outcome != PulseOutcome.Travelling)
            {
                // Pulse combo: a pulse that killed something already scored +1. One that revealed a gray enemy or
                // stripped a chord pair keeps the combo (no +1), even if it also hit a wrong color (their slot mates
                // come in at the same distance). One that only hit a wrong color, or reached the edge, breaks it.
                // A fever pulse that kills nothing keeps it too.
                if (!pulse.IsFever && pulse.Kills == 0 && !pulse.HitProgress)
                {
                    // A miss shows even at combo 0: the player still learns why the shot failed.
                    ResetPulseCombo();
                    ShowMiss(pulse);
                }
                Destroy(wave.Ring.gameObject);
                _waves.RemoveAt(i);
            }
            else
            {
                DrawCircle(wave.Ring, pulse.Radius);
                // Fever pulses spin the same hue wheel as the core.
                if (pulse.IsFever)
                    wave.Ring.colorGradient = _pulseRainbow;
                // A stopped pulse fades out over its short run-on.
                if (pulse.IsStopping && !pulse.IsFever)
                {
                    var color = Colors[pulse.ColorIndex];
                    color.a = Mathf.Clamp01((pulse.StopRadius - pulse.Radius) / GameplayTuning.PulsePassThrough);
                    wave.Ring.startColor = wave.Ring.endColor = color;
                }
                i++;
            }
        }
    }

    void SpawnFromNewDirection(SpawnOrder order, int group = 0, bool mate = false)
    {
        // Keep consecutive spawns at least 60 degrees apart, including within a color group. A mate turns less
        // (60..150), which also keeps the third enemy of a slot away from the first.
        float turn = mate ? SpawnDirector.MateAngleStep(Random.value) : Random.Range(60f, 300f);
        _spawnAngle = Mathf.Repeat(_spawnAngle + turn, 360f);
        float radians = _spawnAngle * Mathf.Deg2Rad;
        var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        bool chordPair = order.SecondColorIndex >= 0;
        float halfSize = GameplayTuning.BaseEnemyHalfSize;
        if (chordPair)
            halfSize *= GameplayTuning.ChordShapeScale;
        else if (order.Kind == EnemyKind.Splitter)
            halfSize *= GameplayTuning.SplitterScale;
        else if (order.Kind == EnemyKind.Charger)
            halfSize *= GameplayTuning.ChargerScale;
        var state = new EnemyState
        {
            Kind = order.Kind,
            Group = group,
            ColorIndex = order.ColorIndex,
            SecondColorIndex = order.SecondColorIndex,
            Distance = _arenaRadius,
            Speed = order.Speed,
            HalfSize = halfSize,
            IsHidden = order.Hidden
        };
        CreateEnemyView(state, direction);
        _enemyStates.Add(state);
    }

    // A split child: its state is already in _enemyStates (added by the resolver) at the parent's distance.
    // It heads in along the parent's direction turned a little to one side (side = +1 or -1).
    void SpawnSplitChild(EnemyState child, Enemy parent, float side)
    {
        float spreadAngle = Mathf.Asin(Mathf.Clamp01(SplitSpreadOffset / Mathf.Max(child.Distance, 0.0001f))) * Mathf.Rad2Deg;
        var direction = (Vector2)(Quaternion.Euler(0f, 0f, side * spreadAngle) * parent.Direction);
        CreateEnemyView(child, direction);
    }

    // Takes an object from the pool, places it at the state's distance facing the core, and draws it from the state.
    void CreateEnemyView(EnemyState state, Vector2 direction)
    {
        float angle = Vector2.SignedAngle(Vector2.up, -direction);
        var rotation = Quaternion.Euler(0f, 0f, angle);
        var instance = _poolManager.GetFromPool(EnemyPoolKey,
            transform.position + (Vector3)(direction * state.Distance), rotation);
        var root = instance.transform;
        var enemy = new Enemy
        {
            State = state,
            Instance = instance,
            Direction = direction,
            Body = instance.GetComponent<SpriteRenderer>(),
            LeftShape = root.GetChild(ChordLeftChild).GetComponent<SpriteRenderer>(),
            RightShape = root.GetChild(ChordRightChild).GetComponent<SpriteRenderer>(),
            Bar = root.GetChild(ChordBarChild).GetComponent<SpriteRenderer>(),
            InnerLeft = root.GetChild(SplitterInnerLeftChild).GetComponent<SpriteRenderer>(),
            InnerRight = root.GetChild(SplitterInnerRightChild).GetComponent<SpriteRenderer>(),
            TrailNear = root.GetChild(TrailNearChild).GetComponent<SpriteRenderer>(),
            TrailFar = root.GetChild(TrailFarChild).GetComponent<SpriteRenderer>(),
            ShakePhase = Random.Range(0f, Mathf.PI * 2f),
            LeftColor = state.IsChordPair ? state.ColorIndex : -1
        };
        ApplyEnemyAppearance(enemy);
        _enemyViews.Add(state, enemy);
    }

    // Redraws every part from the state, so a pooled object never keeps a previous enemy's look.
    // A chord pair: two small shapes side by side across the direction of travel, joined by a neutral bar.
    // Once one half is stripped, only the survivor stays, in its own place.
    void ApplyEnemyAppearance(Enemy enemy)
    {
        var state = enemy.State;
        bool intactPair = state.IsChordPair;
        bool strippedPair = !intactPair && state.StrippedColorIndex >= 0;
        bool single = !intactPair && !strippedPair;
        bool splitter = single && state.Kind == EnemyKind.Splitter && !state.IsHidden;
        bool charger = single && state.Kind == EnemyKind.Charger;

        // Root scale: a chord pair keeps the layout space of one basic enemy; everything else is drawn at its
        // HalfSize (basic, split child, splitter), a charger squeezed across and stretched along its path.
        float size = state.HalfSize * 2f;
        float layout = GameplayTuning.BaseEnemyHalfSize * 2f;
        enemy.Instance.transform.localScale = !single ? new Vector3(layout, layout, layout)
            : charger ? new Vector3(size * ChargerWidth, size * ChargerLength, size)
            : new Vector3(size, size, size);

        enemy.Body.enabled = single;
        if (single)
        {
            int shape = state.IsHidden ? Colors.Length : state.ColorIndex;
            enemy.Body.sprite = _enemyShapes[splitter ? OutlineShape(shape) : shape];
            enemy.Body.color = state.IsHidden ? HiddenColor : Colors[state.ColorIndex];
        }

        // Splitter: two small filled shapes inside the outline.
        enemy.InnerLeft.enabled = splitter;
        enemy.InnerRight.enabled = splitter;
        if (splitter)
        {
            float height = SplitterInnerHeight[state.ColorIndex];
            float spacing = SplitterInnerSpacing[state.ColorIndex];
            SetKindPart(enemy.InnerLeft, state.ColorIndex, 1f,
                new Vector3(-spacing, height, 0f), new Vector3(SplitterInnerScale, SplitterInnerScale, 1f));
            SetKindPart(enemy.InnerRight, state.ColorIndex, 1f,
                new Vector3(spacing, height, 0f), new Vector3(SplitterInnerScale, SplitterInnerScale, 1f));
        }

        // Charger: two fading afterimages behind it; each boost spreads and stretches them further.
        enemy.TrailNear.enabled = charger;
        enemy.TrailFar.enabled = charger;
        if (charger)
        {
            int shape = state.IsHidden ? Colors.Length : state.ColorIndex;
            float growth = 1f + TrailBoostGrowth * state.Boosts;
            SetTrail(enemy.TrailNear, shape, 0, growth);
            SetTrail(enemy.TrailFar, shape, 1, growth);
        }

        bool leftShown = intactPair || (strippedPair && state.ColorIndex == enemy.LeftColor);
        bool rightShown = intactPair || (strippedPair && state.ColorIndex != enemy.LeftColor);
        enemy.LeftShape.enabled = leftShown;
        enemy.RightShape.enabled = rightShown;
        enemy.Bar.enabled = intactPair;
        if (leftShown)
            SetShape(enemy.LeftShape, enemy.LeftColor);
        if (rightShown)
            SetShape(enemy.RightShape, intactPair ? state.SecondColorIndex : state.ColorIndex);
    }

    void SetShape(SpriteRenderer renderer, int colorIndex)
    {
        renderer.sprite = _enemyShapes[colorIndex];
        renderer.color = Colors[colorIndex];
    }

    // Index of the hollow outline of a filled shape in _enemyShapes.
    static int OutlineShape(int shape) => Colors.Length + 1 + shape;

    void SetKindPart(SpriteRenderer renderer, int shape, float alpha, Vector3 localPosition, Vector3 localScale)
    {
        renderer.sprite = _enemyShapes[shape];
        var color = shape < Colors.Length ? Colors[shape] : HiddenColor;
        color.a = alpha;
        renderer.color = color;
        renderer.transform.localPosition = localPosition;
        renderer.transform.localScale = localScale;
    }

    // One charger afterimage (index 0 = near, 1 = far), in the root's local space where one unit is its length.
    void SetTrail(SpriteRenderer renderer, int shape, int index, float growth)
    {
        float behind = -TrailSpacing * (index + 1) * growth;
        SetKindPart(renderer, shape, TrailAlpha[index],
            new Vector3(0f, behind, TrailDepth), new Vector3(TrailWidth[index], growth, 1f));
    }

    // Game over: play the collapse (everything shakes, then bursts outward from the core) before the Restart
    // screen; the music goes muffled and fades meanwhile (timeline in GameOverSequence).
    void HandleGameOver()
    {
        _gameManager.DelayGameOverPresentation();
        _gameManager.SetRoundResult(_score, _maxPulseCombo);
        _soundManager.FadeOutMusic(GameOverSequence.TotalSeconds - 0.2f);
        if (_targetMarkers != null)
            _targetMarkers.Hide();

        _gameOverTargets.Clear();
        _gameOverTargets.Add(new GameOverSequence.Target { Transform = _core.transform, Distance = 0f, Burst = BurstCore });
        _gameOverTargets.Add(new GameOverSequence.Target { Transform = _boundary.transform, Distance = _arenaRadius });
        foreach (var enemy in _enemyViews.Values)
        {
            var view = enemy;
            _gameOverTargets.Add(new GameOverSequence.Target
            {
                Transform = view.Instance.transform,
                Distance = Vector2.Distance(view.Instance.transform.position, transform.position),
                Burst = () => ShatterEnemyView(view)
            });
        }
        foreach (var dying in _dying)
        {
            var d = dying;
            _gameOverTargets.Add(new GameOverSequence.Target
            {
                Transform = d.View.Instance.transform,
                Distance = Vector2.Distance(d.View.Instance.transform.position, transform.position),
                Burst = () =>
                {
                    if (_dying.Remove(d))
                        Burst(d);
                }
            });
        }
        foreach (var wave in _waves)
        {
            var w = wave;
            _gameOverTargets.Add(new GameOverSequence.Target
            {
                Transform = w.Ring.transform,
                Distance = w.Pulse.Radius,
                Burst = () => ShatterRing(w.Ring, w.Pulse.Radius)
            });
        }
        _gameOverSequence.Play(_gameOverTargets, () =>
        {
            _soundManager.StopMusic();
            _gameManager.PresentGameOver();
        });
    }

    // The core goes first and biggest: the shockwave starts from it.
    void BurstCore()
    {
        var color = Colors[_selectedColor];
        for (int i = 0; i < 3; i++)
            Shatter(_core.transform.position, color);
        _core.enabled = false;
        _coreDamage.HideCracks();
        _coreIcon.enabled = false;
        _cooldownArc.enabled = false;
    }

    // A core hit (that left health): the screen shakes and every enemy on the field breaks apart, so the player gets
    // a clean restart under the invincibility window. No score, combo or kill chord for these.
    void ClearEnemiesAfterHit()
    {
        _tensionVignette.Shake(HitShakeSeconds, HitShakeStrength);
        foreach (var state in _enemyStates)
            ShatterEnemyView(_enemyViews[state]);
        _enemyStates.Clear();
        _enemyViews.Clear();
    }

    // Invincible: the core blinks until the window ends.
    void DrawInvincibleCore()
    {
        bool invincible = Time.time < _invincibleUntil;
        bool visible = !invincible || Mathf.Repeat(Time.time * InvincibleBlinkRate, 1f) < 0.6f;
        if (_core.enabled != visible)
        {
            _core.enabled = visible;
            _coreIcon.enabled = visible && !_soundManager.FeverActive;
        }
    }

    // A live enemy caught by the game-over shockwave: shards from every shape it shows, in its colours.
    void ShatterEnemyView(Enemy enemy)
    {
        if (enemy.Instance == null || !enemy.Instance.activeSelf)
            return;
        if (enemy.Body.enabled)
            Shatter(enemy.Instance.transform.position, enemy.Body.color);
        else
        {
            if (enemy.LeftShape.enabled)
                Shatter(enemy.LeftShape.transform.position, enemy.LeftShape.color);
            if (enemy.RightShape.enabled)
                Shatter(enemy.RightShape.transform.position, enemy.RightShape.color);
        }
        _poolManager.ReturnToPool(enemy.Instance);
    }

    // A pulse ring breaks into shards around its circle (hidden, not destroyed: StartRound destroys the rings).
    void ShatterRing(LineRenderer ring, float radius)
    {
        if (!ring.enabled)
            return;
        var color = ring.startColor;
        color.a = 1f;
        const int points = 6;
        for (int i = 0; i < points; i++)
        {
            float a = i * (2f * Mathf.PI / points);
            Shatter(transform.position + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius, color);
        }
        ring.enabled = false;
    }

    // Kill confirmed: the enemy stops, flashes white and turns into a hollow outline of its colour(s), then charges
    // up (shrinks, trembles, brightens) until the kill chord's beat, where it shatters. Its parts and colours are
    // captured now because the look changes.
    void StartDying(Enemy enemy, int burstStep)
    {
        var dying = _dyingPool.Count > 0 ? _dyingPool.Pop() : new DyingEnemy();
        dying.View = enemy;
        dying.BurstStep = burstStep;
        dying.Age = 0f;
        dying.Origin = enemy.Instance.transform.position;
        dying.BaseScale = enemy.Instance.transform.localScale;
        dying.Parts = 0;
        var state = enemy.State;
        if (enemy.Body.enabled)
            AddDyingPart(dying, enemy.Body, state.ColorIndex);
        else
        {
            // Same colour rule as ApplyEnemyAppearance: an intact pair shows LeftColor and SecondColorIndex,
            // a stripped one only its survivor (ColorIndex) on its own side.
            if (enemy.LeftShape.enabled)
                AddDyingPart(dying, enemy.LeftShape, enemy.LeftColor);
            if (enemy.RightShape.enabled)
                AddDyingPart(dying, enemy.RightShape, state.IsChordPair ? state.SecondColorIndex : state.ColorIndex);
        }
        // Everything that suggests motion or a living enemy goes at once.
        enemy.Bar.enabled = false;
        enemy.InnerLeft.enabled = false;
        enemy.InnerRight.enabled = false;
        enemy.TrailNear.enabled = false;
        enemy.TrailFar.enabled = false;
        _dying.Add(dying);
    }

    void AddDyingPart(DyingEnemy dying, SpriteRenderer part, int colorIndex)
    {
        int i = dying.Parts++;
        dying.PartRenderers[i] = part;
        dying.PartColors[i] = Colors[colorIndex];
        part.sprite = _enemyShapes[OutlineShape(colorIndex)];
        part.color = Color.white;
    }

    void UpdateDying(float deltaTime)
    {
        double beat = _soundManager.BeatPosition;
        double heardStep = beat * SoundManager.StepsPerBeat;
        float secondsPerStep = (float)_soundManager.BeatSeconds / SoundManager.StepsPerBeat;
        for (int i = _dying.Count - 1; i >= 0; i--)
        {
            var dying = _dying[i];
            dying.Age += deltaTime;
            // Burst when the chord is heard; at once if there is no chord (music off) or it is somehow overdue.
            bool burst = dying.BurstStep < 0 || beat < 0 || heardStep >= dying.BurstStep || dying.Age >= DyingMaxWait;
            if (burst)
            {
                Burst(dying);
                _dying.RemoveAt(i);
                continue;
            }

            // 0 at the kill, 1 on the beat: drives the charge-up.
            float remaining = (float)(dying.BurstStep - heardStep) * secondsPerStep;
            float charge = Mathf.Clamp01(dying.Age / Mathf.Max(0.01f, dying.Age + remaining));
            var root = dying.View.Instance.transform;
            float push = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dying.Age / DyingPushSeconds)) * DyingPushDistance;
            float tremble = DyingTremble * charge * charge;
            var jitter = new Vector3(Mathf.Sin(dying.Age * 61f), Mathf.Sin(dying.Age * 47f + 1.3f), 0f) * tremble;
            root.position = dying.Origin + (Vector3)(dying.View.Direction * push) + jitter;
            float squeeze = 1f - DyingSqueeze * charge;
            for (int p = 0; p < dying.Parts; p++)
            {
                var part = dying.PartRenderers[p];
                // White flash first, then its own colour coming back brighter as the burst nears.
                var target = Color.Lerp(dying.PartColors[p], Color.white, 0.35f * charge);
                var c = dying.Age < DyingFlashSeconds ? Color.white : target;
                c.a = 0.55f + 0.45f * charge;
                part.color = c;
            }
            root.localScale = dying.BaseScale * squeeze;
        }
    }

    // The kill chord sounds now: shatter every captured part in its own colour and return the object.
    void Burst(DyingEnemy dying)
    {
        for (int p = 0; p < dying.Parts; p++)
            Shatter(dying.PartRenderers[p].transform.position, dying.PartColors[p]);
        _poolManager.ReturnToPool(dying.View.Instance);
        dying.View = null;
        _dyingPool.Push(dying);
    }

    // Chord pair parts on the enemy template, hidden until ApplyEnemyAppearance shows them.
    // Children use the root's local space, where one unit is one basic enemy across.
    void CreateChordParts(Transform root)
    {
        float scale = GameplayTuning.ChordShapeScale;
        float offset = scale * ChordShapeSpacing * 0.5f;
        CreateChordPart(root, "Chord Left", new Vector3(-offset, 0f, 0f), Vector3.one * scale);
        CreateChordPart(root, "Chord Right", new Vector3(offset, 0f, 0f), Vector3.one * scale);
        var bar = CreateChordPart(root, "Chord Bar", new Vector3(0f, 0f, ChordBarDepth),
            new Vector3(offset * 2f, ChordBarThickness / (GameplayTuning.BaseEnemyHalfSize * 2f), 1f));
        bar.sprite = _enemyShapes[Colors.Length];
        bar.color = ChordBarColor;
    }

    // Splitter and charger parts on the enemy template, hidden until ApplyEnemyAppearance places and shows them.
    // Children 3..6 (after the chord parts): splitter inner left, inner right, charger near and far afterimage.
    static void CreateKindParts(Transform root)
    {
        CreateChordPart(root, "Splitter Inner Left", Vector3.zero, Vector3.one);
        CreateChordPart(root, "Splitter Inner Right", Vector3.zero, Vector3.one);
        CreateChordPart(root, "Charger Trail Near", Vector3.zero, Vector3.one);
        CreateChordPart(root, "Charger Trail Far", Vector3.zero, Vector3.one);
    }

    static SpriteRenderer CreateChordPart(Transform root, string objectName, Vector3 localPosition, Vector3 localScale)
    {
        var part = new GameObject(objectName).AddComponent<SpriteRenderer>();
        part.transform.SetParent(root, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.enabled = false;
        return part;
    }

    // Cooldown arc and selected-color shape, both inside the core ring.
    void CreateCoreIndicators()
    {
        _cooldownArc = new GameObject("Pulse Cooldown").AddComponent<LineRenderer>();
        _cooldownArc.transform.SetParent(transform, false);
        _cooldownArc.useWorldSpace = false;
        _cooldownArc.loop = false;
        _cooldownArc.startWidth = _cooldownArc.endWidth = CooldownArcWidth;
        _cooldownArc.sharedMaterial = _ringMaterial;
        _cooldownArc.sortingOrder = 1;
        _cooldownArc.enabled = false;

        _coreIcon = new GameObject("Core Color Shape").AddComponent<SpriteRenderer>();
        _coreIcon.transform.SetParent(transform, false);
        // Shape sprites are one world unit across.
        _coreIcon.transform.localScale = Vector3.one * CoreIconSize;
        _coreIcon.sortingOrder = 1;
    }

    void CreateEnemyShapes()
    {
        const int size = 64;
        int width = size * _enemyShapes.Length;
        _enemyShapeTexture = new Texture2D(width, size, TextureFormat.RGBA32, false);
        _enemyShapeTexture.name = "Chord Pulse Enemy Shapes";
        _enemyShapeTexture.filterMode = FilterMode.Bilinear;
        _enemyShapeTexture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[width * size];

        for (int shape = 0; shape < _enemyShapes.Length; shape++)
        {
            // Past the gray square come the outlines: the same shapes, keeping only a band inside their edge.
            bool outline = shape > Colors.Length;
            int outlineOf = outline ? shape - Colors.Length - 1 : shape;
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) * 2f / size - 1f;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) * 2f / size - 1f;
                    float edge = outlineOf switch
                    {
                        0 => Mathf.Min(v + 0.9f, (0.9f - v) * 0.53f - Mathf.Abs(u)),
                        1 => 0.94f - Mathf.Sqrt(u * u + v * v),
                        2 => 0.97f - Mathf.Abs(u) - Mathf.Abs(v),
                        _ => 0.96f - Mathf.Max(Mathf.Abs(u), Mathf.Abs(v))
                    };
                    if (outline)
                        edge = Mathf.Min(edge, OutlineThickness - edge);
                    byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(edge * size * 0.5f + 0.5f));
                    pixels[y * width + shape * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
        }

        _enemyShapeTexture.SetPixels32(pixels);
        _enemyShapeTexture.Apply(false, true);
        for (int shape = 0; shape < _enemyShapes.Length; shape++)
        {
            var rect = new Rect(shape * size, 0, size, size);
            _enemyShapes[shape] = Sprite.Create(_enemyShapeTexture, rect,
                new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }
    }

    LineRenderer CreateRing(string objectName, Color color, float radius, float width)
    {
        var ring = new GameObject(objectName).AddComponent<LineRenderer>();
        ring.transform.SetParent(transform, false);
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = CircleSegments;
        ring.startWidth = ring.endWidth = width;
        ring.startColor = ring.endColor = color;
        ring.sharedMaterial = _ringMaterial;
        ring.sortingOrder = 1;
        DrawCircle(ring, radius);
        return ring;
    }

    static void DrawCircle(LineRenderer ring, float radius)
    {
        for (int i = 0; i < CircleSegments; i++)
        {
            float angle = i * (2f * Mathf.PI / CircleSegments);
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    void CreateShatterEffect()
    {
        _shards = new GameObject("Enemy Shatter").AddComponent<ParticleSystem>();
        _shards.transform.SetParent(transform, false);
        _shards.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _shards.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 192;

        var emission = _shards.emission;
        emission.enabled = false;
        var shape = _shards.shape;
        shape.enabled = false;

        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
        var color = _shards.colorOverLifetime;
        color.enabled = true;
        color.color = fade;

        var spin = _shards.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

        _shardMesh = new Mesh { name = "Triangular Shard" };
        _shardMesh.vertices = new[] { new Vector3(-0.5f, -0.38f), new Vector3(-0.12f, 0.54f), new Vector3(0.5f, -0.24f) };
        _shardMesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.right };
        _shardMesh.triangles = new[] { 0, 1, 2 };

        var renderer = _shards.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = _shardMesh;
        renderer.sharedMaterial = _ringMaterial;
        renderer.sortingOrder = 2;
        _shards.Play();
    }

    void Shatter(Vector3 position, Color color)
    {
        const int shardCount = 8;
        float offset = Random.Range(0f, Mathf.PI * 2f);
        for (int i = 0; i < shardCount; i++)
        {
            float angle = offset + i * (Mathf.PI * 2f / shardCount);
            var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle));
            _shards.Emit(new ParticleSystem.EmitParams
            {
                position = position + direction * Random.Range(0f, 0.1f),
                velocity = direction * Random.Range(0.65f, 1.8f),
                startLifetime = Random.Range(0.24f, 0.46f),
                startSize = Random.Range(0.07f, 0.16f),
                startColor = Color.Lerp(color, Color.white, Random.Range(0f, 0.35f)),
                rotation = Random.Range(0f, 360f)
            }, 1);
        }
    }

    void UpdateHud()
    {
        gamePanel.SetStatus(_health, MaxHealth, _soundManager.CurrentBpm, _pulseCombo, _score);
    }

    void OnDestroy()
    {
        if (_gameManager != null)
        {
            _gameManager.OnGameRestarted -= StartRound;
            _gameManager.OnGameOver -= HandleGameOver;
        }
        if (_soundManager != null)
            _soundManager.StopMusic();
        if (_enemyTemplate != null)
            Pool.GetPoolByPrefab(_enemyTemplate, false)?.Clear(true);
        foreach (var shape in _enemyShapes)
            Destroy(shape);
        Destroy(_enemyShapeTexture);
        Destroy(_shardMesh);
        Destroy(_ringMaterial);
    }
}
