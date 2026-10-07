# OurTaikoView_Web

独立 Unity 6000.3.25f1 Web 播放器。仅 PracticeScene 为构建场景；场景、美术与共享玩法代码复制自相邻 OurTaikoPlay。禁止修改来源项目来修复本项目。

- Unity 场景/资源修改通过 Editor API；不要手改 YAML 或 GUID。
- `OurTaiko.Editor.WebViewBuild.Build` 构建 Web，`scripts/install-player.sh` 将产物复制到 Fanmade 前端。
- `WebPlayerBridge` 是 postMessage 到 Unity 的协议入口；版本 1，模式 bool：practice / autoPlay / replay。
- 首版支持玩家练习与自动观看；回放明确拒绝，不伪装成自动演奏。
- 两种模式结束均回第一小节并暂停。保留每帧仅最早一次输入以及帧统一判定时刻。
- 玩家信息和设置使用不落盘的配置；不启动 OnlineManager，不保存或上传成绩。
- 浏览器音频走 Web Audio 后端（`AudioBackend.WebAudio`，`Web/WebAudio.cs` + jslib），沿用 AudioBus/NativeAudioSample 抽象。`WebViewBuild` 构建期间关闭 Unity 音频（`m_DisableAudio`，构建后恢复），Web 端只有自己的一个 AudioContext、无 Unity 回退；Unity 音频只用于 Editor 播放。内置音效靠构建时生成的 NativeAudioCatalog 提供原始编码字节。无扩展名音频 URL 必须传 audioType。
- 资源许可证和来源归属必须保留。ManagedBass 是复制依赖，Web 不加载原生音频库。
- 改动协议后同步检查 Fanmade/frontend 的 embedded-player-protocol.ts 和实际 iframe e2e 测试。
- 2026-10-05 从 OurTaikoPlay `3f93225`、`c3a30c4` 同步音符可见区间（`Core/LaneWindow.cs`）、判定游标与只读判定状态，保留 ForcedBranch 与 Bridge 差异；设计与验证见 OurTaikoPlay AGENTS.md「音符可见区间与判定游标」。本项目无测试程序集，同步时用临时测试对照本项目原 PlaySession（含三种强制分支）23/23 通过后删除。
- 2026-10-05 解析与分支选择分离（同 OurTaikoPlay「练习分支：固定路线」）：`TjaParser.Parse` 不再接收分支，PlaySession 以固定路线游玩。路线只在游戏内练习菜单选择：load 载荷不再有 `branch`（旧宿主多传会被忽略），模板「开始」只关遮罩并停在游戏内菜单，不再发送 start；`start`／`resume` 仍是程序化开始播放的命令。Bridge 用普通譜面固定路线 PlaySession 验证谱面；`getState` 的 `forcedBranch` 为当前路线，新增 `stage`（Branch／Measure／Speed）。
- 2026-10-06 从 OurTaikoPlay `2834759`（单次加分数字：最终高度横向滑入、平齐一行、淡出上移 15 px，删除 `fanStep`）与 `e754b76`（轨道难度图标按谱面 COURSE 切换，`PlayScene.laneDifficulty`／`laneDifficultySprites`＝`lane_difficulty` 切片 `LaneDifficulty0–4`）同步。`ScoreAdditionView.cs`、`ScoreAddition.anim`、`lane_difficulty.png.meta` 原样复制（本项目原文件与来源父提交逐字相同）；PlayScene 只合入新增字段与 `ShowDifficulty`，保留 Bridge 差异；PracticeScene 绑定经 Editor API 完成，场景差异与来源一致。未构建、未安装到 Fanmade 前端。
