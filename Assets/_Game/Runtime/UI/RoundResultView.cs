using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 先将当前画面淡到全黑，再显示结算内容；支持等待本机死亡转场完成后再展示。
/// </summary>
public class RoundResultView : MonoBehaviour
{
    [SerializeField]
    private UnityEngine.UI.Image blackout;

    [SerializeField]
    private CanvasGroup content;

    [SerializeField]
    private Button firstButton;

    [SerializeField]
    private PlayerSpectator spectator;

    [SerializeField, Min(0f)]
    private float blackoutDuration = 0.9f;

    [SerializeField, Min(0f)]
    private float contentFadeDuration = 0.35f;

    private bool presentationBlocked;

    private bool blackReached;

    private float elapsed;

    public bool IsShowing { get; private set; }

    // 重复快照不重新开始转场，等待死亡花屏期间只保留展示请求。
    public void Show()
    {
        if(IsShowing)
        {
            return;
        }

        IsShowing = true;
        if(!presentationBlocked)
        {
            BeginPresentation(false);
        }
    }

    // 花屏结束后若已是黑屏，可直接从黑屏衔接结算内容。
    public void SetBlocked(bool blocked, bool alreadyBlack = false)
    {
        if(presentationBlocked == blocked)
        {
            return;
        }

        presentationBlocked = blocked;
        if(!IsShowing)
        {
            return;
        }

        if(blocked)
        {
            SetContentAlpha(0f);
            gameObject.SetActive(false);
        }
        else
        {
            BeginPresentation(alreadyBlack);
        }
    }

    private void BeginPresentation(bool alreadyBlack)
    {
        if(blackout == null || content == null)
        {
            Debug.LogError("[RoundResultView] 请绑定黑幕和结算内容。", this);
            return;
        }

        elapsed = 0f;
        blackReached = alreadyBlack;
        SetBlackAlpha(alreadyBlack ? 1f : 0f);
        SetContentAlpha(0f);
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        if(EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        if(alreadyBlack && spectator != null)
        {
            spectator.StopSpectating();
        }
    }

    private void Update()
    {
        if(!IsShowing || presentationBlocked || blackout == null || content == null)
        {
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        if(!blackReached)
        {
            float progress = blackoutDuration > 0f ? Mathf.Clamp01(elapsed / blackoutDuration) : 1f;
            SetBlackAlpha(Mathf.SmoothStep(0f, 1f, progress));
            if(progress < 1f)
            {
                return;
            }

            // 先保留一帧完整黑屏，切换摄像机不会暴露在淡黑过程中。
            blackReached = true;
            elapsed = 0f;
            if(spectator != null)
            {
                spectator.StopSpectating();
            }

            return;
        }

        float contentProgress = contentFadeDuration > 0f
            ? Mathf.Clamp01(elapsed / contentFadeDuration) : 1f;
        SetContentAlpha(Mathf.SmoothStep(0f, 1f, contentProgress));
        if(contentProgress >= 1f && !content.interactable)
        {
            content.interactable = true;
            content.blocksRaycasts = true;
            if(EventSystem.current != null && firstButton != null && firstButton.isActiveAndEnabled
                && firstButton.IsInteractable())
            {
                EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
            }
        }
    }

    private void SetBlackAlpha(float alpha)
    {
        if(blackout != null)
        {
            blackout.color = new Color(0f, 0f, 0f, alpha);
        }
    }

    private void SetContentAlpha(float alpha)
    {
        if(content == null)
        {
            return;
        }

        content.alpha = alpha;
        // 已完成时由 Update 开启交互；隐藏期间不接受鼠标和键盘确认。
        if(alpha < 1f)
        {
            content.interactable = false;
            content.blocksRaycasts = false;
        }
    }

    public void Cancel()
    {
        IsShowing = false;
        presentationBlocked = false;
        blackReached = false;
        elapsed = 0f;
        SetBlackAlpha(0f);
        SetContentAlpha(0f);
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if(content == null)
        {
            return;
        }

        content.interactable = false;
        content.blocksRaycasts = false;
    }
}
