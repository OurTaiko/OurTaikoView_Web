# 与 OurTaikoPlay 同步（2026-10-09，自动击打与分歧显示）

来源：OurTaikoPlay `c8cddb9`、`d50a506`、`a200fda`；更新前 Web 基线：`c225ec2`。

- 自动演奏从右手起打，每次实际击打后换手，普通音符与 5／6／7／9 号长音符连续交替；练习重置后恢复右手起打。
- 公共段落显示普通谱与普通底色，到分歧段实际开始时才显示所选路线；提前计算的分歧结果不改变公共段显示。
- 练习开始时根据倒退两秒后的实际播放位置立即恢复完整路线标签，跳转不重播升降级动画；真正跨越分歧边界时仍播放原动画。
- `PlaySession.cs`、`BranchLaneView.cs`、`PlayScene.Practice.cs`、`BranchRouteTests.cs` 与来源逐字一致；`PlayScene.cs` 仅合入上述改动，保留 Web Audio 枚举、Bridge 等待／挂接、结束回首小节暂停和卸载音频。
- 同步分歧逻辑测试，并增加网页 PracticeScene 中的即时标签／透明度／底色检查。来源项目的原生 PlayMode 测试依赖入口与选曲场景，不直接复制到网页。
- 验证：Unity EditMode `BranchRouteTests` 13/13 通过，报告 `TestResults/20261009-branch-sync/branch-routes.json`；四个共享文件逐字一致，`git diff --check` 通过。`EmbeddedPresentationTests` 尚未完成：编辑器在第一组测试后失去 Pipeline 连接，重启仍未恢复；新增三个场景检查不能视为已通过。
- 本次为源码同步；没有安装到 Fanmade 前端或发布生产播放器。

---

# 与 OurTaikoPlay 同步（2026-10-09，音符表情）

来源：OurTaikoPlay `9c07e49`（PR #27）；更新前 Web 基线：`ad41632`。

- 补齐 `NoteExpression`、`PlaySession`、`TaikoChart`、`TjaParser`，四个共享文件与来源逐字一致。
- 50–149 连段每八分音符切换嘴巴；达到 150 连段，从整数拍开始每十六分音符切换。阈值音符位于非整数拍时等到下一整数拍，整数拍上的阈值音符当拍开始，断连立即复位。
- 使用谱面拍数，包含 OFFSET、BPMCHANGE、DELAY 和分支；手动击打时间的早晚不改变起点。
- `SyncPlayPresentation.ApplyNoteExpressions()` 使用 Editor API 导入来源 atlas 的切片定义、保留 sprite IDs，并只给 PracticeScene 加上表情帧绑定。彩球复用原帧。
- 全部 Runtime 对比后，剩余差异仅为下方表格所列 Web Audio、双时钟、无持久化、服务启动与嵌入生命周期；桥接协议没有变化。
- Play 的三个新增未引用字体没有加入 Web；原生平台构建配置、其他场景及动态字体缓存仍保留各自版本。
- 此次发布同时包含 `ad41632` 已同步、但未安装到 Fanmade 的连打计数／气球层级／UTF-8 解析更新。

验证与发布结果见 [WebVerification.md](WebVerification.md)。

---

# 与 OurTaikoPlay 同步（2026-10-09）

来源：OurTaikoPlay `4d15dbb`（共享表现更新到 `3b50b4d`，另包含 UTF-8 下载解析改动）。更新前 Web 基线：`b7ed84a`。

- 补齐 Nijiiro 连打计数扇形面板：小/大连打按单个长音符计数，每次击打重播数字伸缩，停打后保持并淡出，练习重置时清空。
- 连打与气球共用原始 96×112 高清数字贴图及 mipmap；气球仍按 77×90 和 64 间距显示。
- 气球移到魂槽上方、暂停控件和菜单下方，保持原坐标。PracticeScene 通过 `SyncPlayPresentation.ApplyCounters()` 修改；原生 SinglePlayScene、Entry、SongSelect 和平台构建配置不加入网页。
- 同步共享 SongSearchView 的键盘收起处理、关键词编辑交互及布局逻辑；仅同步代码，网页没有新增选曲页。
- 在线模型和下载解析不再读取 encoding，严格 UTF-8 解码并拒绝损坏字节；已有 BOM 处理保留。
- 完整对比 Runtime 后，剩余差异均为下文列出的 Web Audio、双时钟、嵌入桥接、无持久化和服务启动边界。原生 iOS 导出及音频导入配置不适用于 Web。
- 原素材与动画直接复制，删除旧气球专用数字；动态字体缓存不作为本次共享更新。

验证结果见 [WebVerification.md](WebVerification.md)。本次构建保存在 `Builds/Web`；Fanmade 生产 WASM 的安装与发布属于独立前端操作，本次仅推送三个指定仓库并部署后端。

---

# 与 OurTaikoPlay 同步（2026-10-08）

来源：OurTaikoPlay `1183c5c`。更新前 Web 基线：`14ea059`。来源项目未修改。

## 已同步

- 共享分支条件：准确率 p、连打 r、分数 s，含重置、长音符得分与强制路线逻辑。
- 资源缓存：以 SHA-256 内容哈希复用 chart/audio/preview 对象，流式校验、并发落盘，TJA 在内存中处理。编辑器解码根据资源类型识别无扩展名对象。
- 自定义键位数据、输入管理、设置菜单与共享 UI 逻辑。
- 曲库扫描、SongSelectManager 与选曲视图拆分、关键词搜索、难度/星数过滤。仅同步共享代码依赖，网页没有新增选曲或设置场景。
- 50 Combo 提示及语音；100–5000 Combo 语音索引顺延。
- 良／可／不可文字与分数、Combo 使用同一伸缩曲线，底边不移动，保持不透明 250ms 后隐藏。
- 删除未使用的重复难度图标切片。资源来源记录保留并补充 50 Combo 音源。

## 保留的 Web 差异

| 模块 | 原因 |
| --- | --- |
| AudioBus、AudioEngine、AudioOptions、NativeAudioSample、SoundSettings | 浏览器使用 Web Audio，编辑器使用 Unity 音频；不采用主仓库的 BASS-only 后端与枚举改名 |
| GameTimeline、SongDefinition | AudioContext/Editor 音频时钟及浏览器解码缓冲区 |
| GameSettings、SettingManager、PlayerInfoController | 设置与玩家信息不落盘，保持 Web/Editor 后端选择 |
| OnlineManager、LocalSongLibrary、SongSelectManager | 不自动启动登录、上传队列或磁盘曲库服务 |
| SceneSwitcher、PlayScene、PlayScene.Embedded、WebPlayerBridge | 内嵌加载、控制、结束通知与卸载；结束回首小节暂停 |
| SongLoadingScene、SongSelectScene、GlobalSettingView | 编辑器音频回退及音频状态显示 |
| PracticeScene、构建配置、模板与 jslib | 只构建 PracticeScene，协议 v1 与 Web 音频解锁逻辑不变 |

普通游玩的 EndingView 代码一并同步以保持共享依赖完整；网页不会触发它，也没有打包结束演出的美术和音频。原生入口、选曲、设置、结算场景、BASS 库、平台 CI 及主仓库子模块配置不属于 Web 同步范围。动态字体图集保留 Web 版本，不复制主仓库的生成缓存。

## 重建与回归

`OurTaiko.Editor.SyncPlayPresentation.Apply` 根据本项目的 TextStretch.anim 重建判定动画，更新 PracticeScene 判定基点与语音数组，并通过 sprite data provider 清理重复切片。执行前需保存场景并退出 Play 模式；可重复执行。

编辑器回归位于 `Assets/OurTaiko/Tests`，由主仓库对应测试同步，动画场景目标调整为 PracticeScene，另包含 Web 语音绑定回归。测试程序集不进入发布构建。

## 本次验证结果

- EditMode：114/114 通过；报告 `TestResults/shared-sync-editmode.xml`。
- WebGL：成功，0 errors，35,437,048 bytes；产物 `Builds/Web`，报告 `Builds/report.json`。
- Chrome 真实 WASM：自动演奏 8 良、0 不可；结束回首小节暂停并清零；键盘和鼓面输入；鼓声音量；普通／玄人／达人路线分别 2／3／5 良；OGG/WAV 解码；390px 布局；卸载。无浏览器 pageerror，无 API POST。
- 浏览器使用现有 Fanmade `e2e/embedded-player.spec.ts` 的临时副本。测试入口由本机 Vite 中间件映射到本次构建；移动布局末步增加打开“谱面”分区，以适配当前前端。前端源码、播放器安装清单和生产环境均未修改。
- JudgmentFade.anim 和 lane_difficulty.png.meta 与来源文件逐字一致；场景保留 Web 配置，仅调整判定基点、语音数组和新增共享字段。重建可重复执行。
- 未验证 Safari、Firefox、真实移动设备；未安装到 Fanmade 前端，未提交、推送或部署。
