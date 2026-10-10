# Web 播放器验证 — 2026-10-10（舞者 arcade rig 本地构建）

- 来源：OurTaikoPlay `099be23`；同步提交 `cf267b2`。范围与 Web 差异见 [SharedCodeSync.md](SharedCodeSync.md)。
- Unity EditMode：162/162 通过，含 `DancerTests` 11 项。
- WebGL：Succeeded，0 errors，37,155,443 bytes（上一次为 35,494,648，增加约 1.66 MB）；`Web.data.unityweb` 21,964,318 bytes。通过 `WebViewBuild.QueueBuild()` 构建，报告 `Builds/report.json`。
- **构建裁剪修正**：`WebViewBuild.Build` 会删除场景不依赖的美术与生成资产，而没有任何资产依赖图集（Sprite 只是被它打包），所以舞者图集会在构建时被删掉，舞者退回 111 张独立帧图。现在把 `Generated` 下的 SpriteAtlas 加入保留集合。本次构建后工作区没有资产被删除。
- 本机实际 WASM（应用内置浏览器，Chromium，WebGL 2，支持 `WEBGL_compressed_texture_s3tc`，即 DXT5 直接上传的路径）：临时宿主页以 iframe 加载 `Builds/Web`，按协议发送 `load`（自编 150 BPM 18 小节 Oni 谱面＋40 秒 WAV，autoPlay）。结果：`ready → loading → loaded`；开始后 8 秒 31 良、3 名舞者在台上；30 秒 142 良 0 不可、魂槽过クリア线后 5 名舞者全部在场且动作同步；FPS 计数 119–122；控制台无错误。舞者位于 Footer 之前，脚尖略被台子遮挡，与 OurTaikoPlay 一致。
- 画质：DXT5 图集页在 Editor 中导出后按 1:1 查看，以及浏览器运行画面，描边与色块均未见块状失真。浏览器截图分辨率只有 800×600，不足以判断全尺寸下的细节。
- **未验证**：不支持 S3TC 的浏览器（iPad Safari 等）上图集解包为 RGBA 的路径，包括其内存（估算约 48 MB）与加载耗时；Safari、Firefox、真实移动设备；开场跳入与减员退场的画面；Fanmade iframe e2e（`e2e/embedded-player.spec.ts`）没有运行。没有安装到 Fanmade 前端，没有发布。
- 构建后 `ProjectSettings/AudioManager.asset` 多出 4 个由 Unity 补写的默认字段（与 `m_DisableAudio` 无关），未提交。

# Web 播放器验证 — 2026-10-09（音符表情发布）

- 来源：OurTaikoPlay `9c07e49`；更新前 Web `ad41632`。完整差异与平台边界见 [SharedCodeSync.md](SharedCodeSync.md)。
- Unity EditMode：144/144 通过，含 27 项节拍／连段表情逻辑和场景切片绑定检查；报告 `TestResults/note-expression-sync-editmode.json`。
- WebGL：Succeeded，0 errors，35,489,535 bytes；报告 `Builds/report.json`。通过 `WebViewBuild.QueueBuild()` 独立编辑器回调构建，避免 Pipeline 请求超时或临时回调失效。
- 已安装完整五文件 bundle 到 Fanmade `public/player/41f1d8cc377391c8/`，清单 `public/player-build.json`；前端构建逐一校验 SHA-256。
- 本地 Chrome 实际 WASM 回归通过，测试明确断言 iframe 路径等于当前 manifest：自动演奏、手动键盘／鼓面、音量、结束复位、三条固定分支、OGG/WAV、390px 窄屏及卸载。
- 新增阈值流程实际达到 50 和 150 连段，并保存截图；完整短谱面 151 良、0 不可，包含小／大连打和气球。测试使用业务 API 夹具，无页面错误，无 API POST。它不验证真实成绩上传。
- 前端 lint、format:check、129 项单元测试（另 1 项按条件跳过）和生产构建通过。浏览器测试文件为 `Fanmade/frontend/e2e/embedded-player.spec.ts`。
- Safari、Firefox、真实移动设备未验证。生产发布在前端 main 推送后自动执行，实际结果需另核对 Actions 和公网五文件哈希。

# Web 播放器验证 — 2026-10-09（连打与 UTF-8 同步）

- 来源：OurTaikoPlay `4d15dbb`；同步范围及保留差异见 [SharedCodeSync.md](SharedCodeSync.md)。
- EditMode 共 116 项：完整程序集运行中其余 115 项通过；预览场景的世界矩阵断言改为检查实际保存的锚点后，EmbeddedPresentationTests 的 6 项全部通过。覆盖分支、输入、动画、编码、连打计数/淡出和气球层级。
- 最终 WebGL 构建 Succeeded，0 errors，35,497,170 bytes，报告 `Builds/report.json`。初次构建的唯一错误来自同步 Pipeline 请求的 5 秒超时；改为独立的编辑器回调后重新构建，无此错误。
- Chrome 实际 WASM：自动演奏、手动键盘/鼓面、音量、结束重置、普通/玄人/达人分支、OGG/WAV、390px 布局及卸载通过；新增小/大连打与气球流程，自动演奏产生 69 次连打，截图确认扇形计数、共享数字及气球。
- 本机临时 Vite 入口直接提供 `Builds/Web`；业务 API 使用测试夹具，不写生产成绩。此次不安装到 Fanmade 前端，不发布生产 WASM；未验证 Safari、Firefox、真实移动设备。

# Web 播放器验证 — 2026-10-08（同步主仓库）

- 从 OurTaikoPlay `1183c5c` 同步共享逻辑和表现，保留嵌入协议、Web Audio 和练习结束行为；[范围与差异](SharedCodeSync.md)。
- EditMode 114/114 通过；WebGL 构建成功，0 errors，35,437,048 bytes。
- Chrome 真实 WASM 回归通过：自动演奏、手动键盘/鼓面、鼓声音量、结束重置、三条固定路线、OGG/WAV、390px 布局与卸载，无 pageerror 或 API POST。
- 使用临时入口加载本次构建；既有浏览器测试的移动布局末步临时适配为先打开“谱面”分区。未修改前端源码或安装播放器。
- 未提交、推送或部署；未验证 Safari、Firefox 与真实移动设备。

# Web 播放器验证 — 2026-10-06（Web Audio 后端）

- 音频改走 Web Audio：单个 `latencyHint: interactive` 的 AudioContext（本机 Chrome：48 kHz，baseLatency 5.3 ms，outputLatency 24 ms）。构建时禁用 Unity 音频，页面只创建这一个 AudioContext（无 Unity 的 AudioContext，也无 OfflineAudioContext）；已删除 BrowserAudioDecoder 与 PCM AudioClip 回退。
- 构建成功，0 errors，35,352,118 bytes（多出约 1.3 MB 为内置音效原始 OGG 字节）。
- 插桩页实测：音效在调用时即以 `currentTime` 起播（不再等下一帧）；歌曲按 AudioContext 时钟排程（当时含输出延迟补偿，已于同日按用户决定移除）；自动演奏 5 良；练习开始/暂停/继续/卸载后输出 RMS 分别为非零/0/非零/0。
- Fanmade iframe e2e（同源代理指向本次构建，Chrome）通过：OGG/WAV 解码、自动演奏、结束重置、练习、键盘与鼓面输入、三条固定路线、卸载。
- 尚未实测 Safari、Firefox 与移动设备。

# Web 播放器验证 — 2026-10-05

- Unity 6000.3.25f1 Web 构建成功，0 errors，34,088,692 bytes。只打包 PracticeScene，报告位于 Builds/report.json。
- Fanmade 前端单元测试 129 passed、1 skipped；类型检查、lint、格式检查和构建通过。
- Chrome 真实 WASM 回归通过（e2e/embedded-player.spec.ts）：OGG 自动演奏 8 个良；结束回到第一小节暂停并清零；切换练习、键盘和鼓面输入；切换难度；普通/玄人/达人固定路线分别 2/3/5 个良（含只写普通的局部分支）；WAV 重新加载；390px 宽度无横向溢出；离开 tab 移除 iframe；全程没有 API POST。
- 真实公开歌曲「霜降」：OGG 时长 162.688 秒成功解码，自动游玩约 8 秒时 25 良、0 不可，音频输出 RMS 0.128（非静音）。没有跑完该真实歌曲；结束行为由短谱面回归覆盖。截图 TestResults/fanmade-preview.png。
- 尚未实测 Safari、Firefox 和真实移动设备；浏览器变速使用 Unity pitch，音高随速度改变。
- 当前为本地集成和构建产物，未提交、推送或部署生产环境。前端发布必须携带完整 public/player，参见 Fanmade/frontend/deploy/PLAYER.md。

## 音频实现依据

Unity Web 的 AudioClip.Create 使用 stream=false，并一次设置完整样本；参考 [Unity Web audio documentation](https://docs.unity.com/en-us/engine/6000.0/manual/platform-specific/webgl/develop/audio)。实际 OGG 返回零长度的问题由本次浏览器运行确认；目前改为浏览器 decodeAudioData 后创建 PCM AudioClip，避免依赖下载接口的压缩音频长度。2026-10-06 起 Web Audio 后端直接播放 decodeAudioData 的 AudioBuffer，PCM AudioClip 路径已删除。
