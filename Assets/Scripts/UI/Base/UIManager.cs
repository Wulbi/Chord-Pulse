using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI 패널들을 관리하는 싱글톤 Manager
/// </summary>
public class UIManager : SingletonBehaviour<UIManager>
{
    [SerializeField] Transform canvasTransform;

    readonly Dictionary<UIPanelType, BasePanel> _panelDict = new Dictionary<UIPanelType, BasePanel>();
    readonly Stack<BasePanel> _panelStack = new Stack<BasePanel>();

    public UIPanelType CurrentPanel { get; private set; }

    public Transform CanvasTransform
    {
        get
        {
            if (canvasTransform != null)
                return canvasTransform;

            var canvas = GameObject.Find("GameCanvas");
            if (canvas == null)
                canvas = GameObject.Find("UI Canvas");

            if (canvas != null)
                canvasTransform = canvas.transform;

            return canvasTransform;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        RegisterPanelsInScene();
    }

    public void RegisterPanel(BasePanel panel)
    {
        if (panel == null)
            return;

        _panelDict[panel.TypeOfPanel] = panel;
        if (panel.gameObject.activeSelf)
            panel.gameObject.SetActive(false);
    }

    public void PushPanel(UIPanelType panelType, params object[] datas)
    {
        CurrentPanel = panelType;
        var panel = GetPanel(panelType);
        if (panel == null)
        {
            Debug.LogWarning($"[UIManager] Panel not found: {panelType}");
            return;
        }

        if (_panelStack.Count > 0)
        {
            var topPanel = _panelStack.Peek();
            if (panel.TypeOfPanelStyle != UIPanelStyleType.WINDOW)
                topPanel.OnPause();
        }

        panel.transform.SetAsLastSibling();
        panel.OnEnter(datas);
        _panelStack.Push(panel);
    }

    public void PopPanel()
    {
        if (_panelStack.Count <= 0)
            return;

        var topPanel = _panelStack.Pop();
        if (topPanel != null)
            topPanel.OnExit();

        while (_panelStack.Count > 0 && _panelStack.Peek() == null)
            _panelStack.Pop();

        if (_panelStack.Count <= 0)
            return;

        var next = _panelStack.Peek();
        if (next == null)
            return;

        CurrentPanel = next.TypeOfPanel;
        if (topPanel == null || topPanel.TypeOfPanelStyle != UIPanelStyleType.WINDOW)
            next.OnResume();
    }

    public void CloseAll()
    {
        while (_panelStack.Count > 0)
            PopPanel();
    }

    public BasePanel GetPanel(UIPanelType panelType)
    {
        if (_panelDict.TryGetValue(panelType, out var panel) && panel != null)
            return panel;

        RegisterPanelsInScene();
        _panelDict.TryGetValue(panelType, out panel);
        return panel;
    }

    void RegisterPanelsInScene()
    {
        var panels = FindObjectsByType<BasePanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var panel in panels)
            RegisterPanel(panel);
    }
}
