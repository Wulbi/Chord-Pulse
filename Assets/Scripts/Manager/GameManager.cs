using System;

/// <summary>
/// 게임 상태(진행 중 / 게임 오버)와 판의 결과를 관리하는 싱글톤 Manager.
/// 게임 오버는 이벤트로 알리고, UI는 구독 측에서 처리한다.
/// </summary>
public class GameManager : SingletonBehaviour<GameManager>
{
    public event Action OnGameOver;
    /// <summary>
    /// The game-over screen may show now. Fires right after <see cref="OnGameOver"/> unless a handler called
    /// <see cref="DelayGameOverPresentation"/> to play its own sequence first; that handler then calls
    /// <see cref="PresentGameOver"/> when it is done.
    /// </summary>
    public event Action OnGameOverPresented;
    public event Action OnGameRestarted;

    public bool IsPlaying { get; private set; } = true;
    public bool IsGameOver { get; private set; }
    /// <summary>The finished round's score and highest combo, for the game-over screen (set by the game at game over).</summary>
    public int ResultScore { get; private set; }
    public int ResultMaxCombo { get; private set; }

    public void SetRoundResult(int score, int maxCombo)
    {
        ResultScore = score;
        ResultMaxCombo = maxCombo;
    }

    bool _presentationDelayed;
    bool _presented;

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

        IsGameOver = false;
        IsPlaying = true;
        OnGameRestarted?.Invoke();
    }
}
