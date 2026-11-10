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
    static readonly Color KickAccent = new Color(0.59f, 0.73f, 0.90f);
    static readonly Color SnareAccent = new Color(0.75f, 0.84f, 0.94f);

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
    const float WaveSpeed = 5f;
    const float ArenaInset = 0.45f;
    const float SameColorChance = 0.8f;
    const int MaxGroupSize = 3;

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
        public bool ChordScheduled;
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
    ParticleSystem _shards;
    Mesh _shardMesh;
    float _arenaRadius;
    int _selectedColor;
    int _spawnCount;
    int _groupColor;
    int _groupSize;
    float _spawnAngle;
    int _hits;
    int _health;
    int _nextSpawnBeat;

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
        _groupSize = 0;
        _spawnAngle = Random.Range(0f, 360f);
        _hits = 0;
        _health = MaxHealth;
        _nextSpawnBeat = 0;
        _soundManager.StartMusic();
        _core.startColor = _core.endColor = Colors[0];
        UIManager.Instance.PushPanel(UIPanelType.GAME_PANEL);

        UpdateHud();
    }

    void SpawnOpeningEnemies()
    {
        // One opening shot can shatter all three red enemies before meeting the blue blocker.
        float openingGap = Mathf.Min(0.25f, 0.46f / _arenaRadius);
        Spawn(Vector2.right, 0, 1f - 3f * openingGap);
        Spawn(Vector2.up, 0, 1f - 2f * openingGap);
        Spawn(Vector2.left, 0, 1f - openingGap);
        Spawn(new Vector2(1f, 1f).normalized, 1);
        Spawn(Vector2.down, 2);
    }

    void Update()
    {
        if (!gamePanel.IsPlaying || !_gameManager.IsPlaying)
            return;

        double beatPosition = _soundManager.BeatPosition;
        int beat = _soundManager.CurrentBeat;
        AnimateArena((float)beatPosition, beat);
        if (_nextSpawnBeat == 0 && beat >= 0)
        {
            SpawnOpeningEnemies();
            _nextSpawnBeat = SoundManager.BeatsPerBar;
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
                Ring = CreateRing("Pulse", Colors[_selectedColor], CoreRadius, WaveWidth),
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

        if (beat >= _nextSpawnBeat)
        {
            // Keep a new group on a downbeat even if a frame skipped its original slot.
            if (_groupSize == 0 && beat % SoundManager.BeatsPerBar != 0)
                _nextSpawnBeat = NextDownbeat(beat);
            else
                SpawnNextEnemy(beat);
        }
    }

    static int NextDownbeat(int beat)
    {
        return (beat / SoundManager.BeatsPerBar + 1) * SoundManager.BeatsPerBar;
    }

    void SpawnNextEnemy(int beat)
    {
        if (_groupSize == 0)
            _groupColor = Random.Range(0, Colors.Length);

        // Keep consecutive spawns at least 60 degrees apart, including within a color group.
        _spawnAngle = Mathf.Repeat(_spawnAngle + Random.Range(60f, 300f), 360f);
        float radians = _spawnAngle * Mathf.Deg2Rad;
        var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        bool hidden = beat * SoundManager.SecondsPerBeat >= HiddenEnemyStart && _spawnCount % 2 == 0;
        Spawn(direction, _groupColor, hidden: hidden);
        _spawnCount++;
        _groupSize++;

        if (_groupSize < MaxGroupSize && Random.value < SameColorChance)
        {
            _nextSpawnBeat = beat + 1;
        }
        else
        {
            _groupSize = 0;
            _nextSpawnBeat = NextDownbeat(beat);
        }
    }

    void AdvanceWaves(float deltaTime)
    {
        float tolerance = EnemyHalfSize + WaveHitPadding;
        float maxRadius = _arenaRadius + EnemyHalfSize + WaveHitPadding * 2f;

        for (int i = 0; i < _waves.Count;)
        {
            var wave = _waves[i];
            float nextRadius = Mathf.Min(maxRadius, wave.Radius + WaveSpeed * deltaTime);
            _contacts.Clear();

            foreach (var enemy in _enemies)
            {
                if (enemy.Distance + tolerance < wave.Radius)
                    continue;

                // Both move during the frame. Solve for when their leading edges meet.
                float contactTime = Mathf.Max(0f, enemy.Distance - tolerance - wave.Radius)
                    / (WaveSpeed + EnemySpeed);
                float contactRadius = wave.Radius + WaveSpeed * contactTime;
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

                if (contact.Enemy.ColorIndex != wave.ColorIndex)
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
                if (!wave.ChordScheduled)
                {
                    _soundManager.ScheduleChord(wave.ColorIndex);
                    wave.ChordScheduled = true;
                }
                _hits++;
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

    void Spawn(Vector2 direction, int colorIndex, float edgeFraction = 1f, bool hidden = false)
    {
        float distance = _arenaRadius * edgeFraction;
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

    void AnimateArena(float beatPosition, int beat)
    {
        bool kick = beat >= 0 && SoundManager.IsKickBeat(beat);
        float decay = kick ? 12f : 30f;
        float impact = beat >= 0 ? Mathf.Exp(-decay * Mathf.Max(0f, beatPosition - beat)) : 0f;
        float scale = 1f + (kick ? 0.025f * impact : 0f);
        _boundary.transform.localScale = new Vector3(scale, scale, 1f);
        _boundary.startWidth = _boundary.endWidth = 0.025f + (kick ? 0.025f : 0.012f) * impact;
        _boundary.startColor = _boundary.endColor =
            Color.Lerp(BoundaryColor, kick ? KickAccent : SnareAccent, impact);
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
        gamePanel.SetStatus(ColorNames[_selectedColor], Colors[_selectedColor], _hits, _health, MaxHealth);
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
