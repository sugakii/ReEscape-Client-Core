using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 检查两个设置入口、滑块实时应用、持久化和菜单状态切换。
/// </summary>
public static class SettingsMenuChecks
{
    public static void Run()
    {
        Require(EditorApplication.isPlaying, "Enter Play Mode first.");
        var menus = Object.FindAnyObjectByType<PrototypeMenus>();
        var settings = menus.GetComponent<SettingsMenuController>();
        var camera = Object.FindAnyObjectByType<CameraController>(FindObjectsInactive.Include);
        Transform canvas = menus.GetComponentInChildren<Canvas>(true).transform;
        Transform panel = canvas.Find("SettingsPanel");
        menus.SetPlayerControlAllowed(true);
        menus.ShowMainMenu();
        canvas.Find("MainMenu/SettingsButton").GetComponent<Button>().onClick.Invoke();
        Require(settings.IsOpen && menus.State == PrototypeMenus.MenuState.MainMenu && Time.timeScale == 0f,
            "Main settings changed menu or time state.");
        Quaternion originalRotation = camera.transform.rotation;
        Drag(panel.Find("VolumeSlider").GetComponent<Slider>(), 0.37f);
        Drag(panel.Find("SensitivitySlider").GetComponent<Slider>(), 0.4f);
        Require(Mathf.Abs(AudioListener.volume - 0.37f) < 0.001f && Mathf.Abs(camera.MouseSensitivity - 0.4f) < 0.001f,
            "Slider did not apply volume or sensitivity immediately.");
        Require(Quaternion.Angle(originalRotation, camera.transform.rotation) < 0.001f, "Sensitivity change moved camera.");
        Require(panel.Find("VolumeValue").GetComponent<Text>().text == "37%"
            && panel.Find("SensitivityValue").GetComponent<Text>().text == "0.40", "Values are not displayed correctly.");
        settings.SetMasterVolume(float.NaN);
        settings.SetMouseSensitivity(float.PositiveInfinity);
        Require(Mathf.Abs(settings.MasterVolume - 0.37f) < 0.001f && Mathf.Abs(settings.MouseSensitivity - 0.4f) < 0.001f,
            "Invalid setting was accepted.");
        panel.Find("BackButton").GetComponent<Button>().onClick.Invoke();
        Require(!settings.IsOpen && canvas.Find("MainMenu").gameObject.activeSelf, "Back did not restore main menu.");
        Require(Mathf.Abs(PlayerPrefs.GetFloat(SettingsMenuController.VolumeKey) - 0.37f) < 0.001f
            && Mathf.Abs(PlayerPrefs.GetFloat(SettingsMenuController.SensitivityKey) - 0.4f) < 0.001f,
            "Closing did not persist settings.");

        menus.StartGame();
        menus.PauseGame();
        canvas.Find("PauseMenu/SettingsButton").GetComponent<Button>().onClick.Invoke();
        Require(settings.IsOpen && menus.State == PrototypeMenus.MenuState.Paused && Time.timeScale == 1f
            && !menus.CanControlPlayer && !camera.enabled, "Pause settings unlocked controls or paused network time.");
        Require(!panel.Find("MenuBackground").gameObject.activeSelf, "Pause settings covered live scene with menu screenshot.");
        menus.HandleBackInput();
        Require(!settings.IsOpen && menus.State == PrototypeMenus.MenuState.Paused
            && canvas.Find("PauseMenu").gameObject.activeSelf && !camera.enabled, "Back resumed play unexpectedly.");
        menus.HandleBackInput();
        Require(camera.enabled && menus.CanControlPlayer, "Resume did not restore controls.");
        menus.SetPlayerControlAllowed(false);
        menus.PauseGame();
        menus.OpenSettings();
        menus.CloseSettings();
        menus.ResumeGame();
        Require(!menus.CanControlPlayer && !camera.enabled, "Settings removed death/execution control lock.");
        menus.PauseGame();
        menus.OpenSettings();
        menus.ShowRoundResult();
        Require(!settings.IsOpen && menus.State == PrototypeMenus.MenuState.Result, "Result left settings open.");
        menus.SetPlayerControlAllowed(true);
        menus.ShowMainMenu();
        Debug.Log("PASS: settings/main-and-pause/real-pointer-slider/live-apply/save/controls/result");
    }

    public static void VerifyReload()
    {
        var settings = Object.FindAnyObjectByType<SettingsMenuController>();
        var camera = Object.FindAnyObjectByType<CameraController>(FindObjectsInactive.Include);
        Require(Mathf.Abs(settings.MasterVolume - 0.37f) < 0.001f && Mathf.Abs(AudioListener.volume - 0.37f) < 0.001f
            && Mathf.Abs(settings.MouseSensitivity - 0.4f) < 0.001f && Mathf.Abs(camera.MouseSensitivity - 0.4f) < 0.001f,
            "New session did not restore saved settings.");
        Debug.Log("PASS: settings/restart-loads-saved-values");
    }

    private static void Drag(Slider slider, float value)
    {
        var area = (RectTransform)slider.handleRect.parent;
        float fraction = Mathf.InverseLerp(slider.minValue, slider.maxValue, value);
        Vector3 world = area.TransformPoint(new Vector3(Mathf.Lerp(area.rect.xMin, area.rect.xMax, fraction), area.rect.center.y, 0));
        var data = new PointerEventData(EventSystem.current)
        {
            position = RectTransformUtility.WorldToScreenPoint(null, world),
            button = PointerEventData.InputButton.Left
        };
        slider.OnPointerDown(data);
        slider.OnDrag(data);
        slider.OnPointerUp(data);
    }

    private static void Require(bool condition, string message)
    {
        if(!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
