using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Settings: the two volume sliders (SoundManager.MusicMix / KillChordMix) and the credits roll.
/// A prefab (Resources/UI/SettingPanel, built by Tools > Chord Pulse > Build Setting Panel Prefab) that
/// <see cref="Open"/> instantiates under the UI canvas on first use and registers with the UIManager.
/// It is a PANEL, so pushing it pauses the panel below (GamePanel.IsPlaying goes false and the round stops updating);
/// meanwhile it holds Time.timeScale at 0 so the round timer does not run, and the music is frozen
/// (SoundManager.PauseMusic) and continues on the same beat when the panel closes. Volume changes are heard after that.
/// Esc closes the credits, then the panel.
/// </summary>
public sealed class SettingPanel : BasePanel
{
    public const string ResourcePath = "UI/SettingPanel";
    public override UIPanelType TypeOfPanel => UIPanelType.SETTING_PANEL;

    [SerializeField] RectTransform card;
    [SerializeField] Slider musicSlider;
    [SerializeField] Text musicValue;
    [SerializeField] Slider killChordSlider;
    [SerializeField] Text killChordValue;
    [SerializeField] Button creditButton;
    [SerializeField] Button closeButton;
    [SerializeField] GameObject credits;
    [SerializeField] Button creditsBackButton;

    // The instance Open made; Unity's null check covers a scene unload.
    static SettingPanel _instance;

    Tween _openTween;
    bool _open;
    bool _changed;
    float _previousTimeScale = 1f;

    /// <summary>Opens the panel on top of the current one. False if it is already open or the prefab is missing.</summary>
    public static bool Open()
    {
        var ui = UIManager.Instance;
        // Not UIManager.GetPanel: for a panel it does not know yet it registers every panel in the scene again, and
        // registering deactivates them - the HUD under this panel would disappear.
        var panel = _instance;
        if (panel == null)
        {
            var prefab = Resources.Load<SettingPanel>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"[SettingPanel] Prefab not found: Resources/{ResourcePath}");
                return false;
            }
            panel = Instantiate(prefab, ui.CanvasTransform, false);
            panel.name = prefab.name;
            ui.RegisterPanel(panel);
            _instance = panel;
        }
        if (panel._open)
            return false;

        ui.PushPanel(UIPanelType.SETTING_PANEL);
        return true;
    }

    void Awake()
    {
        musicSlider.onValueChanged.AddListener(OnMusicChanged);
        killChordSlider.onValueChanged.AddListener(OnKillChordChanged);
        creditButton.onClick.AddListener(() => ShowCredits(true));
        creditsBackButton.onClick.AddListener(() => ShowCredits(false));
        closeButton.onClick.AddListener(OnClose);
    }

    public override void OnEnter(params object[] datas)
    {
        _open = true;
        _changed = false;
        _previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        float music = 1f, killChord = 1f;
        if (SoundManager.HasInstance)
        {
            SoundManager.Instance.PauseMusic();
            music = SoundManager.Instance.MusicMix;
            killChord = SoundManager.Instance.KillChordMix;
        }
        musicSlider.SetValueWithoutNotify(music);
        killChordSlider.SetValueWithoutNotify(killChord);
        musicValue.text = Percent(music);
        killChordValue.text = Percent(killChord);
        ShowCredits(false);
        base.OnEnter(datas);
    }

    public override void OnOpenEffect()
    {
        _openTween?.Kill();
        card.localScale = Vector3.one * 0.001f;
        _openTween = card.DOScale(1f, 0.2f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(gameObject);
    }

    public override void OnExit()
    {
        _openTween?.Kill();
        Leave();
        base.OnExit();
    }

    public override void OnClose()
    {
        if (_open)
            base.OnClose();
    }

    void OnDestroy() => Leave();

    // Gives the game its time scale and its music back and stores the volumes the player changed.
    void Leave()
    {
        if (!_open)
            return;
        _open = false;
        Time.timeScale = _previousTimeScale;
        if (SoundManager.HasInstance)
            SoundManager.Instance.ResumeMusic();
        if (_changed)
            VolumeSettings.Save(musicSlider.value, killChordSlider.value);
        Deselect();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            return;
        if (credits.activeSelf)
            ShowCredits(false);
        else
            OnClose();
    }

    void ShowCredits(bool show)
    {
        credits.SetActive(show);
        card.gameObject.SetActive(!show);
        Deselect();
    }

    void OnMusicChanged(float value)
    {
        _changed = true;
        musicValue.text = Percent(value);
        if (SoundManager.HasInstance)
            SoundManager.Instance.MusicMix = value;
    }

    void OnKillChordChanged(float value)
    {
        _changed = true;
        killChordValue.text = Percent(value);
        if (SoundManager.HasInstance)
            SoundManager.Instance.KillChordMix = value;
    }

    static string Percent(float value) => Mathf.RoundToInt(value * 100f) + "%";

    // A clicked button stays selected, and Space / Enter would press it again (Space is also the game's fire key).
    static void Deselect()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }
}
