using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class GamePanel : BasePanel
{
    public override UIPanelType TypeOfPanel => UIPanelType.GAME_PANEL;
    public bool IsPlaying { get; private set; }

    // Core HP as heart images. (A "♥" text glyph rendered only through the OS font fallback - nothing in WebGL.)
    RectTransform _heartRow;
    readonly System.Collections.Generic.List<Image> _hearts = new System.Collections.Generic.List<Image>();
    static Sprite s_heartSprite;
    Text _bpm;
    Text _score;
    ComboCounter _comboCounter;
    Button _settingsButton;

    public override void OnEnter(params object[] datas)
    {
        base.OnEnter(datas);
        IsPlaying = true;
        if (_settingsButton == null)
            _settingsButton = CreateSettingsButton();
        // The volumes the player set in the settings panel on an earlier run.
        if (SoundManager.HasInstance)
            VolumeSettings.Apply(SoundManager.Instance);
    }

    public override void OnPause() => IsPlaying = false;
    public override void OnResume() => IsPlaying = true;

    public override void OnExit()
    {
        IsPlaying = false;
        base.OnExit();
    }

    static readonly Color HeartFull = new Color32(0xff, 0x4d, 0x5e, 0xff);
    static readonly Color HeartLost = new Color(1f, 1f, 1f, 0.2f);
    const float HeartSize = 64f;
    const float HeartGap = 10f;

    public void SetStatus(int health, int maxHealth, int bpm, int pulseCombo, int score)
    {
        if (_heartRow == null)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _heartRow = new GameObject("Core HP", typeof(RectTransform)).GetComponent<RectTransform>();
            _heartRow.SetParent(transform, false);
            _heartRow.anchorMin = _heartRow.anchorMax = _heartRow.pivot = new Vector2(0f, 1f);
            _heartRow.anchoredPosition = new Vector2(24f, -20f);
            _heartRow.sizeDelta = new Vector2(660f, HeartSize);
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
        while (_hearts.Count < maxHealth)
            _hearts.Add(CreateHeart(_hearts.Count));
        for (int i = 0; i < _hearts.Count; i++)
        {
            _hearts[i].gameObject.SetActive(i < maxHealth);
            _hearts[i].color = i < health ? HeartFull : HeartLost;
        }
        _bpm.text = $"{bpm} BPM";
        _comboCounter.Set(pulseCombo);
        _score.text = $"SCORE  {score}";
    }

    Image CreateHeart(int index)
    {
        var image = new GameObject("Heart " + index, typeof(RectTransform)).AddComponent<Image>();
        var rect = image.rectTransform;
        rect.SetParent(_heartRow, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(index * (HeartSize + HeartGap), 0f);
        rect.sizeDelta = new Vector2(HeartSize, HeartSize);
        if (s_heartSprite == null)
            s_heartSprite = CreateHeartSprite();
        image.sprite = s_heartSprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    // A heart from the implicit curve (x^2 + y^2 - 1)^3 - x^2 y^3 <= 0, drawn into a texture with 4x4 samples per
    // pixel for smooth edges - the same way as the gear, so it looks identical on every platform.
    static Sprite CreateHeartSprite()
    {
        const int size = 96;
        const int samples = 4;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Heart",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int inside = 0;
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        // Map the pixel into the curve's box: x in [-1.25, 1.25], y in [-1.15, 1.35].
                        float px = ((x + (sx + 0.5f) / samples) / size) * 2.5f - 1.25f;
                        float py = ((y + (sy + 0.5f) / samples) / size) * 2.5f - 1.15f;
                        float a = px * px + py * py - 1f;
                        if (a * a * a - px * px * py * py * py <= 0f)
                            inside++;
                    }
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * inside / (samples * samples)));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
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

    // Gear in the top-right corner (the HUD uses the top-left corner and the middle of the right edge).
    Button CreateSettingsButton()
    {
        var image = new GameObject("Settings Button", typeof(RectTransform)).AddComponent<Image>();
        var rect = image.rectTransform;
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-24f, -20f);
        rect.sizeDelta = new Vector2(60f, 60f);
        image.sprite = CreateGearSprite();

        var button = image.gameObject.AddComponent<Button>();
        // Not reachable by keyboard: Space and Enter belong to the game.
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.75f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(1f, 0.82f, 0.3f, 1f);
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
        button.onClick.AddListener(OpenSettings);
        return button;
    }

    void OpenSettings()
    {
        // A click leaves the button selected, and Space (the fire key) would then press it again.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
        // Only during play: not under the game-over screen or its collapse sequence.
        if (!IsPlaying || !GameManager.HasInstance || !GameManager.Instance.IsPlaying)
            return;
        SettingPanel.Open();
    }

    // An 8-tooth gear with a round hole, drawn into a texture (4x4 samples per pixel for smooth edges).
    static Sprite CreateGearSprite()
    {
        const int size = 96;
        const int samples = 4;
        const float outer = 0.96f, root = 0.72f, hole = 0.32f;
        const int teeth = 8;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Gear",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int inside = 0;
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        float px = ((x + (sx + 0.5f) / samples) / size) * 2f - 1f;
                        float py = ((y + (sy + 0.5f) / samples) / size) * 2f - 1f;
                        float radius = Mathf.Sqrt(px * px + py * py);
                        bool tooth = Mathf.Cos(Mathf.Atan2(py, px) * teeth) > 0f;
                        if (radius >= hole && radius <= (tooth ? outer : root))
                            inside++;
                    }
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * inside / (samples * samples)));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
