# 练习开始前的准备时间

2026-10-05：游戏端与 Web 同步。

- 暂停浏览保留所选位置；确认播放时从更早的时间开始，至少预留 2 秒实际时间。
- 0.1×–3.0× 速度按相应谱面时间回退，准备时间不会随加速缩短；音画和判定偏移继续生效。
- 首次播放、任意小节跳转、暂停后开始、重新开始和自动观看均共用 ConfirmPractice。
- PlaySession 仍以选中位置初始化，准备阶段不会重新计入此前的音符或漏判；跳入连打中途时，手动和自动连打都必须等到所选位置才计数。
- 暂停时的小节游标、正常游玩的开场逻辑和每帧输入互斥不变。

实现：PracticeProgress.PlaybackStart、PlayScene.ConfirmPractice、PlaySession 连打判定下界。

验证：PracticeTests 9/9，PracticeFlowTests 5/5（含 BASS 和 Unity 后端），Fanmade 真实 WASM 浏览器回归通过；Web 与前端静态构建成功。
