using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameManager 점수 이벤트를 구독해 HUD를 갱신한다.
/// </summary>
public class ScoreUI : MonoBehaviour
{
    [SerializeField] Text scoreText;

    void OnEnable()
    {
        if (GameManager.HasInstance)
        {
            GameManager.Instance.OnScoreChanged += HandleScoreChanged;
            HandleScoreChanged(GameManager.Instance.Score);
        }
    }

    void Start()
    {
        if (!GameManager.HasInstance)
            return;

        GameManager.Instance.OnScoreChanged -= HandleScoreChanged;
        GameManager.Instance.OnScoreChanged += HandleScoreChanged;
        HandleScoreChanged(GameManager.Instance.Score);
    }

    void OnDisable()
    {
        if (GameManager.HasInstance)
            GameManager.Instance.OnScoreChanged -= HandleScoreChanged;
    }

    void HandleScoreChanged(int score)
    {
        if (scoreText != null)
            scoreText.text = "Score: " + score;
    }
}
