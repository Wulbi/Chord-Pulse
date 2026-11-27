using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class GameOverPanel : BasePanel
{
    public override UIPanelType TypeOfPanel => UIPanelType.GAME_OVER_PANEL;

    [SerializeField] RectTransform popupRect;
    [SerializeField] Button restartButton;

    // The round's result, built in code under the popup (900 x 600: title at y 160, restart button moved down to make room).
    const float RestartButtonY = -170f;
    static readonly Color ResultValueColor = new Color(1f, 0.82f, 0.3f);
    Text _maxCombo;
    Text _score;

    Sequence _effectSeq;

    void Start()
    {
        if (restartButton != null)
            restartButton.onClick.AddListener(OnClickRestart);
    }

    public override void OnEnter(params object[] datas)
    {
        ShowResult();
        base.OnEnter(datas);
    }

    void ShowResult()
    {
        if (popupRect == null || !GameManager.HasInstance)
            return;
        if (_maxCombo == null)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _maxCombo = CreateResultRow("Max Combo", "MAX COMBO", 60f, font);
            _score = CreateResultRow("Score", "SCORE", -10f, font);
            if (restartButton != null)
            {
                var rect = (RectTransform)restartButton.transform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, RestartButtonY);
            }
        }
        _maxCombo.text = GameManager.Instance.ResultMaxCombo.ToString();
        _score.text = GameManager.Instance.ResultScore.ToString();
    }

    // Label on the left half, value on the right half; returns the value text.
    Text CreateResultRow(string rowName, string label, float y, Font font)
    {
        CreateResultText(rowName + " Label", label, font, TextAnchor.MiddleRight, new Vector2(-190f, y), Color.white);
        return CreateResultText(rowName + " Value", "0", font, TextAnchor.MiddleLeft, new Vector2(190f, y), ResultValueColor);
    }

    Text CreateResultText(string textName, string value, Font font, TextAnchor alignment, Vector2 position, Color color)
    {
        var text = new GameObject(textName, typeof(RectTransform)).AddComponent<Text>();
        var rect = text.rectTransform;
        rect.SetParent(popupRect, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(340f, 64f);
        text.font = font;
        text.fontSize = 48;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.text = value;
        return text;
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
