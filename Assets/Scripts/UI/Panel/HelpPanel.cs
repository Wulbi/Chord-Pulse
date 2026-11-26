using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Small controls card shown before the first round. Space (or Enter / click) closes it and starts the game.
/// Built in code, so it needs no scene object: <see cref="Create"/> makes it under the UI canvas.
/// </summary>
public sealed class HelpPanel : BasePanel
{
    public override UIPanelType TypeOfPanel => UIPanelType.HELP_PANEL;

    static readonly (string Key, string Action)[] Controls =
    {
        ("A", "Change Pulse Color"),
        ("Space", "Shoot Pulse")
    };

    RectTransform _card;
    Text _prompt;
    Action _onClosed;
    Tween _openTween;
    bool _closing;

    public static HelpPanel Create(Transform canvas)
    {
        var root = new GameObject("Help Panel", typeof(RectTransform));
        var rect = (RectTransform)root.transform;
        rect.SetParent(canvas, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var panel = root.AddComponent<HelpPanel>();
        panel.Build();
        root.SetActive(false);
        return panel;
    }

    /// <summary>Shows the card; <paramref name="onClosed"/> runs once the player dismisses it.</summary>
    public void Show(Action onClosed)
    {
        _onClosed = onClosed;
        UIManager.Instance.PushPanel(TypeOfPanel);
    }

    public override void OnEnter(params object[] datas)
    {
        _closing = false;
        base.OnEnter(datas);
    }

    public override void OnOpenEffect()
    {
        _openTween?.Kill();
        _card.localScale = Vector3.one * 0.001f;
        _openTween = _card.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(gameObject);
    }

    public override void OnExit()
    {
        _openTween?.Kill();
        base.OnExit();
    }

    void Update()
    {
        _prompt.color = new Color(1f, 1f, 1f, 0.45f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f));
        if (_closing)
            return;

        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        bool pressed = (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
            || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        if (!pressed)
            return;

        _closing = true;
        OnClose();
        var onClosed = _onClosed;
        _onClosed = null;
        onClosed?.Invoke();
    }

    void Build()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var dim = gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);

        _card = new GameObject("Card", typeof(RectTransform)).GetComponent<RectTransform>();
        _card.SetParent(transform, false);
        _card.sizeDelta = new Vector2(560f, 330f);
        var card = _card.gameObject.AddComponent<Image>();
        card.color = new Color(0.07f, 0.1f, 0.16f, 0.95f);
        var outline = _card.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.4f, 0.55f, 0.8f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);

        CreateText("Title", "HOW TO PLAY", font, 40, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0f, 110f), new Vector2(520f, 56f), Color.white);
        for (int i = 0; i < Controls.Length; i++)
        {
            float y = 30f - i * 60f;
            CreateText("Key", Controls[i].Key, font, 32, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2(-110f, y), new Vector2(160f, 48f), new Color(1f, 0.82f, 0.3f));
            CreateText("Action", Controls[i].Action, font, 30, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(150f, y), new Vector2(320f, 48f), Color.white);
        }
        _prompt = CreateText("Prompt", "Press Space to start", font, 26, FontStyle.Italic, TextAnchor.MiddleCenter,
            new Vector2(0f, -120f), new Vector2(520f, 40f), Color.white);
    }

    Text CreateText(string textName, string value, Font font, int size, FontStyle style, TextAnchor alignment,
        Vector2 position, Vector2 boxSize, Color color)
    {
        var text = new GameObject(textName, typeof(RectTransform)).AddComponent<Text>();
        var rect = text.rectTransform;
        rect.SetParent(_card, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = boxSize;
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }
}
