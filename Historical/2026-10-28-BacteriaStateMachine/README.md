# 细菌 AI：enum + switch 历史版本

[阅读 NetworkBacteria.cs](NetworkBacteria.cs)

这份代码来自 VS Code 于 **2026 年 10 月 28 日 15:27（北京时间）** 保存的本地历史记录，是作者参与编写、调试过程中的开发版本。上传时直接复制历史文件，未用当前游戏版本覆盖或重写其逻辑。

## 阅读重点

- `BacteriaStatus` 定义 `Patrol`、`Chase`、`Execute`；`Update()` 使用 `switch(status)` 调度行为。当时 `Execute` 只是预留状态，没有处决实现。
- 感知检测按时间间隔执行：检查玩家网络对象、距离、前方夹角及射线遮挡。
- `visibleClientIds` 记录全部可见玩家，`target` 记录唯一追击目标。
- `EnterChase()` / `EnterPatrol()` 集中切换状态和重置相关记录。
- 追击使用 `NavMeshAgent.CalculatePath()` 和 `SetPath()`，连续失去视野后恢复巡逻。
- 巡逻先生成随机候选点，再用 `NavMeshQueryFilter`、`NavMesh.SamplePosition()` 和完整路径检查筛选可达位置；到达后等待，选点失败则延后重试。

## 历史版本的边界

本例是作者参与实现的历史快照，并不表示每行代码均独立完成；开发中包含 AI 对任务、API、注释及问题排查的辅助。

代码保留当时的已知不足，例如：感知扫描将最后一个可见玩家作为候选目标，没有保持当前可见目标的优先级；巡逻速度在后续更新中设置；部分提前返回分支没有清理动画和感知状态。头注释及方法注释也可能尚未随实现完全更新。后续完整项目已继续修改，勿将本例视作最终修复版本。

代码依赖完整项目中的 `ServerLobby`、`PlayerNetworkIdentity` 和 `PlayerMotor` 等类型，以及 NGO、AI Navigation、NavMesh、Animator 和 Inspector 引用。本仓库没有包含全部依赖，也未对这个历史快照重新做运行验收。

为与当前客户端节选区分，本文件放在 `Historical/`，不在 Unity 的 `Assets/` 编译目录中；当前游戏工程未被修改。
