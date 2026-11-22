using UnityEngine;
using UnityEngine.UI;

public sealed class GamePanel : BasePanel
{
    public override UIPanelType TypeOfPanel => UIPanelType.GAME_PANEL;
    public bool IsPlaying { get; private set; }

    Text _status;
    Text _musicStatus;

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

    public void SetStatus(string pulseColor, Color color, int hits, int health, int maxHealth, string musicStatus)
    {
        if (_status == null)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            CreateLabel("Controls", "A  Change color     Space  Fire", 24, font);
            _status = CreateLabel("Pulse status", "", 64, font);
            _musicStatus = CreateLabel("Music status", "", 104, font);
        }

        _status.text = $"Pulse: {pulseColor}     Hits: {hits}     Core HP: {health}/{maxHealth}";
        _status.color = color;
        _musicStatus.text = musicStatus;
    }

    Text CreateLabel(string labelName, string value, float top, Font font)
    {
        var label = new GameObject(labelName, typeof(RectTransform)).AddComponent<Text>();
        var rect = label.rectTransform;
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -top);
        rect.sizeDelta = new Vector2(660f, 40f);
        label.font = font;
        label.fontSize = 30;
        label.color = Color.white;
        label.raycastTarget = false;
        label.text = value;
        return label;
    }
}
