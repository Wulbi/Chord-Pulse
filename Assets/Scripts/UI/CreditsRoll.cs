using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Film-style credits: the text rises from below the viewport, leaves at the top and starts over.
/// Runs on unscaled time (the settings panel holds the game at time scale 0).
/// </summary>
public sealed class CreditsRoll : MonoBehaviour
{
    [SerializeField] RectTransform viewport;
    [Tooltip("Top-anchored (pivot y = 1) child of the viewport that carries the text.")]
    [SerializeField] Text content;
    [Tooltip("Canvas units per second.")]
    [SerializeField] float speed = 80f;

    float _offset;
    float _height;

    void OnEnable()
    {
        content.text = CreditsContent.Build();
        var rect = content.rectTransform;
        _height = content.preferredHeight;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _height);
        Restart();
    }

    void Update()
    {
        _offset += speed * Time.unscaledDeltaTime;
        if (_offset > _height)
            Restart();
        else
            Place();
    }

    // The text's top edge starts at the viewport's bottom edge.
    void Restart()
    {
        _offset = -viewport.rect.height;
        Place();
    }

    void Place()
    {
        content.rectTransform.anchoredPosition = new Vector2(0f, _offset);
    }
}
