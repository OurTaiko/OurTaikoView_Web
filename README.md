# OurTaikoView_Web

独立的 Unity WebAssembly 谱面播放器，来源于相邻 OurTaikoPlay 的 PracticeScene。Unity 6000.3.25f1，URP 17.3.0。原项目不受修改。

## 模式

- `practice: true, autoPlay: false, replay: false`：键盘和鼓面练习。
- `practice: true, autoPlay: true, replay: false`：自动击打观看，保留跳小节和变速。
- 两种模式结束后均回到第一小节暂停。F/J 咚，D/K 咔，空格暂停。
- `replay: true` 当前明确返回 `REPLAY_NOT_SUPPORTED`；尚未实现 inputs 回放。
- 不登录、不保存或上传成绩。音频直接走浏览器 Web Audio（`latencyHint: interactive`）：歌曲与内置音效由 decodeAudioData 解码后留在 JS 侧，按 AudioContext 时钟精确排程；不做输出延迟补偿，音乐与打击音共享设备延迟。Web 构建禁用 Unity 音频（不创建 Unity 的 AudioContext），浏览器不支持 Web Audio 时 load 返回 `AUDIO_UNAVAILABLE`。变速使用 playbackRate，音高随速度改变。

## 构建与前端安装

```sh
unity run . --timeout 1800 -- -buildTarget WebGL -executeMethod OurTaiko.Editor.WebViewBuild.Build -logFile Builds/build.log
sh scripts/install-player.sh ../Fanmade/frontend
```

产物为 `Builds/Web` 整个目录，构建报告 `Builds/report.json`。构建工具通过 Editor API 清理非 PracticeScene 的场景及无关美术依赖，保留场景序列化引用。

Fanmade 直接将构建产物保存于 `public/player/<内容哈希>/`，`.unityweb` 大文件使用 Git LFS。安装脚本同时更新前端 `public/player-build.json`，固定入口和各文件 SHA-256；前端构建校验文件完整性，避免发布 LFS 指针。内容哈希目录避免浏览器混用不同版本的缓存。

更新播放器：构建 Web → 运行安装脚本 → 在前端提交新目录及 player-build.json → 推送。前端 CI 和服务器均须安装 Git LFS 并拉取对象。也可用 `VITE_PLAYER_URL` 指向独立地址，跨域时需配置资源 CORS。

## 通信协议 v1

iframe URL 的 `parentOrigin` 参数指定允许发送命令的宿主来源，默认与播放器同源。双方必须验证 origin 和 window source，并使用精确 targetOrigin。

```json
{"channel":"ourtaiko-view","version":1,"type":"load","requestId":"unique-id","payload":{"chartText":"TITLE:...","audioUrl":"https://example.com/audio.ogg","audioType":"ogg","course":"Oni","branch":"normal","practice":true,"autoPlay":false,"replay":false}}
```

`audioType` 支持 `ogg/mp3/wav`；URL 无扩展名时必传（例如 Fanmade 的 `/audio`）。

`chartText` 可替换为 UTF-8 `chartUrl`。显式 audioUrl 覆盖 TJA 的 WAVE 名称。Fanmade 使用自身已解码的 TJA 文本，兼容已有编码。

等待 `ready` 后发送 `load`，`loaded` 后需点击播放器内的开始按钮解锁声音。命令有 `hello/load/start/resume/pause/restart/unload/getState/setDrumVolume`。`setDrumVolume` 的 payload 为 `{"volume":0-100}`（默认 100，越界返回 `INVALID_VOLUME`），设置打击音（Drum 组）音量，`ready` 后即可发送、无需已加载谱面，正在发声的打击音也立即变化；宿主应在每次 `ready` 后重发当前值。事件有 `ready/loading/loaded/finished/error/exit`，`getState` 返回 `state`（暂停、模式、谱面时间、当前/首小节位置、速度、分支、计数与 `drumVolume`），异步事件带 requestId；finished 后重置并暂停。错误中的 code 可供宿主展示或重试。

## 来源

美术和玩法来自 OurTaikoPlay 的已有实现；参考项目 OurTaikoPlayer 为模拟器，并非原版游戏。资源权利归原权利人，见 LICENSE、NOTICE 和 Documentation/ImportedAssets.json；复制依赖所带许可证仍保留。ManagedBass 在此为独立复制的依赖，Web 不加载其原生库。

分支通过 `branch: normal/expert/master` 固定选择普通／玄人／达人（默认普通），支持 p/r/s 三种分支条件的谱面，但不根据成绩动态切换路线。切换路线重新加载并回到第一小节暂停。局部分支缺少玄人时沿用普通，缺少达人时沿用玄人，与 Fanmade 谱面图片一致。BMSCROLL/HBSCROLL 等仍未实现的指令会明确报错，不静默改变谱面。
