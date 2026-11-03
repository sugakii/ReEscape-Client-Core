# Re: Escape the Backrooms — 游戏源码阅读版

基于 Unity 6 / C# / Netcode for GameObjects 制作的第一人称多人合作恐怖游戏，支持最多 4 名玩家联机，包含探索寻钥、出口交互、敌人巡逻追击、处决、死亡观战和胜负结算。

[实机演示视频](https://www.bilibili.com/video/BV1rTxP65EAf/)

本仓库最初只公开部分客户端模块；现已同步项目的全部自编游戏脚本，包含客户端功能、网络会话与服务器逻辑、构建工具和检查脚本。代码使用四空格缩进，控制语句使用大括号，序列化字段特性单独成行，并保留简短的职责与流程注释。

这是用于学习和求职展示的**源码阅读版本**。仓库没有包含完整游戏场景、Prefab、模型、贴图、音频、视频和动画片段，不能直接当作完整 Unity 工程打开试玩。动画控制器与代码引用的场景和资源路径是原工程配置，相关资源并未全部公开。

## 推荐阅读顺序

下表中的脚本路径以 `Assets/_Game/Runtime/` 为起点。

| 模块 | 主要脚本 | 内容 |
| --- | --- | --- |
| 输入与视角 | [PlayerInputReader](Assets/_Game/Runtime/Input/PlayerInputReader.cs)、[CameraController](Assets/_Game/Runtime/Camera/CameraController.cs) | 输入读取、第一人称水平转向与俯仰、灵敏度设置 |
| 移动与体力 | [PlayerMotor](Assets/_Game/Runtime/Player/PlayerMotor.cs)、[PlayerStamina](Assets/_Game/Runtime/Player/PlayerStamina.cs) | 行走、冲刺、跳跃、重力、体力消耗与恢复、被追击状态 |
| 角色动画 | [PlayerAnimationDriver](Assets/_Game/Runtime/Player/PlayerAnimationDriver.cs) | 根据实际移动速度驱动第一、第三人称动画，并复制参数给网络角色 |
| 交互与提示 | [PlayerInteraction](Assets/_Game/Runtime/Player/PlayerInteraction.cs)、[IInteractable](Assets/_Game/Runtime/Interaction/IInteractable.cs)、[InteractionPromptUI](Assets/_Game/Runtime/Interaction/InteractionPromptUI.cs) | 范围查询、目标去重、遮挡检测、屏幕提示、高亮与交互入口 |
| 大厅与连接 | [NetworkSession](Assets/_Game/Runtime/Network/NetworkSession.cs)、[ClientLobby](Assets/_Game/Runtime/Network/ClientLobby.cs)、[ServerLobby](Assets/_Game/Runtime/Network/ServerLobby.cs) | 会话生命周期、成员快照、准备状态、连接审批和游戏阶段 |
| 房间列表 | [RoomBrowserController](Assets/_Game/Runtime/UI/RoomBrowserController.cs)、[ServerRoomDirectory](Assets/_Game/Runtime/Network/ServerRoomDirectory.cs)、[RoomDirectoryProtocol](Assets/_Game/Runtime/Network/RoomDirectoryProtocol.cs) | 查询、创建和加入单个服务器房间，显示房名与人数 |
| 本机与网络角色 | [LocalPlayerNetworkBridge](Assets/_Game/Runtime/Network/LocalPlayerNetworkBridge.cs)、[PlayerNetworkIdentity](Assets/_Game/Runtime/Network/PlayerNetworkIdentity.cs)、[PlayerNetworkView](Assets/_Game/Runtime/Network/PlayerNetworkView.cs) | 出生定位、位姿和动画同步、身体显示、第一人称观战相机数据 |
| 生成与网络交互 | [ServerWorldSpawner](Assets/_Game/Runtime/Network/ServerWorldSpawner.cs)、[RandomPointNetworkSpawner](Assets/_Game/Runtime/Network/RandomPointNetworkSpawner.cs)、[NetworkKey](Assets/_Game/Runtime/Network/NetworkKey.cs)、[NetworkDoor](Assets/_Game/Runtime/Network/NetworkDoor.cs) | 本局物体生成、随机候选点、钥匙拾取与团队开门条件 |
| 细菌 AI | [NetworkBacteria](Assets/_Game/Runtime/Network/NetworkBacteria.cs) | `enum + switch` 管理巡逻、追击和处决；视野、遮挡与 NavMesh 路径检测 |
| 生命与处决 | [NetworkPlayerLife](Assets/_Game/Runtime/Network/NetworkPlayerLife.cs)、[PlayerExecutionState](Assets/_Game/Runtime/Network/PlayerExecutionState.cs)、[PlayerExecutionView](Assets/_Game/Runtime/Player/PlayerExecutionView.cs) | 服务器确认生命状态，统一处决时间并锁定本机视角 |
| 死亡与观战 | [LocalDeathTransition](Assets/_Game/Runtime/Player/LocalDeathTransition.cs)、[PlayerSpectator](Assets/_Game/Runtime/Player/PlayerSpectator.cs) | 受害者本机花屏转场、队友第一人称观战和左键切换目标 |
| 回合规则 | [ServerRoundRules](Assets/_Game/Runtime/Gameplay/ServerRoundRules.cs)、[ExitZone](Assets/_Game/Runtime/Gameplay/ExitZone.cs) | 存活成员全部进入出口时获胜，全员死亡时失败 |
| 菜单与设置 | [PrototypeMenus](Assets/_Game/Runtime/UI/PrototypeMenus.cs)、[RoundResultView](Assets/_Game/Runtime/UI/RoundResultView.cs)、[SettingsMenuController](Assets/_Game/Runtime/UI/SettingsMenuController.cs) | 主菜单、暂停、结算、音量与鼠标灵敏度设置及保存 |
| 声音 | [FootstepAudio](Assets/_Game/Runtime/Audio/FootstepAudio.cs)、[PlayerFootstepDriver](Assets/_Game/Runtime/Audio/PlayerFootstepDriver.cs)、[NetworkFootstepDriver](Assets/_Game/Runtime/Audio/NetworkFootstepDriver.cs)、[BacteriaChaseAudio](Assets/_Game/Runtime/Audio/BacteriaChaseAudio.cs) | 本机与远端脚步、避免连续重复片段、追击音循环与淡出 |

## 工程目录

```text
Assets/_Game/
├── Runtime/             游戏运行时代码、输入配置和动画控制器
│   ├── Audio/
│   ├── Camera/
│   ├── Gameplay/
│   ├── Input/
│   ├── Interaction/
│   ├── Network/
│   ├── Player/
│   └── UI/              包含死亡断讯效果的 Shader
├── Editor/              Windows 客户端和服务器构建菜单
└── Tests/Editor/        处决、观战、房间列表和设置的检查脚本
Tools/Network/           Windows 客户端、服务器启动脚本
Packages/               原工程依赖及版本锁定记录
ProjectSettings/        原工程 Unity 版本记录
Historical/             开发过程中的历史代码示例
```

`Tests/Editor/` 中的检查脚本依赖完整原工程的场景和组件，部分需要进入 Play Mode 后执行，不是一套可以在本源码阅读仓库中直接运行的独立单元测试。

## 游戏与连接规则

- 最多 4 名玩家；目前每个独立服务器进程维护一间房，客户端从主菜单创建或浏览房间。
- 游戏进入进行阶段后不接收新玩家，同一局死亡的玩家不复活。
- 服务器生成钥匙、门和敌人，确认交互、生命状态和回合结果；客户端负责本机输入与表现。
- 第一人称本机角色与网络玩家对象分开，由桥接组件传递位置、动画和状态。
- 当前体力代码的重新启动冲刺阈值为 80%；消耗与恢复时长可在 Inspector 覆盖。被追击时允许持续冲刺，追击结束后体力回满。
- 游戏连接默认使用 UDP 7777，房间目录使用 UDP 7778；部署时两处端口均需可访问。启动脚本可通过参数指定地址和端口，脚本需要配合完整的对应平台构建使用。

## 开发环境与验证

原工程使用 Unity **6000.5.6f1**、Netcode for GameObjects **2.13.3**、AI Navigation **2.0.14**、Input System **1.20.0**、URP **17.5.0** 和 UGUI **2.5.0**。详细依赖见 [Packages/manifest.json](Packages/manifest.json)。

完整工程开发过程中进行过多客户端联机和云服务器连接验证。本次同步包含统一排版和简短注释，已对 51 个 C# 脚本进行语法及代码内容对比，并检查编辑器、客户端和服务器条件编译分支；本次发布没有重新进行完整游戏运行验收。
