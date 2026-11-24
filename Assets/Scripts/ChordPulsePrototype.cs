using System.Collections.Generic;
using GameLogic.Core;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class ChordPulsePrototype : MonoBehaviour
{
    const string EnemyPoolKey = "ChordPulseEnemy";
    const float CoreRadius = 0.42f;
    const float EnemyHalfSize = 0.21f;
    const float WaveWidth = 0.028f;
    const float WaveHitPadding = 0.0275f;
    const float SimultaneousContactTolerance = 0.01f;
    const int CircleSegments = 96;
    static readonly Color BoundaryColor = new Color(0.27f, 0.34f, 0.46f);

    static readonly Color[] Colors =
    {
        new Color(1f, 0.29f, 0.35f),
        new Color(0.29f, 0.72f, 1f),
        new Color(1f, 0.83f, 0.25f)
    };

    const int MaxHealth = 3;
    const float HiddenEnemyStart = 30f;
    static readonly Color HiddenColor = new Color(0.62f, 0.65f, 0.71f);

    const float EnemySpeed = 0.65f;
    // A pulse crosses from the core to the arena edge in this many beats, at any tempo,
    // so one press resolves inside a single beat and its kills share one chord.
    const float PulseBeats = 0.75f;
    const float ArenaInset = 0.45f;
    const float SameColorChance = 0.8f;
    const int FeverThreshold = 8;
    // Music tension starts once the nearest enemy is inside this fraction of the arena radius.
    const float TensionOuterRadius = 0.55f;
    // Kills in a row (without being hit) for each music combo level 1..3.
    static readonly int[] ComboThresholds = { 4, 8, 12 };
    // Survival seconds at which difficulty steps up (same timing as the original 8-bar tempo stages).
    static readonly double[] DifficultyThresholds = { 17.8, 34.3, 49.8 };

    [SerializeField] Camera playCamera;
    [SerializeField] GamePanel gamePanel;

    static readonly string[] ColorNames = { "Red", "Blue", "Yellow" };

    sealed class Enemy
    {
        public GameObject Instance;
        public int ColorIndex;
        public float Distance;
        public bool IsHidden;
    }

    sealed class Wave
    {
        public LineRenderer Ring;
        public int ColorIndex;
        public float Radius;
        public bool HasKill;
        /// <summary>Beat step of this pulse's kill chord (-1 until its first kill).</summary>
        public int ChordStep = -1;
        public bool IsFeverPulse;
    }

    readonly List<Enemy> _enemies = new List<Enemy>();
    readonly List<(Enemy Enemy, float Radius)> _contacts = new List<(Enemy, float)>();
    readonly List<Wave> _waves = new List<Wave>();

    PoolManager _poolManager;
    GameManager _gameManager;
    SoundManager _soundManager;
    GameObject _enemyTemplate;
    Material _ringMaterial;
    Texture2D _enemyShapeTexture;
    readonly Sprite[] _enemyShapes = new Sprite[4];
    LineRenderer _core;
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
    int _spawnCount;
    int _openingEnemiesSpawned;
    int _groupColor;
    int _groupSize;
    float _spawnAngle;
    int _hits;
    int _streak;
    int _health;
    int _nextSpawnStep;
    int _feverCharge;
    int _lastHudBeat;
    bool _wasFeverActive;

    void Awake()
    {
        _gameManager = GameManager.Instance;
        _soundManager = SoundManager.Instance;
        _gameManager.OnGameRestarted += StartRound;
        _gameManager.OnGameOver += _soundManager.StopMusic;
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
        CreateShatterEffect();

        CreateEnemyShapes();
        _enemyTemplate = new GameObject("Chord Pulse Enemy Template");
        _enemyTemplate.transform.SetParent(transform, false);
        _enemyTemplate.SetActive(false);
        _enemyTemplate.transform.localScale = Vector3.one * (EnemyHalfSize * 2f);
        _enemyTemplate.AddComponent<SpriteRenderer>();
        _enemyTemplate.AddComponent<Poolable>();

        _poolManager = PoolManager.Instance;
        _poolManager.RegisterPool(EnemyPoolKey, _enemyTemplate, 12);

        StartRound();
    }

    void StartRound()
    {
        _poolManager.ReturnAll(EnemyPoolKey);
        _enemies.Clear();
        foreach (var wave in _waves)
            Destroy(wave.Ring.gameObject);
        _waves.Clear();
        _contacts.Clear();
        _shards.Clear();

        _selectedColor = 0;
        _spawnCount = 0;
        _openingEnemiesSpawned = 0;
        _groupSize = 0;
        _spawnAngle = Random.Range(0f, 360f);
        _hits = 0;
        _streak = 0;
        _health = MaxHealth;
        _nextSpawnStep = 0;
        _feverCharge = 0;
        _lastHudBeat = -1;
        _wasFeverActive = false;
        _visuals.ResetVisuals();
        _bedStyleVisuals.ResetVisuals();
        _bedLayerVisuals.ResetVisuals();
        _tensionVignette.ResetVisuals();
        _featureVisuals.ResetVisuals();
        _soundManager.StartMusic();
        _core.startColor = _core.endColor = Colors[0];
        UIManager.Instance.PushPanel(UIPanelType.GAME_PANEL);

        UpdateHud();
    }

    void Update()
    {
        if (!gamePanel.IsPlaying || !_gameManager.IsPlaying)
            return;

        int beat = _soundManager.CurrentBeat;
        // The arrangement fills in as the round gets harder; slower music later can still lower density on its own.
        _soundManager.Intensity = DifficultyLevel(_soundManager.RoundSeconds);
        _soundManager.Tension = MusicTension();
        _soundManager.Danger = _health == 1;
        _soundManager.Combo = ComboLevel(_streak);
        int step = _soundManager.CurrentStep;
        bool fever = _soundManager.FeverActive;
        if (fever != _wasFeverActive)
        {
            _wasFeverActive = fever;
            _groupSize = 0;
            _nextSpawnStep = step;
            if (!fever)
                _feverCharge = 0;
            UpdateHud();
        }
        if (beat != _lastHudBeat)
        {
            _lastHudBeat = beat;
            UpdateHud();
        }

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.aKey.wasPressedThisFrame)
        {
            _selectedColor = (_selectedColor + 1) % Colors.Length;
            _core.startColor = _core.endColor = Colors[_selectedColor];
            UpdateHud();
        }

        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            _waves.Add(new Wave
            {
                Ring = CreateRing("Pulse", fever ? Color.white : Colors[_selectedColor], CoreRadius, WaveWidth),
                IsFeverPulse = fever,
                ColorIndex = _selectedColor,
                Radius = CoreRadius
            });
        }

        float deltaTime = Time.deltaTime;
        AdvanceWaves(deltaTime);
        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            var enemy = _enemies[i];
            enemy.Distance = Mathf.Max(0f, enemy.Distance - EnemySpeed * deltaTime);
            if (enemy.Distance <= CoreRadius + EnemyHalfSize)
            {
                _poolManager.ReturnToPool(enemy.Instance);
                _enemies.RemoveAt(i);
                _health--;
                _streak = 0;
                _soundManager.Hurt();
                UpdateHud();
                if (_health == 0)
                {
                    _gameManager.GameOver();
                    return;
                }
                continue;
            }

            enemy.Instance.transform.position = Vector3.MoveTowards(
                enemy.Instance.transform.position, transform.position, EnemySpeed * deltaTime);
        }

        if (step >= _nextSpawnStep)
            SpawnNextEnemy(step, fever);
    }

    // 0..1 threat for the adaptive music: the nearest enemy closing in on the core (up to 0.85 at contact),
    // a little more for each extra enemy close by, and a floor from lost lives (0.3 with 2 left, 0.75 with 1).
    static int ComboLevel(int streak)
    {
        int level = 0;
        while (level < ComboThresholds.Length && streak >= ComboThresholds[level])
            level++;
        return level;
    }

    float MusicTension()
    {
        float contact = CoreRadius + EnemyHalfSize;
        float outer = Mathf.Max(contact + 0.01f, _arenaRadius * TensionOuterRadius);
        float nearest = float.MaxValue;
        int close = 0;
        foreach (var enemy in _enemies)
        {
            nearest = Mathf.Min(nearest, enemy.Distance);
            if (enemy.Distance < outer)
                close++;
        }
        float proximity = _enemies.Count == 0 ? 0f : 1f - Mathf.Clamp01((nearest - contact) / (outer - contact));
        float crowd = Mathf.Clamp01((close - 1) * 0.15f);
        float lives = _health <= 1 ? 0.75f : _health < MaxHealth ? 0.3f : 0f;
        return Mathf.Max(Mathf.Clamp01(proximity * 0.85f + crowd), lives);
    }

    static int DifficultyLevel(double survivalSeconds)
    {
        int level = 0;
        while (level < DifficultyThresholds.Length && survivalSeconds >= DifficultyThresholds[level])
            level++;
        return level;
    }

    static int NextDownbeat(int step) =>
        (step / SoundManager.StepsPerBar + 1) * SoundManager.StepsPerBar;

    void SpawnNextEnemy(int step, bool fever)
    {
        bool hidden = _soundManager.RoundSeconds >= HiddenEnemyStart && _spawnCount % 2 == 0;
        if (fever)
        {
            SpawnFromNewDirection(Random.Range(0, Colors.Length), hidden);
            _spawnCount++;
            _nextSpawnStep = (step / SoundManager.StepsPerBeat + 1) * SoundManager.StepsPerBeat;
            return;
        }

        if (_groupSize == 0 && step % SoundManager.StepsPerBar != 0)
        {
            _nextSpawnStep = NextDownbeat(step);
            return;
        }

        if (_openingEnemiesSpawned < Colors.Length)
        {
            SpawnFromNewDirection(_openingEnemiesSpawned++);
            _nextSpawnStep = NextDownbeat(step);
            return;
        }

        if (_groupSize == 0)
            _groupColor = Random.Range(0, Colors.Length);

        SpawnFromNewDirection(_groupColor, hidden);
        _spawnCount++;
        _groupSize++;

        // Difficulty follows survival time, not tempo, so calmer music sections can still be hard later on.
        // Breath phrases (chosen by the music) are the deliberate rests: single enemies, an extra bar apart.
        int difficulty = DifficultyLevel(_soundManager.RoundSeconds);
        bool breath = _soundManager.IsBreath;
        int maxGroupSize = breath ? 1 : difficulty == 0 ? 2 : 3;
        if (_groupSize < maxGroupSize && Random.value < SameColorChance)
        {
            bool halfBeat = difficulty >= 2 && Random.value < 0.45f;
            _nextSpawnStep = step + (halfBeat ? 1 : SoundManager.StepsPerBeat);
        }
        else
        {
            _groupSize = 0;
            _nextSpawnStep = NextDownbeat(step) + (breath ? SoundManager.StepsPerBar : 0);
        }
    }

    void AdvanceWaves(float deltaTime)
    {
        float tolerance = EnemyHalfSize + WaveHitPadding;
        float maxRadius = _arenaRadius + EnemyHalfSize + WaveHitPadding * 2f;
        float waveSpeed = (maxRadius - CoreRadius) / (PulseBeats * (float)_soundManager.BeatSeconds);

        for (int i = 0; i < _waves.Count;)
        {
            var wave = _waves[i];
            float nextRadius = Mathf.Min(maxRadius, wave.Radius + waveSpeed * deltaTime);
            _contacts.Clear();

            foreach (var enemy in _enemies)
            {
                if (enemy.Distance + tolerance < wave.Radius)
                    continue;

                // Both move during the frame. Solve for when their leading edges meet.
                float contactTime = Mathf.Max(0f, enemy.Distance - tolerance - wave.Radius)
                    / (waveSpeed + EnemySpeed);
                float contactRadius = wave.Radius + waveSpeed * contactTime;
                if (contactRadius <= nextRadius + SimultaneousContactTolerance)
                    _contacts.Add((enemy, contactRadius));
            }

            _contacts.Sort((a, b) => a.Radius.CompareTo(b.Radius));

            bool blocked = false;
            float contactLimit = nextRadius;
            foreach (var contact in _contacts)
            {
                if (contact.Radius > contactLimit)
                    break;

                if (!wave.IsFeverPulse && contact.Enemy.ColorIndex != wave.ColorIndex)
                {
                    if (contact.Enemy.IsHidden)
                    {
                        contact.Enemy.IsHidden = false;
                        ApplyEnemyAppearance(contact.Enemy);
                    }
                    // Resolve the whole contact band before ending the wave.
                    if (!blocked)
                    {
                        blocked = true;
                        contactLimit = contact.Radius + SimultaneousContactTolerance;
                    }
                    continue;
                }

                Shatter(contact.Enemy.Instance.transform.position, Colors[contact.Enemy.ColorIndex]);
                _poolManager.ReturnToPool(contact.Enemy.Instance);
                _enemies.Remove(contact.Enemy);
                // Every kill of this pulse joins the chord of its first kill: one press, one chord.
                int chordStep = _soundManager.QueueKillChord(contact.Enemy.ColorIndex, wave.ChordStep);
                if (wave.ChordStep < 0)
                    wave.ChordStep = chordStep;
                if (!wave.IsFeverPulse && !wave.HasKill)
                {
                    wave.HasKill = true;
                    if (!_soundManager.FeverActive && !_soundManager.FeverPending &&
                        ++_feverCharge == FeverThreshold)
                        _soundManager.QueueFever();
                }
                _hits++;
                _streak++;
                UpdateHud();
            }

            if (blocked || nextRadius >= maxRadius)
            {
                Destroy(wave.Ring.gameObject);
                _waves.RemoveAt(i);
            }
            else
            {
                wave.Radius = nextRadius;
                DrawCircle(wave.Ring, nextRadius);
                i++;
            }
        }
    }

    void SpawnFromNewDirection(int colorIndex, bool hidden = false)
    {
        // Keep consecutive spawns at least 60 degrees apart, including within a color group.
        _spawnAngle = Mathf.Repeat(_spawnAngle + Random.Range(60f, 300f), 360f);
        float radians = _spawnAngle * Mathf.Deg2Rad;
        var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        float distance = _arenaRadius;
        float angle = Vector2.SignedAngle(Vector2.up, -direction);
        var rotation = Quaternion.Euler(0f, 0f, angle);
        var instance = _poolManager.GetFromPool(EnemyPoolKey,
            transform.position + (Vector3)(direction * distance), rotation);
        var enemy = new Enemy
        {
            Instance = instance,
            ColorIndex = colorIndex,
            Distance = distance,
            IsHidden = hidden
        };
        ApplyEnemyAppearance(enemy);
        _enemies.Add(enemy);
    }

    void ApplyEnemyAppearance(Enemy enemy)
    {
        var renderer = enemy.Instance.GetComponent<SpriteRenderer>();
        renderer.sprite = _enemyShapes[enemy.IsHidden ? Colors.Length : enemy.ColorIndex];
        renderer.color = enemy.IsHidden ? HiddenColor : Colors[enemy.ColorIndex];
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
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) * 2f / size - 1f;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) * 2f / size - 1f;
                    float edge = shape switch
                    {
                        0 => Mathf.Min(v + 0.9f, (0.9f - v) * 0.53f - Mathf.Abs(u)),
                        1 => 0.94f - Mathf.Sqrt(u * u + v * v),
                        2 => 0.97f - Mathf.Abs(u) - Mathf.Abs(v),
                        _ => 0.96f - Mathf.Max(Mathf.Abs(u), Mathf.Abs(v))
                    };
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
        bool fever = _soundManager.FeverActive;
        string feverText = fever ? "FEVER!  Press Space" : _soundManager.FeverBuilding
            ? "FEVER INCOMING..."
            : _soundManager.FeverPending
            ? "FEVER READY"
            : $"Fever: {_feverCharge}/{FeverThreshold}";
        gamePanel.SetStatus(fever ? "All colors" : ColorNames[_selectedColor],
            fever ? Color.white : Colors[_selectedColor], _hits, _health, MaxHealth,
            $"{_soundManager.CurrentBpm} BPM{(_soundManager.IsBreath ? "  ~breathe~" : "")}" +
            $"{(_soundManager.FeatureSounding ? "  + " + _soundManager.CurrentFeature.Name : "")}     {feverText}");
    }

    void OnDestroy()
    {
        if (_gameManager != null)
        {
            _gameManager.OnGameRestarted -= StartRound;
            if (_soundManager != null)
                _gameManager.OnGameOver -= _soundManager.StopMusic;
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
