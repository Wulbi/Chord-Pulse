using UnityEngine;

public class BasePanel : MonoBehaviour
{
    public virtual UIPanelType TypeOfPanel => UIPanelType.GAME_OVER_PANEL;
    public virtual UIPanelStyleType TypeOfPanelStyle => UIPanelStyleType.PANEL;

    public virtual void OnEnter(params object[] datas)
    {
        gameObject.SetActive(true);
        OnOpenEffect();
    }

    public virtual void OnPause() { }
    public virtual void OnResume() { }

    public virtual void OnExit()
    {
        gameObject.SetActive(false);
    }

    public virtual void OnClose()
    {
        UIManager.Instance.PopPanel();
    }

    public virtual void OnOpenEffect() { }
    public virtual void OnBackButton() { }
}
