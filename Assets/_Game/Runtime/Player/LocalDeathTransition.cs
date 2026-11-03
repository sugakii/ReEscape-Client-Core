using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 仅在受害者本机播放一秒断讯花屏，黑屏后通知观战或结算流程接手。
/// </summary>
public class LocalDeathTransition : MonoBehaviour
{
    [SerializeField]
    private GameObject overlay;

    [SerializeField]
    private RawImage noiseImage;

    [SerializeField]
    private AudioSource disconnectAudio;

    [SerializeField]
    private VideoPlayer staticVideo;

    [SerializeField, Min(0.1f)]
    private float duration = 1f;

    private Material noiseMaterial;

    private float elapsed;

    private int finishedFrame = -1;

    private bool waitingForVideo;

    private bool videoFailed;

    public bool IsPlaying { get; private set; }

    public event Action Finished;

    private void Awake()
    {
        if(noiseImage != null && noiseImage.material != null)
        {
            noiseMaterial = new Material(noiseImage.material);
            noiseImage.material = noiseMaterial;
        }
        ResetPresentation();
        if(staticVideo != null && !Application.isBatchMode)
        {
            staticVideo.errorReceived += HandleVideoError;
            staticVideo.Prepare();
        }
    }

    // 启动本机死亡转场，准备花屏和断讯音。
    public void Begin()
    {
        if(IsPlaying)
        {
            return;
        }

        elapsed = 0f;
        finishedFrame = -1;
        IsPlaying = true;
        if(noiseMaterial != null)
        {
            noiseMaterial.SetFloat("_Progress", 0f);
        }

        if(overlay != null)
        {
            overlay.SetActive(true);
        }

        waitingForVideo = staticVideo != null && !videoFailed;
        if(!waitingForVideo && disconnectAudio != null)
        {
            disconnectAudio.Play();
        }
    }

    // 等待视频就绪并推进转场，结束后交给观战流程。
    private void Update()
    {
        if(!IsPlaying)
        {
            return;
        }

        if(waitingForVideo)
        {
            if(staticVideo.isPrepared)
            {
                waitingForVideo = false;
                elapsed = 0f;
                staticVideo.Play();
                if(staticVideo.clip.audioTrackCount == 0 && disconnectAudio != null)
                {
                    disconnectAudio.Play();
                }
            }
            else
            {
                elapsed += Time.unscaledDeltaTime;
                if(elapsed < 2f && !videoFailed)
                {
                    return;
                }

                waitingForVideo = false;
                elapsed = 0f;
                if(disconnectAudio != null)
                {
                    disconnectAudio.Play();
                }
            }
        }
        if(staticVideo != null && staticVideo.isPlaying && staticVideo.texture != null && noiseImage != null)
        {
            noiseImage.texture = staticVideo.texture;
            if(noiseMaterial != null)
            {
                noiseMaterial.SetFloat("_UseVideo", 1f);
            }
        }
        if(finishedFrame >= 0)
        {
            // 留出完整黑屏帧，让新摄像机或结算黑幕先就绪。
            if(Time.frameCount > finishedFrame)
            {
                ResetPresentation();
            }

            return;
        }
        elapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        if(noiseMaterial != null)
        {
            noiseMaterial.SetFloat("_Progress", progress);
        }

        if(progress < 1f)
        {
            return;
        }

        finishedFrame = Time.frameCount;
        if(disconnectAudio != null)
        {
            disconnectAudio.Stop();
        }

        if(staticVideo != null && staticVideo.isPlaying)
        {
            staticVideo.Pause();
        }

        Finished?.Invoke();
    }

    // 退出当前转场时关闭画面、声音和计时。
    public void ResetPresentation()
    {
        IsPlaying = false;
        finishedFrame = -1;
        elapsed = 0f;
        waitingForVideo = false;
        if(noiseMaterial != null)
        {
            noiseMaterial.SetFloat("_UseVideo", 0f);
        }

        if(staticVideo != null && staticVideo.isPrepared)
        {
            staticVideo.Pause();
            staticVideo.time = 0d;
        }
        if(disconnectAudio != null)
        {
            disconnectAudio.Stop();
        }

        if(overlay != null)
        {
            overlay.SetActive(false);
        }
    }

    private void OnDisable() => ResetPresentation();

    private void OnDestroy()
    {
        if(staticVideo != null)
        {
            staticVideo.errorReceived -= HandleVideoError;
        }

        if(noiseMaterial != null)
        {
            Destroy(noiseMaterial);
        }
    }

    // 解码失败仍须完成死亡流程，不能永久卡住观战和结算。
    private void HandleVideoError(VideoPlayer player, string message)
    {
        videoFailed = true;
        Debug.LogWarning("[LocalDeathTransition] 花屏视频解码失败，使用备用噪声：" + message, this);
    }
}
