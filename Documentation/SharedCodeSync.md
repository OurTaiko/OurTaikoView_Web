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
