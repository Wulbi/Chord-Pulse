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
    /// <summary>
    /// The game-over screen may show now. Fires right after <see cref="OnGameOver"/> unless a handler called
    /// <see cref="DelayGameOverPresentation"/> to play its own sequence first; that handler then calls
    /// <see cref="PresentGameOver"/> when it is done.
    /// </summary>
    public event Action OnGameOverPresented;
    public event Action<int> OnScoreChanged;
    public event Action OnGameRestarted;

    public bool IsPlaying { get; private set; } = true;
    public bool IsGameOver { get; private set; }
    public int Score { get; private set; }
    /// <summary>The finished round's score and highest combo, for the game-over screen (set by the game at game over).</summary>
    public int ResultScore { get; private set; }
    public int ResultMaxCombo { get; private set; }

    public void SetRoundResult(int score, int maxCombo)
    {
        ResultScore = score;
        ResultMaxCombo = maxCombo;
    }

    float _score;
    bool _presentationDelayed;
    bool _presented;

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
        _presentationDelayed = false;
        _presented = false;
        OnGameOver?.Invoke();
        if (!_presentationDelayed)
            PresentGameOver();
    }

    /// <summary>Call from an <see cref="OnGameOver"/> handler to hold the game-over screen until <see cref="PresentGameOver"/>.</summary>
    public void DelayGameOverPresentation() => _presentationDelayed = true;

    /// <summary>Shows the game-over screen (once per game over).</summary>
    public void PresentGameOver()
    {
        if (!IsGameOver || _presented)
            return;
        _presented = true;
        OnGameOverPresented?.Invoke();
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
