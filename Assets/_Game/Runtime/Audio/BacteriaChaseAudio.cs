using UnityEngine;

/// <summary>
/// 播放细菌的三维追击开场和循环，处理延迟调度、淡出及追击恢复，不自行决定 AI 状态。
/// </summary>
public class BacteriaChaseAudio : MonoBehaviour
{
    [SerializeField]
    private AudioSource introSource;

    [SerializeField]
    private AudioSource loopSource;

    [SerializeField, Range(0f, 1f)]
    private float introVolume = 0.45f;

    [SerializeField, Range(0f, 1f)]
    private float loopVolume = 0.4f;

    [SerializeField, Min(0f)]
    private float loopDelay = 0.9f;

    [SerializeField, Min(0f)]
    private float fadeDuration = 2f;

    private bool isChasing;

    private bool hasScheduledAudio;

    private float volumeFactor;

    private bool loopScheduled;

    private double loopStartTime;

    private bool SourcesReady => introSource != null && loopSource != null
        && introSource != loopSource && introSource.isActiveAndEnabled && loopSource.isActiveAndEnabled
        && introSource.clip != null && loopSource.clip != null;

    // 相同状态不重播开场，淡出中重新追击则继续原来的声音。
    public void SetChasing(bool chasing)
    {
        if(!isActiveAndEnabled)
        {
            ResetAudio();
            return;
        }
        if(chasing == isChasing)
        {
            return;
        }

        if(chasing && !SourcesReady)
        {
            return;
        }

        isChasing = chasing;

        if(!isChasing)
        {
            // 短暂追击结束时取消尚未开始的循环，避免淡出期间才突然出现循环音轨。
            if(loopScheduled && AudioSettings.dspTime < loopStartTime)
            {
                if(loopSource != null)
                {
                    loopSource.Stop();
                }

                loopScheduled = false;
            }
            return;
        }

        if(hasScheduledAudio)
        {
            if(!loopScheduled)
            {
                ScheduleLoop(AudioSettings.dspTime + 0.05);
            }

            return;
        }

        volumeFactor = 1f;
        ApplyVolume();
        double startTime = AudioSettings.dspTime + 0.05;
        introSource.loop = false;
        introSource.PlayScheduled(startTime);
        ScheduleLoop(startTime + loopDelay);
        hasScheduledAudio = true;
    }

    private void ScheduleLoop(double startTime)
    {
        loopSource.loop = true;
        loopSource.PlayScheduled(startTime);
        loopStartTime = startTime;
        loopScheduled = true;
    }

    private void Awake() => ResetAudio();

    private void Update()
    {
        if(!hasScheduledAudio)
        {
            return;
        }

        if(!SourcesReady)
        {
            ResetAudio();
            return;
        }

        float targetVolume = isChasing ? 1f : 0f;
        volumeFactor = fadeDuration > 0f
            ? Mathf.MoveTowards(volumeFactor, targetVolume, Time.unscaledDeltaTime / fadeDuration)
            : targetVolume;
        ApplyVolume();
        if(!isChasing && volumeFactor <= 0f)
        {
            ResetAudio();
        }
    }

    private void ApplyVolume()
    {
        if(introSource != null)
        {
            introSource.volume = introVolume * volumeFactor;
        }

        if(loopSource != null)
        {
            loopSource.volume = loopVolume * volumeFactor;
        }
    }

    // Stop 同时取消已排队的播放，防止退出后延迟音轨再次响起。
    public void ResetAudio()
    {
        if(introSource != null)
        {
            introSource.Stop();
        }

        if(loopSource != null)
        {
            loopSource.Stop();
        }

        isChasing = false;
        hasScheduledAudio = false;
        loopScheduled = false;
        loopStartTime = 0;
        volumeFactor = 0f;
        ApplyVolume();
    }

    private void OnDisable() => ResetAudio();
}
