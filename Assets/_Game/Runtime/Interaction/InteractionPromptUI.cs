using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 控制交互提示的显示、颜色和屏幕位置，不负责判定交互条件。
/// </summary>
[DefaultExecutionOrder(100)]
public class InteractionPromptUI : MonoBehaviour
{
    [SerializeField]
    private Image promptImage;

    [SerializeField]
    private Color normalColor = new Color(0.65f, 0.65f, 0.65f, 1f);

    [SerializeField]
    private Color pressedColor = new Color(0.35f, 0.35f, 0.35f, 1f);

    [SerializeField]
    private Color highlightedColor = Color.white;

    [SerializeField]
    private float pressedDuration = 0.1f;

    [SerializeField]
    private Camera playerCamera;

    [SerializeField]
    // 通常放在门把手或物品附近。
    private Transform anchor;

    [SerializeField]
    private float referenceDepth = 4f;

    [SerializeField]
    private float minScale = 1f;

    [SerializeField]
    private float maxScale = 10f;

    private bool isHighlighted;

    private float pressedUntil;

    // 取得相机，分离提示层级并默认隐藏。
    private void Awake()
    {
        if(playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        transform.SetParent(null, false);

        SetHighlighted(false);
        SetVisible(false);
    }

    // 设置提示是否显示。
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    // 设置高亮状态并刷新颜色。
    public void SetHighlighted(bool highlighted)
    {
        isHighlighted = highlighted;
        RefreshColor();
    }

    // 短暂变深，提供按下反馈。
    public void PlayPressedEffect()
    {
        pressedUntil = Time.time + pressedDuration;
        RefreshColor();
    }

    // 按下颜色优先，其次是高亮和普通颜色。
    private void RefreshColor()
    {
        if(Time.time < pressedUntil)
        {
            promptImage.color = pressedColor;
            return;
        }

        promptImage.color = isHighlighted ? highlightedColor : normalColor;
    }

    // 按下效果结束后恢复颜色。
    private void Update()
    {
        if(pressedUntil > 0f && Time.time >= pressedUntil)
        {
            pressedUntil = 0f;
            RefreshColor();
        }
    }

    // 把锚点投影到屏幕，并按距离调整提示大小。
    private void LateUpdate()
    {
        Vector3 screenPoint = playerCamera.WorldToScreenPoint(anchor.position);

        promptImage.rectTransform.position = new Vector3(
            screenPoint.x,
            screenPoint.y,
            0f
        );

        float distance = Vector3.Distance(
            playerCamera.transform.position,
            anchor.position
        );

        // 限制缩放和除数，避免距离接近 0 时数值异常。
        float scale = Mathf.Clamp(
            referenceDepth / Mathf.Max(distance, 0.01f),
            minScale,
            maxScale
        );

        promptImage.rectTransform.localScale = Vector3.one * scale;
    }

    // 隐藏时清除临时按下和高亮状态。
    private void OnDisable()
    {
        pressedUntil = 0f;
        isHighlighted = false;
        RefreshColor();
    }
}
