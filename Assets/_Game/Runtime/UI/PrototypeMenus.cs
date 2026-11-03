using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 管理本机菜单、暂停和结算状态，协调玩家输入、准星、时间和鼠标。
/// </summary>
public class PrototypeMenus : MonoBehaviour
{
    [SerializeField]
    private GameObject mainMenu;

    [SerializeField]
    private GameObject pauseMenu;

    [SerializeField]
    private Button startButton;

    [SerializeField]
    private Button mainQuitButton;

    [SerializeField]
    private Button resumeButton;

    [SerializeField]
    private Button returnButton;

    [SerializeField]
    private Button pauseQuitButton;

    [SerializeField]
    // 移动、视角和交互组件，由菜单状态和游戏中的控制限制共同决定是否启用。
    private Behaviour[] playerControls;

    [SerializeField]
    private NetworkSession networkSession;

    [SerializeField]
    private GameObject crosshair;

    [SerializeField]
    private SettingsMenuController settingsMenu;

    public enum MenuState
    {
        MainMenu, Playing, Paused, Result
    }

    public MenuState State { get; private set; }

    public bool CanControlPlayer => State == MenuState.Playing && playerControlAllowed;

    private bool playerControlAllowed = true;

    private bool[] originalControlStates;

    private float gameplayTimeScale = 1f;

    private CursorLockMode originalCursorLock;

    private bool originalCursorVisible;

    private GameObject settingsReturnSelection;

    // 保存原始控制状态，再显示主菜单。
    private void Awake()
    {
        originalCursorLock = Cursor.lockState;
        originalCursorVisible = Cursor.visible;
        originalControlStates = new bool[playerControls.Length];
        for(int i = 0; i < playerControls.Length; i++)
        {
            originalControlStates[i] = playerControls[i] && playerControls[i].enabled;
        }

        ApplyState(MenuState.MainMenu);
    }

    // 用 Esc 在游戏与暂停之间切换。
    private void Update()
    {
        if(Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            return;
        }

        HandleBackInput();
    }

    // 设置先返回来源菜单，再次返回才恢复暂停中的游戏。
    public void HandleBackInput()
    {
        if(settingsMenu != null && settingsMenu.IsOpen)
        {
            CloseSettings();
            return;
        }

        if(State == MenuState.Playing)
        {
            PauseGame();
        }
        else if(State == MenuState.Paused)
        {
            ResumeGame();
        }
    }

    // 只从主菜单进入游戏，重复的 Playing 快照不会解除本机暂停。
    public void StartGame()
    {
        if(State == MenuState.MainMenu)
        {
            ApplyState(MenuState.Playing);
        }
    }

    // 打开本机暂停菜单并禁用控制，联机世界继续运行。
    public void PauseGame()
    {
        if(State != MenuState.Playing)
        {
            return;
        }

        ApplyState(MenuState.Paused);
    }

    // 结果界面保持联机世界时间运行，但禁止控制，Esc 不会返回游戏。
    public void ShowRoundResult()
    {
        playerControlAllowed = false;
        ApplyState(MenuState.Result);
    }

    // 恢复本机游戏状态。
    public void ResumeGame()
    {
        if(State == MenuState.Paused)
        {
            ApplyState(MenuState.Playing);
        }
    }

    // 设置保留来源状态，暂停期间不会重新启用玩家输入。
    public void OpenSettings()
    {
        if(settingsMenu == null || settingsMenu.IsOpen
            || (State != MenuState.MainMenu && State != MenuState.Paused))
        {
            return;
        }

        settingsReturnSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        mainMenu.SetActive(false);
        pauseMenu.SetActive(false);
        settingsMenu.Show(State == MenuState.MainMenu);
    }

    // 关闭设置后恢复来源菜单和选中项。
    public void CloseSettings()
    {
        if(settingsMenu == null || !settingsMenu.IsOpen)
        {
            return;
        }

        settingsMenu.Hide();
        mainMenu.SetActive(State == MenuState.MainMenu);
        pauseMenu.SetActive(State == MenuState.Paused);
        if(EventSystem.current != null)
        {
            GameObject selection = settingsReturnSelection;
            if(selection == null || !selection.activeInHierarchy)
            {
                selection = State == MenuState.MainMenu ? startButton.gameObject : resumeButton.gameObject;
            }

            EventSystem.current.SetSelectedGameObject(selection);
        }
        settingsReturnSelection = null;
    }

    // 处决、死亡等状态通过这个入口限制操作；暂停和恢复不会解除限制。
    public void SetPlayerControlAllowed(bool allowed)
    {
        playerControlAllowed = allowed;
        RefreshPlayerControls();
    }

    private void RefreshPlayerControls()
    {
        if(crosshair != null)
        {
            crosshair.SetActive(CanControlPlayer);
        }

        if(originalControlStates == null)
        {
            return;
        }

        for(int i = 0; i < playerControls.Length; i++)
        {
            if(playerControls[i])
            {
                playerControls[i].enabled = CanControlPlayer && originalControlStates[i];
            }
        }
    }

    // 请求退出网络会话，由断线回调恢复主菜单。
    public void ReturnToMainMenu()
    {
        networkSession.LeaveRoom();
    }

    // 恢复菜单状态、鼠标和控制开关，关闭暂停界面。
    public void ShowMainMenu()
    {
        ApplyState(MenuState.MainMenu);
    }

    // 编辑器内停止运行，独立程序中退出应用。
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // 统一设置面板、控制组件、时间和鼠标。
    private void ApplyState(MenuState next)
    {
        // 断线、开局、处决转场和结算都不能残留设置面板。
        if(settingsMenu != null)
        {
            settingsMenu.Hide();
        }

        settingsReturnSelection = null;
        State = next;
        bool playing = next == MenuState.Playing;
        mainMenu.SetActive(next == MenuState.MainMenu);
        pauseMenu.SetActive(next == MenuState.Paused);

        // 主菜单停止本地模拟；游戏和暂停菜单保持时间运行。
        if(next == MenuState.MainMenu)
        {
            gameplayTimeScale = 0f;
            Time.timeScale = gameplayTimeScale;
        }
        else
        {
            gameplayTimeScale = 1f;
            Time.timeScale = gameplayTimeScale;
        }

        RefreshPlayerControls();

        Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !playing;
        if(EventSystem.current)
        {
            EventSystem.current.SetSelectedGameObject(next == MenuState.MainMenu ? startButton.gameObject :
            (next == MenuState.Paused ? resumeButton.gameObject : null));
        }
    }

    // 退出场景时恢复时间、鼠标和控制状态。
    private void OnDestroy()
    {
        if(originalControlStates == null)
        {
            return;
        }

        // Play Mode skips domain reload, so restore global state on scene exit.
        Time.timeScale = 1f;
        Cursor.lockState = originalCursorLock;
        Cursor.visible = originalCursorVisible;

        for(int i = 0; i < playerControls.Length; i++)
        {
            if(playerControls[i])
            {
                playerControls[i].enabled = originalControlStates[i];
            }
        }
    }
}
