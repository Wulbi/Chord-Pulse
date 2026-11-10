using System.Collections.Generic;
using GameLogic.Core;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class ChordPulsePrototype : MonoBehaviour
{
    const string EnemyPoolKey = "ChordPulseEnemy";
    const float CoreRadius = 0.42f;
    const float EnemyHalfSize = 0.18f;
    const float WaveWidth = 0.055f;
    const float SimultaneousContactTolerance = 0.01f;
    const int CircleSegments = 96;

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
    const float GroupSpawnInterval = 0.7f;
    const float BetweenGroupsInterval = 1.9f;
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
        public int HitCount;
    }

    readonly List<Enemy> _enemies = new List<Enemy>();
    readonly List<(Enemy Enemy, float Radius)> _contacts = new List<(Enemy, float)>();
    readonly List<Wave> _waves = new List<Wave>();

    PoolManager _poolManager;
    GameManager _gameManager;
    SoundManager _soundManager;
    GameObject _enemyTemplate;
    Material _ringMaterial;
    Sprite _square;
    LineRenderer _core;
    float _arenaRadius;
    int _selectedColor;
    int _spawnCount;
    int _groupColor;
    int _groupSize;
    Vector2 _groupDirection;
    int _hits;
    int _health;
    float _elapsed;
    float _spawnTimer;

    void Awake()
    {
        _gameManager = GameManager.Instance;
        _soundManager = SoundManager.Instance;
        _gameManager.OnGameRestarted += StartRound;
    }

    void Start()
    {
        _ringMaterial = new Material(Shader.Find("Sprites/Default"));
        _core = CreateRing("Central Core", Colors[0], CoreRadius, 0.12f);
        _arenaRadius = playCamera.orthographicSize * Mathf.Min(1f, playCamera.aspect) - ArenaInset;
        var boundary = CreateRing("Arena Boundary", new Color(0.27f, 0.34f, 0.46f), _arenaRadius, 0.025f);
        boundary.sortingOrder = -1;

        var texture = Texture2D.whiteTexture;
        _square = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), texture.width);
        _enemyTemplate = new GameObject("Chord Pulse Enemy Template");
        _enemyTemplate.transform.SetParent(transform, false);
        _enemyTemplate.SetActive(false);
        _enemyTemplate.transform.localScale = Vector3.one * (EnemyHalfSize * 2f);
        _enemyTemplate.AddComponent<SpriteRenderer>().sprite = _square;
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

        _selectedColor = 0;
        _spawnCount = 0;
        _groupSize = 0;
        _hits = 0;
        _health = MaxHealth;
        _elapsed = 0f;
        _spawnTimer = BetweenGroupsInterval;
        _core.startColor = _core.endColor = Colors[0];
        UIManager.Instance.PushPanel(UIPanelType.GAME_PANEL);

        // One opening shot plays the three notes before meeting the blue blocker.
        float openingGap = Mathf.Min(0.25f, 0.46f / _arenaRadius);
        Spawn(Vector2.right, 0, 1f - 3f * openingGap);
        Spawn(Vector2.right, 0, 1f - 2f * openingGap);
        Spawn(Vector2.right, 0, 1f - openingGap);
        Spawn(Vector2.right, 1);
        Spawn(Vector2.down, 2);
        UpdateHud();
    }

    void Update()
    {
        if (!gamePanel.IsPlaying || !_gameManager.IsPlaying)
            return;

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
        _elapsed += deltaTime;
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

        _spawnTimer -= deltaTime;
        if (_spawnTimer <= 0f)
            SpawnNextEnemy();
    }

    void SpawnNextEnemy()
    {
        if (_groupSize == 0)
        {
            float angle = Random.Range(0f, 2f * Mathf.PI);
            _groupDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            _groupColor = Random.Range(0, Colors.Length);
        }

        bool hidden = _elapsed >= HiddenEnemyStart && _spawnCount % 2 == 0;
        Spawn(_groupDirection, _groupColor, hidden: hidden);
        _spawnCount++;
        _groupSize++;

        if (_groupSize < MaxGroupSize && Random.value < SameColorChance)
        {
            _spawnTimer += GroupSpawnInterval;
        }
        else
        {
            _groupSize = 0;
            _spawnTimer += BetweenGroupsInterval;
        }
    }

    void AdvanceWaves(float deltaTime)
    {
        float maxRadius = _arenaRadius + EnemyHalfSize + WaveWidth;
        float tolerance = EnemyHalfSize + WaveWidth * 0.5f;

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
                        contact.Enemy.Instance.GetComponent<SpriteRenderer>().color = Colors[contact.Enemy.ColorIndex];
                    }
                    // Resolve the whole contact band before ending the wave.
                    if (!blocked)
                    {
                        blocked = true;
                        contactLimit = contact.Radius + SimultaneousContactTolerance;
                    }
                    continue;
                }

                _poolManager.ReturnToPool(contact.Enemy.Instance);
                _enemies.Remove(contact.Enemy);
                _soundManager.PlayChordTone(wave.ColorIndex + wave.HitCount);
                wave.HitCount++;
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
        var instance = _poolManager.GetFromPool(EnemyPoolKey,
            transform.position + (Vector3)(direction * distance), Quaternion.identity);
        instance.GetComponent<SpriteRenderer>().color = hidden ? HiddenColor : Colors[colorIndex];
        _enemies.Add(new Enemy
        {
            Instance = instance,
            ColorIndex = colorIndex,
            Distance = distance,
            IsHidden = hidden
        });
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

    void UpdateHud()
    {
        gamePanel.SetStatus(ColorNames[_selectedColor], Colors[_selectedColor], _hits, _health, MaxHealth);
    }

    void OnDestroy()
    {
        if (_gameManager != null)
            _gameManager.OnGameRestarted -= StartRound;
        if (_enemyTemplate != null)
            Pool.GetPoolByPrefab(_enemyTemplate, false)?.Clear(true);
        Destroy(_square);
        Destroy(_ringMaterial);
    }
}
