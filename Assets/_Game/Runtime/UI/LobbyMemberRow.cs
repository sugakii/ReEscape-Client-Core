using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 显示大厅中一位成员的编号、名称、身份和准备状态，并用颜色区分本机玩家。
/// 本类只刷新一行；整表分配和人数统计由 RoomMenuController 负责。
/// </summary>
public class LobbyMemberRow : MonoBehaviour
{
    [SerializeField]
    // 固定槽号填 1～4；数组下标才是 0～3。
    private int slotNumber;

    [SerializeField]
    private Text playerNumberText;

    [SerializeField]
    private Text playerNameText;

    [SerializeField]
    private Text roleText;

    [SerializeField]
    // 显示服务器确认的准备状态；房主显示无需准备。
    private Text readyStatusText;

    [SerializeField]
    private Color normalNameColor = new Color(0.94f, 0.94f, 0.90f, 1f);

    [SerializeField]
    private Color localNameColor = new Color(0.90f, 0.82f, 0.56f, 1f);

    // 显示一位成员，区分房主和本机玩家颜色。
    public void SetMember(LobbyMember member, bool isLocalPlayer)
    {
        playerNumberText.text = member.PlayerNumber.ToString();
        playerNameText.text = $"玩家{member.PlayerNumber}";
        roleText.text = "";
        readyStatusText.text = "未准备";
        playerNameText.color = normalNameColor;

        if(member.IsReady)
        {
            readyStatusText.text = "已准备";
        }

        if(member.IsLeader)
        {
            roleText.text = "房主";
            readyStatusText.text = "无需准备";
        }

        if(isLocalPlayer)
        {
            playerNameText.color = localNameColor;
        }
    }

    // 恢复本行空位显示，不修改网络名单。
    public void ClearSlot()
    {
        playerNumberText.text = slotNumber.ToString();
        playerNameText.text = "等待玩家加入...";
        roleText.text = "";
        readyStatusText.text = "空位";
        playerNameText.color = normalNameColor;
    }
}
