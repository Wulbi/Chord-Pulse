using System;
using UnityEngine;

/// <summary>
/// 점수와 게임 상태를 관리하는 싱글톤 Manager.
/// 게임 오버는 이벤트로 알리고, UI는 구독 측에서 처리한다.
/// </summary>
public class GameManager : SingletonBehaviour<GameManager>
{
    [SerializeField] float scorePerSecond = 10f;

    public event Action OnGameOver;
    public event Action<int> OnScoreChanged;
    public event Action OnGameRestarted;

    public bool IsPlaying { get; private set; } = true;
    public bool IsGameOver { get; private set; }
    public int Score { get; private set; }

    float _score;

    protected override void Awake()
    {
        base.Awake();
        ResetState(notify: false);
    }

    void Update()
    {
        if (!IsPlaying || IsGameOver)
            return;

        _score += scorePerSecond * Time.deltaTime;
        var next = Mathf.FloorToInt(_score);
        if (next == Score)
            return;

        Score = next;
        OnScoreChanged?.Invoke(Score);
    }

    public void GameOver()
    {
        if (IsGameOver)
            return;

        IsGameOver = true;
        IsPlaying = false;
        OnGameOver?.Invoke();
    }

    public void Restart()
    {
        if (UIManager.HasInstance)
            UIManager.Instance.CloseAll();

        if (PoolManager.HasInstance)
            PoolManager.Instance.ReturnAll(PoolManager.FallingBlockKey);

        ResetState(notify: true);
        OnGameRestarted?.Invoke();
    }

    void ResetState(bool notify)
    {
        IsGameOver = false;
        IsPlaying = true;
        _score = 0f;
        Score = 0;

        if (notify)
            OnScoreChanged?.Invoke(Score);
    }
}
