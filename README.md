# Re: Escape the Backrooms — 客户端核心代码

基于 Unity 6 制作的第一人称多人合作恐怖游戏。本仓库选取开发过程中我主要学习、编写与调试的客户端模块，供实习求职时阅读代码使用。

这是完整游戏的源码节选，不包含场景、角色模型、音频、动画片段和完整网络系统，不能直接作为完整 Unity 工程运行。动画控制器保留状态机和参数配置，所引用的动画资源未随仓库分发。

## 推荐阅读顺序

所有路径均以 `Assets/_Game/Runtime/` 为起点。

| 模块 | 文件 | 可以关注的实现 |
| --- | --- | --- |
| 输入 | [PlayerInputReader](Assets/_Game/Runtime/Input/PlayerInputReader.cs) | 缓存 InputAction，区分持续按住与本帧按下，将输入读取集中提供给其他组件 |
| 视角 | [CameraController](Assets/_Game/Runtime/Camera/CameraController.cs) | 玩家根节点控制水平转向，相机控制俯仰，限制角度，外部镜头控制结束后同步角度缓存 |
| 移动 | [PlayerMotor](Assets/_Game/Runtime/Player/PlayerMotor.cs) | CharacterController 位移、相机相对移动、跳跃与重力、实际水平速度与出生重置 |
| 体力 | [PlayerStamina](Assets/_Game/Runtime/Player/PlayerStamina.cs) | 冲刺消耗与恢复、重新启动冲刺阈值、持续按住但不能冲刺时恢复体力、追击状态接口 |
| 动画 | [PlayerAnimationDriver](Assets/_Game/Runtime/Player/PlayerAnimationDriver.cs) | 实际速度转角色局部速度，第一/第三人称 Animator 参数驱动、阻尼、参数复制与死亡清理 |
| 交互 | [PlayerInteraction](Assets/_Game/Runtime/Player/PlayerInteraction.cs) | 范围收集、HashSet 去重、屏幕范围和遮挡检测、准星高亮、交互执行及停用清理 |
| 交互契约 | [IInteractable](Assets/_Game/Runtime/Interaction/IInteractable.cs) | 用接口统一交互点、提示与行为入口 |
| 提示表现 | [InteractionPromptUI](Assets/_Game/Runtime/Interaction/InteractionPromptUI.cs) | 世界坐标投影、距离缩放、高亮与按键反馈，与交互判定分离 |
| 音效 | [FootstepAudio](Assets/_Game/Runtime/Audio/FootstepAudio.cs) | 多片段随机播放，通过下标映射避免连续重复，单片段和空配置处理 |
| 大厅数据 | [LobbyMember](Assets/_Game/Runtime/Network/LobbyMember.cs) | 保存成员编号、房主身份与服务器确认的准备状态 |
| 大厅表现 | [LobbyMemberRow](Assets/_Game/Runtime/UI/LobbyMemberRow.cs) | 成员数据映射到单行 UI、本机高亮、房主状态与空槽恢复 |

## 模块协作

```text
PlayerInputReader ──→ CameraController
        │
        ├──────────→ PlayerMotor ←── PlayerStamina
        │                 │
        │                 └──────→ PlayerAnimationDriver
        │
        └──────────→ PlayerInteraction
                              │
                       IInteractable
                              │
                     InteractionPromptUI

服务器确认的成员数据 → LobbyMember → LobbyMemberRow
移动距离驱动（完整项目中） → FootstepAudio
```

客户端本地角色负责输入和第一人称表现；完整项目通过另一个网络玩家对象同步角色位姿与状态。本仓库聚焦本地客户端实现，不包含服务器部署、房间目录和网络同步的全部依赖。

## 配置与实际规则

- `PlayerControls.inputactions`：输入 Action 配置。PlayerInput 使用此资源，Action 名称须与 `PlayerInputReader` 对应。
- `Animation/Player/`：第一、第三人称动画控制器。驱动参数为 `MoveX`、`MoveZ`、`Grounded`、`VerticalSpeed`。
- 移动与动画参数需要在 Inspector 绑定相机、Animator 等引用；移动对象需同时配置 CharacterController、PlayerInputReader 和 PlayerStamina。
- 当前代码中的冲刺重新启动阈值为 **80%**；消耗/恢复时长是可序列化字段，实际场景可覆盖脚本默认值。被追击时允许持续冲刺，追击结束后体力回满。
- 未公开完整场景，因此仓库中的默认参数不代表最终实机的全部配置。

## 开发环境

原项目使用 Unity **6000.5.6f1**、Input System **1.20.0**、UGUI **2.5.0**；完整游戏还使用 Netcode for GameObjects **2.13.3**、AI Navigation **2.0.14** 和 URP **17.5.0**。这些版本是项目使用记录；本仓库不安装依赖或提供一键运行场景。

## 实现与协作说明

以上模块是我在项目中重点动手实现、反复修改和验证的学习实践内容。开发时使用 AI 辅助梳理任务、解释 API、检查错误，以及补充部分边界处理和注释。仓库保留最终版本，因此不能把每一行都视为我独立编写的原始代码，也没有将 AI 辅助开发部分标为独立完成。

完整项目中的服务器连接与部署、部分网络同步、UI 制作及后期修复有较多 AI 辅助，这些内容没有作为本仓库的主要展示范围。
