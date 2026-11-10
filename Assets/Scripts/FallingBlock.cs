using GameLogic.Core;
using UnityEngine;

public class FallingBlock : MonoBehaviour, IPoolable
{
    [SerializeField] float fallSpeed = 4f;

    Camera _cam;
    float _halfHeight;

    void Awake()
    {
        CacheBounds();
    }

    public void OnReuse()
    {
        CacheBounds();
    }

    public void OnRelease()
    {
    }

    void Update()
    {
        if (GameManager.HasInstance && !GameManager.Instance.IsPlaying)
            return;

        if (_cam == null)
            _cam = Camera.main;

        transform.position += Vector3.down * fallSpeed * Time.deltaTime;

        float screenBottom = _cam.transform.position.y - _cam.orthographicSize;
        if (transform.position.y + _halfHeight < screenBottom)
            ReturnToPool();
    }

    void CacheBounds()
    {
        _cam = Camera.main;
        var spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            _halfHeight = spriteRenderer.sprite.bounds.extents.y * Mathf.Abs(transform.lossyScale.y);
            return;
        }

        _halfHeight = Mathf.Abs(transform.localScale.y) * 0.5f;
    }

    void ReturnToPool()
    {
        if (PoolManager.HasInstance)
            PoolManager.Instance.ReturnToPool(gameObject);
        else
            gameObject.Release();
    }
}
