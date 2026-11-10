using UnityEngine;

/// <summary>
/// 게임 상태 이벤트를 구독하고 UIManager를 통해 패널을 연다.
/// </summary>
public class GameUIPresenter : MonoBehaviour
{
    void OnEnable()
    {
        if (GameManager.HasInstance)
            GameManager.Instance.OnGameOver += HandleGameOver;
    }

    void Start()
    {
        if (!GameManager.HasInstance)
            return;

        GameManager.Instance.OnGameOver -= HandleGameOver;
        GameManager.Instance.OnGameOver += HandleGameOver;
    }

    void OnDisable()
    {
        if (GameManager.HasInstance)
            GameManager.Instance.OnGameOver -= HandleGameOver;
    }

    void HandleGameOver()
    {
        if (UIManager.HasInstance)
            UIManager.Instance.PushPanel(UIPanelType.GAME_OVER_PANEL);
    }
}
