using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 主菜单与暂停菜单共用的设置界面，实时应用并保存本机总音量和鼠标灵敏度。
/// </summary>
[DefaultExecutionOrder(-50)]
public class SettingsMenuController : MonoBehaviour
{
    public const string VolumeKey = "ReEscape.MasterVolume";

    public const string SensitivityKey = "ReEscape.MouseSensitivity";

    [SerializeField]
    private GameObject settingsPanel;

    [SerializeField]
    private GameObject menuBackground;

    [SerializeField]
    private Slider volumeSlider;

    [SerializeField]
    private Slider sensitivitySlider;

    [SerializeField]
    private Text volumeText;

    [SerializeField]
    private Text sensitivityText;

    [SerializeField]
    private CameraController cameraController;

    private float volume;

    private float sensitivity;

    private bool dirty;

    public bool IsOpen => settingsPanel != null && settingsPanel.activeSelf;

    public float MasterVolume => volume;

    public float MouseSensitivity => sensitivity;

    // 首次启动沿用相机现有灵敏度，已保存的设置会在进入游戏前应用。
    private void Awake()
    {
        float defaultSensitivity = cameraController != null ? cameraController.MouseSensitivity : 0.2f;
        volume = ReadSetting(VolumeKey, 1f, 0f, 1f);
        sensitivity = ReadSetting(SensitivityKey, defaultSensitivity, 0.05f, 1f);
        AudioListener.volume = volume;
        if(cameraController != null)
        {
            cameraController.SetMouseSensitivity(sensitivity);
        }

        if(volumeSlider != null)
        {
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;
            volumeSlider.onValueChanged.AddListener(SetMasterVolume);
        }
        if(sensitivitySlider != null)
        {
            sensitivitySlider.minValue = 0.05f;
            sensitivitySlider.maxValue = 1f;
            sensitivitySlider.onValueChanged.AddListener(SetMouseSensitivity);
        }
        RefreshValues();
        if(settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
    }

    // 打开设置，并保留主菜单或暂停菜单对应的背景。
    public void Show(bool fromMainMenu)
    {
        if(settingsPanel == null)
        {
            return;
        }

        if(menuBackground != null)
        {
            menuBackground.SetActive(fromMainMenu);
        }

        RefreshValues();
        settingsPanel.SetActive(true);
        settingsPanel.transform.SetAsLastSibling();
        if(EventSystem.current != null && volumeSlider != null)
        {
            EventSystem.current.SetSelectedGameObject(volumeSlider.gameObject);
        }
    }

    // 关闭设置并保存本次调整。
    public void Hide()
    {
        Save();
        if(settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
    }

    // 总音量保留各 AudioSource 原有比例，覆盖脚步、追击音与死亡断讯音。
    public void SetMasterVolume(float value)
    {
        if(!float.IsFinite(value))
        {
            return;
        }

        volume = Mathf.Clamp01(value);
        AudioListener.volume = volume;
        dirty = true;
        RefreshValues();
    }

    // 更新本机相机灵敏度，并同步滑块显示。
    public void SetMouseSensitivity(float value)
    {
        if(!float.IsFinite(value))
        {
            return;
        }

        sensitivity = Mathf.Clamp(value, 0.05f, 1f);
        if(cameraController != null)
        {
            cameraController.SetMouseSensitivity(sensitivity);
        }

        dirty = true;
        RefreshValues();
    }

    // 刷新控件数值，避免再次触发滑块回调。
    private void RefreshValues()
    {
        if(volumeSlider != null)
        {
            volumeSlider.SetValueWithoutNotify(volume);
        }

        if(sensitivitySlider != null)
        {
            sensitivitySlider.SetValueWithoutNotify(sensitivity);
        }

        if(volumeText != null)
        {
            volumeText.text = Mathf.RoundToInt(volume * 100f) + "%";
        }

        if(sensitivityText != null)
        {
            sensitivityText.text = sensitivity.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // 滑动时只改变当前设置，关闭界面或退出程序时再写入磁盘。
    private void Save()
    {
        if(!dirty)
        {
            return;
        }

        PlayerPrefs.SetFloat(VolumeKey, volume);
        PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
        PlayerPrefs.Save();
        dirty = false;
    }

    // 读取保存值，并将异常值恢复到允许范围。
    private static float ReadSetting(string key, float fallback, float min, float max)
    {
        float value = PlayerPrefs.GetFloat(key, fallback);
        return float.IsFinite(value) ? Mathf.Clamp(value, min, max) : Mathf.Clamp(fallback, min, max);
    }

    private void OnApplicationQuit() => Save();

    private void OnDestroy()
    {
        Save();
        if(volumeSlider != null)
        {
            volumeSlider.onValueChanged.RemoveListener(SetMasterVolume);
        }

        if(sensitivitySlider != null)
        {
            sensitivitySlider.onValueChanged.RemoveListener(SetMouseSensitivity);
        }
    }
}
