using UnityEngine;
using UnityEngine.UI;

public sealed class GamePanel : BasePanel
{
    public override UIPanelType TypeOfPanel => UIPanelType.GAME_PANEL;
    public bool IsPlaying { get; private set; }

    Text _hearts;
    Text _bpm;
    Text _score;
    ComboCounter _comboCounter;

    public override void OnEnter(params object[] datas)
    {
        base.OnEnter(datas);
        IsPlaying = true;
    }

    public override void OnPause() => IsPlaying = false;
    public override void OnResume() => IsPlaying = true;

    public override void OnExit()
    {
        IsPlaying = false;
        base.OnExit();
    }

    const string HeartFull = "#ff4d5e";
    const string HeartLost = "#ffffff33";

    public void SetStatus(int health, int maxHealth, int bpm, int pulseCombo, int score)
    {
        if (_hearts == null)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _hearts = CreateLabel("Core HP", "", 16, 72, font);
            _hearts.supportRichText = true;
            _bpm = CreateLabel("BPM", "", 108, 26, font);
            _comboCounter = new GameObject("Combo Counter", typeof(RectTransform)).AddComponent<ComboCounter>();
            _comboCounter.transform.SetParent(transform, false);
            _comboCounter.Init(font);
            // Score sits under the combo counter on the right edge.
            _score = CreateLabel("Score", "", 0, 34, font);
            var scoreRect = _score.rectTransform;
            scoreRect.anchorMin = scoreRect.anchorMax = scoreRect.pivot = new Vector2(1f, 0.5f);
            scoreRect.anchoredPosition = new Vector2(-70f, -120f);
            scoreRect.sizeDelta = new Vector2(320f, 44f);
            _score.alignment = TextAnchor.MiddleRight;
            _score.fontStyle = FontStyle.Bold;
        }

        // Core HP as hearts: red for each one left, faded for each one lost.
        var hearts = new System.Text.StringBuilder();
        for (int i = 0; i < maxHealth; i++)
            hearts.Append($"<color={(i < health ? HeartFull : HeartLost)}>♥</color>");
        _hearts.text = hearts.ToString();
        _bpm.text = $"{bpm} BPM";
        _comboCounter.Set(pulseCombo);
        _score.text = $"SCORE  {score}";
    }

    Text CreateLabel(string labelName, string value, float top, int size, Font font)
    {
        var label = new GameObject(labelName, typeof(RectTransform)).AddComponent<Text>();
        var rect = label.rectTransform;
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -top);
        rect.sizeDelta = new Vector2(660f, size * 1.3f);
        label.font = font;
        label.fontSize = size;
        label.color = Color.white;
        label.raycastTarget = false;
        label.text = value;
        return label;
    }
}
