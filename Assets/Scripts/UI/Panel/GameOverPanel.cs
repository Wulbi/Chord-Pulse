using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class GameOverPanel : BasePanel
{
    public override UIPanelType TypeOfPanel => UIPanelType.GAME_OVER_PANEL;

    [SerializeField] RectTransform popupRect;
    [SerializeField] Button restartButton;

    Sequence _effectSeq;

    void Start()
    {
        if (restartButton != null)
            restartButton.onClick.AddListener(OnClickRestart);
    }

    public override void OnEnter(params object[] datas)
    {
        base.OnEnter(datas);
    }

    public override void OnOpenEffect()
    {
        base.OnOpenEffect();

        if (popupRect == null)
            return;

        _effectSeq?.Kill();
        popupRect.localScale = Vector3.one * 0.001f;
        _effectSeq = DOTween.Sequence();
        _effectSeq.Append(popupRect.DOScale(1f, 0.25f).SetEase(Ease.OutBack));
        _effectSeq.SetUpdate(true);
        _effectSeq.SetLink(gameObject);
        _effectSeq.OnComplete(() => _effectSeq = null);
    }

    public override void OnExit()
    {
        _effectSeq?.Kill();
        _effectSeq = null;
        base.OnExit();
    }

    void OnDisable()
    {
        if (_effectSeq == null)
            return;

        _effectSeq.Kill();
        _effectSeq = null;
    }

    void OnClickRestart()
    {
        if (GameManager.HasInstance)
            GameManager.Instance.Restart();
    }
}
