using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds Assets/Resources/UI/SettingPanel.prefab (the panel SettingPanel.Open instantiates).
/// The layout lives here, like the code-built HUD and help card; run the menu again after changing it.
/// Canvas reference size is 1980 x 1020 (GameCanvas).
/// </summary>
public static class SettingPanelPrefabBuilder
{
    const string PrefabPath = "Assets/Resources/UI/SettingPanel.prefab";

    static readonly Color CardColor = new Color(0.07f, 0.1f, 0.16f, 0.97f);
    static readonly Color OutlineColor = new Color(0.4f, 0.55f, 0.8f, 0.8f);
    static readonly Color Accent = new Color(1f, 0.82f, 0.3f);
    static readonly Color ButtonColor = new Color(0.16f, 0.22f, 0.34f, 1f);

    [MenuItem("Tools/Chord Pulse/Build Setting Panel Prefab")]
    public static void Build()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var resources = new DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd")
        };

        var root = new GameObject("SettingPanel", typeof(RectTransform));
        Stretch((RectTransform)root.transform);
        // Full-screen dim: also keeps clicks from reaching the HUD below.
        root.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        var panel = root.AddComponent<SettingPanel>();

        // --- Settings card ---
        var card = Child("Card", root.transform, Vector2.zero, new Vector2(640f, 420f));
        card.gameObject.AddComponent<Image>().color = CardColor;
        var outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = OutlineColor;
        outline.effectDistance = new Vector2(2f, -2f);

        Label("Title", card, "SETTINGS", font, 40, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0f, 165f), new Vector2(600f, 56f), Color.white);
        var music = VolumeRow(card, "Music", "MUSIC", 75f, font, resources, out var musicValue);
        var killChord = VolumeRow(card, "Kill Sound", "KILL SOUND", 5f, font, resources, out var killChordValue);
        var creditButton = TextButton("Credit Button", card, "CREDIT", font, resources,
            new Vector2(-130f, -125f), new Vector2(220f, 60f));
        var closeButton = TextButton("Close Button", card, "CLOSE", font, resources,
            new Vector2(130f, -125f), new Vector2(220f, 60f));

        // --- Credits roll (replaces the card while shown) ---
        var credits = Child("Credits", root.transform, Vector2.zero, Vector2.zero);
        Stretch(credits);
        credits.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.92f);

        var viewport = Child("Viewport", credits, Vector2.zero, Vector2.zero);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(0f, 130f);
        viewport.offsetMax = new Vector2(0f, -50f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = Label("Content", viewport, "", font, 28, FontStyle.Normal, TextAnchor.UpperCenter,
            Vector2.zero, new Vector2(1100f, 100f), Color.white);
        content.supportRichText = true;
        content.lineSpacing = 1.25f;
        content.horizontalOverflow = HorizontalWrapMode.Wrap;
        content.verticalOverflow = VerticalWrapMode.Overflow;
        var contentRect = content.rectTransform;
        contentRect.anchorMin = contentRect.anchorMax = contentRect.pivot = new Vector2(0.5f, 1f);

        var backButton = TextButton("Back Button", credits, "BACK", font, resources, Vector2.zero, new Vector2(220f, 60f));
        var backRect = (RectTransform)backButton.transform;
        backRect.anchorMin = backRect.anchorMax = backRect.pivot = new Vector2(0.5f, 0f);
        backRect.anchoredPosition = new Vector2(0f, 40f);

        var roll = credits.gameObject.AddComponent<CreditsRoll>();
        var rollObject = new SerializedObject(roll);
        rollObject.FindProperty("viewport").objectReferenceValue = viewport;
        rollObject.FindProperty("content").objectReferenceValue = content;
        rollObject.ApplyModifiedPropertiesWithoutUndo();
        credits.gameObject.SetActive(false);

        var panelObject = new SerializedObject(panel);
        panelObject.FindProperty("card").objectReferenceValue = card;
        panelObject.FindProperty("musicSlider").objectReferenceValue = music;
        panelObject.FindProperty("musicValue").objectReferenceValue = musicValue;
        panelObject.FindProperty("killChordSlider").objectReferenceValue = killChord;
        panelObject.FindProperty("killChordValue").objectReferenceValue = killChordValue;
        panelObject.FindProperty("creditButton").objectReferenceValue = creditButton;
        panelObject.FindProperty("closeButton").objectReferenceValue = closeButton;
        panelObject.FindProperty("credits").objectReferenceValue = credits.gameObject;
        panelObject.FindProperty("creditsBackButton").objectReferenceValue = backButton;
        panelObject.ApplyModifiedPropertiesWithoutUndo();

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();
        if (saved)
            Debug.Log($"[SettingPanelPrefabBuilder] Saved {PrefabPath}");
        else
            Debug.LogError($"[SettingPanelPrefabBuilder] Could not save {PrefabPath}");
    }

    // Label on the left, slider in the middle, percentage on the right.
    static Slider VolumeRow(RectTransform card, string rowName, string label, float y, Font font,
        DefaultControls.Resources resources, out Text value)
    {
        Label(rowName + " Label", card, label, font, 28, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Vector2(-195f, y), new Vector2(190f, 44f), Color.white);

        var sliderObject = DefaultControls.CreateSlider(resources);
        sliderObject.name = rowName + " Slider";
        var rect = (RectTransform)sliderObject.transform;
        rect.SetParent(card, false);
        rect.anchoredPosition = new Vector2(55f, y);
        rect.sizeDelta = new Vector2(290f, 28f);
        var slider = sliderObject.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        sliderObject.transform.Find("Background").GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);
        sliderObject.transform.Find("Fill Area/Fill").GetComponent<Image>().color = Accent;

        value = Label(rowName + " Value", card, "100%", font, 28, FontStyle.Normal, TextAnchor.MiddleRight,
            new Vector2(255f, y), new Vector2(90f, 44f), Color.white);
        return slider;
    }

    static Button TextButton(string buttonName, RectTransform parent, string label, Font font,
        DefaultControls.Resources resources, Vector2 position, Vector2 size)
    {
        var buttonObject = DefaultControls.CreateButton(resources);
        buttonObject.name = buttonName;
        var rect = (RectTransform)buttonObject.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        buttonObject.GetComponent<Image>().color = ButtonColor;
        var button = buttonObject.GetComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        // Tints are multiplied by 1.6, so normal is the image colour as it is and hover is brighter.
        var colors = button.colors;
        colors.colorMultiplier = 1.6f;
        colors.normalColor = colors.selectedColor = new Color(0.625f, 0.625f, 0.625f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = Accent;
        button.colors = colors;

        var text = buttonObject.GetComponentInChildren<Text>();
        text.text = label;
        text.font = font;
        text.fontSize = 28;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        return button;
    }

    static Text Label(string labelName, RectTransform parent, string value, Font font, int size, FontStyle style,
        TextAnchor alignment, Vector2 position, Vector2 boxSize, Color color)
    {
        var rect = Child(labelName, parent, position, boxSize);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    static RectTransform Child(string childName, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(childName, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
