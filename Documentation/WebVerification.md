# Web 播放器验证 — 2026-10-06（Web Audio 后端）

- 音频改走 Web Audio：单个 `latencyHint: interactive` 的 AudioContext（本机 Chrome：48 kHz，baseLatency 5.3 ms，outputLatency 24 ms）。构建时禁用 Unity 音频，页面只创建这一个 AudioContext（无 Unity 的 AudioContext，也无 OfflineAudioContext）；已删除 BrowserAudioDecoder 与 PCM AudioClip 回退。
- 构建成功，0 errors，35,352,118 bytes（多出约 1.3 MB 为内置音效原始 OGG 字节）。
- 插桩页实测：音效在调用时即以 `currentTime` 起播（不再等下一帧）；歌曲按 AudioContext 时钟排程，并按测得输出延迟提前；自动演奏 5 良；练习开始/暂停/继续/卸载后输出 RMS 分别为非零/0/非零/0。
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
