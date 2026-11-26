using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Big pulse-combo number on the right edge. Each +1 rolls the old number up and out while the new one rises in
/// with a punch; a break drops the number down in red. The colour warms toward gold as the combo grows.
/// </summary>
public sealed class ComboCounter : MonoBehaviour
{
    const float RollDistance = 70f;
    const float RollSeconds = 0.22f;
    const float PunchScale = 1.35f;
    const float BreakSeconds = 0.35f;
    // Combo at which the number is fully gold.
    const float GoldCombo = 30f;
    static readonly Color Cold = new Color(0.85f, 0.9f, 1f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.3f);
    static readonly Color Broken = new Color(1f, 0.32f, 0.35f);

    Text _current;
    Text _outgoing;
    Text _caption;
    int _shown = -1;
    float _rollTime = 1f;
    float _breakTime = 1f;

    public void Init(Font font)
    {
        var rect = (RectTransform)transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-70f, 40f);
        rect.sizeDelta = new Vector2(320f, 160f);

        _outgoing = CreateText("Combo Outgoing", font, 140, Vector2.zero);
        _current = CreateText("Combo Value", font, 140, Vector2.zero);
        _caption = CreateText("Combo Caption", font, 34, new Vector2(0f, -100f));
        _outgoing.color = Color.clear;
    }

    /// <summary>Shows <paramref name="combo"/>; animates when it changes.</summary>
    public void Set(int combo)
    {
        _caption.text = "COMBO";
        if (combo == _shown)
            return;

        if (_shown >= 0)
        {
            _outgoing.text = _shown.ToString();
            if (combo > _shown)
                _rollTime = 0f;
            else
                _breakTime = 0f;
        }
        _shown = combo;
        _current.text = combo.ToString();
    }

    void Update()
    {
        if (_current == null)
            return;

        // Warms toward gold as the combo grows (fever has its own gauge; this is just the streak's size).
        var progressColor = Color.Lerp(Cold, Gold, Mathf.Clamp01(_shown / GoldCombo));

        var current = _current.rectTransform;
        var outgoing = _outgoing.rectTransform;
        if (_rollTime < 1f)
        {
            // +1: the old number rolls up and fades, the new one rises in from below and punches.
            _rollTime = Mathf.Min(1f, _rollTime + Time.unscaledDeltaTime / RollSeconds);
            float t = 1f - (1f - _rollTime) * (1f - _rollTime);
            outgoing.anchoredPosition = new Vector2(0f, RollDistance * t);
            _outgoing.color = Fade(progressColor, 1f - t);
            current.anchoredPosition = new Vector2(0f, -RollDistance * (1f - t));
            current.localScale = Vector3.one * Mathf.Lerp(PunchScale, 1f, t);
            _current.color = Fade(Color.Lerp(Color.white, progressColor, t), t);
        }
        else if (_breakTime < 1f)
        {
            // Break: the old number falls away in red and the reset value fades in.
            _breakTime = Mathf.Min(1f, _breakTime + Time.unscaledDeltaTime / BreakSeconds);
            float t = _breakTime;
            outgoing.anchoredPosition = new Vector2(Mathf.Sin(t * 60f) * 10f * (1f - t), -RollDistance * t * t);
            _outgoing.color = Fade(Broken, 1f - t);
            current.anchoredPosition = Vector2.zero;
            current.localScale = Vector3.one;
            _current.color = Fade(progressColor, t);
        }
        else
        {
            _outgoing.color = Color.clear;
            current.anchoredPosition = Vector2.zero;
            current.localScale = Vector3.one;
            _current.color = progressColor;
        }
        _caption.color = Fade(progressColor, 0.7f);
    }

    static Color Fade(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    Text CreateText(string textName, Font font, int size, Vector2 position)
    {
        var text = new GameObject(textName, typeof(RectTransform)).AddComponent<Text>();
        var rect = text.rectTransform;
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(320f, size * 1.3f);
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleRight;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
