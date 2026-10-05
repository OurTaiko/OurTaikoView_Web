# OurTaikoView_Web

独立的 Unity WebAssembly 谱面播放器，来源于相邻 OurTaikoPlay 的 PracticeScene。Unity 6000.3.25f1，URP 17.3.0。原项目不受修改。

## 模式

- `practice: true, autoPlay: false, replay: false`：键盘和鼓面练习。
- `practice: true, autoPlay: true, replay: false`：自动击打观看，保留跳小节和变速。
- 两种模式结束后均回到第一小节暂停。F/J 咚，D/K 咔，空格暂停。
- `replay: true` 当前明确返回 `REPLAY_NOT_SUPPORTED`；尚未实现 inputs 回放。
- 不登录、不保存或上传成绩。浏览器通过 Web Audio 解码原始音频，再生成完整 PCM AudioClip，播放后端为 Unity；变速会改变音高。

## 构建与前端安装

```sh
unity run . --timeout 1800 -- -buildTarget WebGL -executeMethod OurTaiko.Editor.WebViewBuild.Build -logFile Builds/build.log
sh scripts/install-player.sh ../Fanmade/frontend
```

产物为 `Builds/Web` 整个目录，构建报告 `Builds/report.json`。构建工具通过 Editor API 清理非 PracticeScene 的场景及无关美术依赖，保留场景序列化引用。

Fanmade 正式构建由 `player-build.json` 固定本仓库 GitHub Release 的版本、下载地址及 SHA-256；构建时自动下载和校验，部署至同源 `/player/<version>/index.html`。Unity 构建产物通过 Release 分发，不提交到 Git 源码历史。

本地未发布构建可使用上面的安装脚本复制到前端 `public/player`，然后设置 `VITE_PLAYER_URL=/player/index.html` 启动开发服务器。也可用该变量指向独立静态播放器地址，并为资源地址配置播放器来源的 CORS。

## 通信协议 v1

iframe URL 的 `parentOrigin` 参数指定允许发送命令的宿主来源，默认与播放器同源。双方必须验证 origin 和 window source，并使用精确 targetOrigin。

```json
{"channel":"ourtaiko-view","version":1,"type":"load","requestId":"unique-id","payload":{"chartText":"TITLE:...","audioUrl":"https://example.com/audio.ogg","audioType":"ogg","course":"Oni","branch":"normal","practice":true,"autoPlay":false,"replay":false}}
```

`audioType` 支持 `ogg/mp3/wav`；URL 无扩展名时必传（例如 Fanmade 的 `/audio`）。

`chartText` 可替换为 UTF-8 `chartUrl`。显式 audioUrl 覆盖 TJA 的 WAVE 名称。Fanmade 使用自身已解码的 TJA 文本，兼容已有编码。

等待 `ready` 后发送 `load`，`loaded` 后需点击播放器内的开始按钮解锁声音。命令有 `hello/load/start/resume/pause/restart/unload/getState`。事件有 `ready/loading/loaded/finished/error/exit`，`getState` 返回 `state`（暂停、模式、谱面时间、当前/首小节位置、速度、分支与计数），异步事件带 requestId；finished 后重置并暂停。错误中的 code 可供宿主展示或重试。

## 来源

美术和玩法来自 OurTaikoPlay 的已有实现；参考项目 OurTaikoPlayer 为模拟器，并非原版游戏。资源权利归原权利人，见 LICENSE、NOTICE 和 Documentation/ImportedAssets.json；复制依赖所带许可证仍保留。ManagedBass 在此为独立复制的依赖，Web 不加载其原生库。

分支通过 `branch: normal/expert/master` 固定选择普通／玄人／达人（默认普通），支持 p/r/s 三种分支条件的谱面，但不根据成绩动态切换路线。切换路线重新加载并回到第一小节暂停。局部分支缺少玄人时沿用普通，缺少达人时沿用玄人，与 Fanmade 谱面图片一致。BMSCROLL/HBSCROLL 等仍未实现的指令会明确报错，不静默改变谱面。
