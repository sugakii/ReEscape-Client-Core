using UnityEngine;

/// <summary>
/// 从地毯脚步片段中随机播放一声，避免连续重复，供本机和远端玩家共用。
/// </summary>
public class FootstepAudio : MonoBehaviour
{
    [SerializeField]
    private AudioClip[] clips;

    [SerializeField]
    private AudioSource audioSource;

    [SerializeField]
    private float volumeScale = 1f;

    private int index = -1;

    // 多片段时跳过上一次下标，单片段时允许重复。
    public void PlayStep()
    {
        if(
            !isActiveAndEnabled
            || audioSource == null
            || !audioSource.isActiveAndEnabled
            || clips == null
            || clips.Length == 0
        )
        {
            return;
        }

        int randomIndex;
        AudioClip clip;

        if(index < 0 || index >= clips.Length || clips.Length == 1)
        {
            randomIndex = Random.Range(0, clips.Length);
        } else
        {
            randomIndex = Random.Range(0, clips.Length - 1);
            randomIndex = randomIndex >= index ? randomIndex + 1 : randomIndex;
        }

        clip = clips[randomIndex];
        if(clip == null) return;
        audioSource.PlayOneShot(clip, volumeScale);
        index = randomIndex;
    }

    // 死亡、结算和退出时清掉仍在播放的短音效。
    public void StopSteps()
    {
        if(audioSource != null) audioSource.Stop();
        index = -1;
    }

    private void OnDisable() => StopSteps();
}
