using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] float moveSpeed = 8f;

    Camera _cam;
    float _halfWidth;

    void Awake()
    {
        _cam = Camera.main;
        var spriteRenderer = GetComponent<SpriteRenderer>();
        _halfWidth = spriteRenderer != null
            ? spriteRenderer.bounds.extents.x
            : transform.localScale.x * 0.5f;
    }

    void Update()
    {
        if (GameManager.HasInstance && !GameManager.Instance.IsPlaying)
            return;

        float input = 0f;
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.leftArrowKey.isPressed)
                input -= 1f;
            if (keyboard.rightArrowKey.isPressed)
                input += 1f;
        }

        Vector3 position = transform.position;
        position.x += input * moveSpeed * Time.deltaTime;

        float halfScreenWidth = _cam.orthographicSize * _cam.aspect;
        float minX = _cam.transform.position.x - halfScreenWidth + _halfWidth;
        float maxX = _cam.transform.position.x + halfScreenWidth - _halfWidth;
        position.x = Mathf.Clamp(position.x, minX, maxX);

        transform.position = position;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<FallingBlock>() == null)
            return;

        if (GameManager.HasInstance)
            GameManager.Instance.GameOver();
    }
}
