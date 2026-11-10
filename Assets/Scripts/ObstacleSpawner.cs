using UnityEngine;

public class ObstacleSpawner : MonoBehaviour
{
    [SerializeField] float spawnInterval = 1f;
    [SerializeField] float spawnHalfWidthFallback = 0.5f;

    Camera _cam;
    float _timer;

    void Awake()
    {
        _cam = Camera.main;
    }

    void OnEnable()
    {
        if (GameManager.HasInstance)
            GameManager.Instance.OnGameRestarted += HandleRestarted;
    }

    void Start()
    {
        if (!GameManager.HasInstance)
            return;

        GameManager.Instance.OnGameRestarted -= HandleRestarted;
        GameManager.Instance.OnGameRestarted += HandleRestarted;
    }

    void OnDisable()
    {
        if (GameManager.HasInstance)
            GameManager.Instance.OnGameRestarted -= HandleRestarted;
    }

    void Update()
    {
        if (GameManager.HasInstance && !GameManager.Instance.IsPlaying)
            return;

        _timer += Time.deltaTime;
        if (_timer < spawnInterval)
            return;

        _timer = 0f;
        Spawn();
    }

    void HandleRestarted()
    {
        _timer = 0f;
    }

    void Spawn()
    {
        if (!PoolManager.HasInstance)
            return;

        if (_cam == null)
            _cam = Camera.main;

        float halfWidth = spawnHalfWidthFallback;
        float halfScreenWidth = _cam.orthographicSize * _cam.aspect;
        float minX = _cam.transform.position.x - halfScreenWidth + halfWidth;
        float maxX = _cam.transform.position.x + halfScreenWidth - halfWidth;
        float x = Random.Range(minX, maxX);
        float y = _cam.transform.position.y + _cam.orthographicSize + halfWidth;

        PoolManager.Instance.GetFromPool(
            PoolManager.FallingBlockKey,
            new Vector3(x, y, 0f),
            Quaternion.identity);
    }
}
