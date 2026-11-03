using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System;
using UnityEngine.EventSystems;

/// <summary>
/// 切换房间相关面板，并把客户端名单显示到大厅固定槽位。
/// 根据本机身份和成员准备状态刷新按钮，联动本机菜单状态并显示连接提示。
/// 展示服务器确认的结算结果，并提供离开房间入口。
/// </summary>
public class RoomMenuController : MonoBehaviour
{
    [SerializeField]
    private GameObject mainManu;

    [SerializeField]
    private GameObject createRoomPanel;

    [SerializeField]
    private GameObject lobbyPanel;

    [SerializeField]
    private GameObject roomListPanel;

    [SerializeField]
    private Text roomNameText;

    [SerializeField]
    private GameObject readyButtonObject;

    [SerializeField]
    private GameObject hostStartButtonObject;

    [SerializeField]
    private Text playerCountText;

    [SerializeField]
    // 按槽位 1～4 排列，下标对应 PlayerNumber - 1。
    private LobbyMemberRow[] memberRows;

    [SerializeField]
    private Text readyButtonText;

    [SerializeField]
    private Text hostStartButtonText;

    [SerializeField]
    private PrototypeMenus prototypeMenus;

    [SerializeField]
    private Text connectionMessageText;

    [SerializeField]
    private GameObject resultPanel;

    [SerializeField]
    private Text resultTitleText;

    [SerializeField]
    private Text resultDescriptionText;

    [SerializeField]
    private Button resultLeaveButton;

    [SerializeField]
    private RoundResultView resultView;

    private RoundResult shownResult = RoundResult.None;

    // 显示本次连接的拒绝或断线原因；空内容用于正常退出。
    public void ShowConnectionMessage(string message)
    {
        if(connectionMessageText == null)
        {
            Debug.LogError("[RoomMenuController] 未绑定连接提示文字。", this);
            return;
        }

        connectionMessageText.text = message ?? string.Empty;
    }

    // 新的连接流程或连接成功时清除旧提示。
    public void ClearConnectionMessage()
    {
        ShowConnectionMessage(string.Empty);
    }

    // 显示创建房间页面，不启动服务器。
    public void ShowCreateRoom()
    {
        if(roomListPanel != null)
        {
            roomListPanel.SetActive(false);
        }

        HideRoundResult();
        mainManu.SetActive(false);
        createRoomPanel.SetActive(true);
        lobbyPanel.SetActive(false);
    }

    // 显示主菜单并恢复本机菜单状态；网络关闭由 NetworkSession 处理。
    public void ShowMainManu()
    {
        if(roomListPanel != null)
        {
            roomListPanel.SetActive(false);
        }

        HideRoundResult();
        mainManu.SetActive(true);
        createRoomPanel.SetActive(false);
        lobbyPanel.SetActive(false);
        prototypeMenus.ShowMainMenu();
    }

    // 显示大厅，并按 房主身份切换操作按钮。
    public void ShowLobby(bool isLeader)
    {
        if(roomListPanel != null)
        {
            roomListPanel.SetActive(false);
        }

        HideRoundResult();
        mainManu.SetActive(false);
        createRoomPanel.SetActive(false);
        lobbyPanel.SetActive(true);

        if(isLeader)
        {
            readyButtonObject.SetActive(false);
            hostStartButtonObject.SetActive(true);
            return;
        }

        readyButtonObject.SetActive(true);
        readyButtonObject.GetComponent<Button>().interactable = true;
        hostStartButtonObject.SetActive(false);
    }

    // 先清空旧显示，再按 PlayerNumber 填入固定槽位。
    public void RefreshMembers(List<LobbyMember> members, ulong localClientId)
    {
        foreach(LobbyMemberRow memberRow in memberRows)
        {
            memberRow.ClearSlot();
        }

        foreach(LobbyMember member in members)
        {
            int index = member.PlayerNumber - 1;

            if(index < 0 || index >= memberRows.Length)
            {
                Debug.LogWarning($"成员槽位下标无效：{index}");
                continue;
            }

            bool isLocalPlayer = member.ClientId == localClientId;

            memberRows[index].SetMember(member, isLocalPlayer);
        }

        playerCountText.text = $"人数: {members.Count} / {memberRows.Length}";

        RefreshLobbyActions(members, localClientId);
    }

    // 下方按钮按本机身份刷新；房主开始按钮还需检查其他成员是否全部准备。
    private void RefreshLobbyActions(List<LobbyMember> members, ulong localClientId)
    {
        bool isInList = false;
        LobbyMember localMember = null;

        foreach(LobbyMember member in members)
        {
            if(member.ClientId == localClientId)
            {
                isInList = true;

                localMember = member;

                break;
            }
        }

        Button readyButton = readyButtonObject.GetComponent<Button>();
        Button hostStartButton = hostStartButtonObject.GetComponent<Button>();

        if(!isInList)
        {
            readyButton.interactable = false;
            hostStartButton.interactable = false;

            return;
        }


        if(!localMember.IsLeader)
        {
            readyButton.interactable = true;
            readyButtonText.text = localMember.IsReady ? "已准备" : "准备";

            return;
        }

        // 房主无需准备；空槽没有成员记录，不参与检查。
        foreach(LobbyMember member in members)
        {
            if(member.IsLeader)
            {
                continue;
            }

            if(!member.IsReady)
            {
                hostStartButton.interactable = false;
                hostStartButtonText.text = "有玩家未准备";

                return;
            }
        }

        hostStartButton.interactable = true;
        hostStartButtonText.text = "开始游戏";
    }

    // 隐藏房间页面，再交给 PrototypeMenus 开启本机游戏控制。
    public void ShowGame()
    {
        if(roomListPanel != null)
        {
            roomListPanel.SetActive(false);
        }

        HideRoundResult();
        mainManu.SetActive(false);
        createRoomPanel.SetActive(false);
        lobbyPanel.SetActive(false);
        prototypeMenus.StartGame();
    }

    // 只展示服务器确认的结果，同一结果的重复快照不会反复切换界面。
    public void ShowRoundResult(RoundResult result)
    {
        if(roomListPanel != null)
        {
            roomListPanel.SetActive(false);
        }

        if(result != RoundResult.Victory && result != RoundResult.Defeat)
        {
            return;
        }

        if(shownResult == result && (resultView != null
                    ? resultView.IsShowing : resultPanel != null && resultPanel.activeSelf))
        {
            return;
        }

        shownResult = result;
        mainManu.SetActive(false);
        createRoomPanel.SetActive(false);
        lobbyPanel.SetActive(false);
        prototypeMenus.ShowRoundResult();
        if(resultPanel == null || resultTitleText == null || resultDescriptionText == null)
        {
            Debug.LogError("[RoomMenuController] 请配置结算面板和文字。", this);
            return;
        }
        resultTitleText.text = result == RoundResult.Victory ? "逃生成功" : "全员阵亡";
        resultTitleText.color = result == RoundResult.Victory
            ? new Color(0.88f, 0.81f, 0.57f, 1f) : new Color(0.8f, 0.67f, 0.63f, 1f);
        resultDescriptionText.text = result == RoundResult.Victory
            ? "所有存活玩家已抵达出口。" : "没有玩家生还。";
        if(resultView != null)
        {
            resultView.Show();
        }
        else
        {
            resultPanel.SetActive(true);
            if(EventSystem.current != null && resultLeaveButton != null)
            {
                EventSystem.current.SetSelectedGameObject(resultLeaveButton.gameObject);
            }
        }
    }

    public void ShowRoomList()
    {
        HideRoundResult();
        mainManu.SetActive(false);
        createRoomPanel.SetActive(false);
        lobbyPanel.SetActive(false);
        if(roomListPanel != null)
        {
            roomListPanel.SetActive(true);
        }
    }

    public void SetRoomName(string name)
    {
        if(roomNameText != null)
        {
            roomNameText.text = name;
        }
    }

    // 本机死亡花屏可暂缓结算展示，完成后从黑屏继续，不改变服务器结果。
    public void SetRoundResultPresentationBlocked(bool blocked, bool alreadyBlack = false)
    {
        if(resultView != null)
        {
            resultView.SetBlocked(blocked, alreadyBlack);
        }
    }

    // 退出会话后清除上局界面，避免下一次加入沿用结果。
    private void HideRoundResult()
    {
        if(resultView != null)
        {
            resultView.Cancel();
        }
        else if(resultPanel != null)
        {
            resultPanel.SetActive(false);
        }

        shownResult = RoundResult.None;
    }
}
