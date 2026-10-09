# S3 / CloudFront 播放器发布

Bucket：`ourtaiko-play-tokyo`。CDN：`https://d2mguycu233w0q.cloudfront.net`。

完整播放器存放在 `player/<内容哈希>/`，根目录的 `player-build.json` 指向当前版本。
Fanmade 生产配置通过 `VITE_PLAYER_MANIFEST_URL` 在每次打开播放器时读取清单，解析到清单所在 CDN 的版本目录。
网站只需部署这次接入；后续保持当前音频传输协议的播放器更新仅发布 S3。正在游玩的会话不自动重载。

## 一次性配置

- 私有 S3，通过 CloudFront OAC 读取。
- CloudFront `/player-build.json` behavior：`CachingDisabled`，`SimpleCORS`，HTTPS。
- 默认 behavior 缓存版本目录；不添加阻止 Fanmade iframe 的 `X-Frame-Options`。
- 音乐由 Fanmade 父页面同源下载，再通过 postMessage 转移 ArrayBuffer 给 CDN iframe，在播放器 JavaScript 内用 Web Audio 解码。谱面直接传文本；播放器不下载谱面或音乐 URL，不需要给后端新增 CloudFront CORS。清单仍需 SimpleCORS。
- Bridge 不带协议版本字段，只接受谱面文本和音频字节，不支持音频 URL 或固定播放器地址回退。关闭播放器、重试或切换谱面时取消旧下载。
- 安装 AWS CLI v2，使用已有身份/命名 profile，上传身份需要此桶 `player/*` 和 `player-build.json` 的 `s3:PutObject`。不把密钥写入脚本或仓库。

## GitHub Actions 自动构建与发布（推荐）

工作流：`.github/workflows/publish-web.yml`。推送 `main` 或在 main 手动运行工作流后：
发布脚本测试 → Unity WebGL 构建 → 校验构建报告 → 保存 GitHub artifact → 使用 AWS OIDC 临时身份上传 → CDN 校验 → 更新清单。
Unity 版本自动读取 `ProjectSettings/ProjectVersion.txt`，当前为 6000.3.25f1。
工作流串行发布；旧提交重跑时，如果 main 已有新提交则跳过上传。
只在 publish job 获取 AWS 权限，不把 AWS 凭据传给 Unity 构建容器。

### 1. AWS IAM 添加 GitHub 身份提供商

IAM → Identity providers → Add provider：

- Type：OpenID Connect
- Provider URL：`https://token.actions.githubusercontent.com`
- Audience：`sts.amazonaws.com`

如果这个提供商已经存在，直接复用。

### 2. 创建上传策略和角色

IAM → Policies → Create policy → JSON：使用 [上传策略](aws-player-upload-policy.json)，名称例如 `OurTaikoViewWebUpload`。
策略允许此桶的版本目录和清单写入，以及失败的分段上传清理，不允许删除文件、改桶策略或写入其他桶。
当前配置按 S3 默认 SSE-S3 加密；如果桶要求 SSE-KMS，还需单独配置相应 KMS key 权限。

IAM → Roles → Create role → Web identity，选择上面的 GitHub provider 和 audience。
限定 Organization `OurTaiko`、Repository `OurTaikoView_Web`、Branch `main`。
绑定刚创建的上传策略，角色名例如 `OurTaikoViewWebPublisher`。
核对最终 Trust relationships 与 [信任策略](aws-github-trust-policy.json) 一致；文件中的 `<AWS_ACCOUNT_ID>` 换成自己的 AWS 账户 ID。
当前工作流没有 GitHub environment，信任条件使用分支 subject，不能换成 environment subject。
本仓库已启用 immutable subject，必须保留组织与仓库的数字 ID：
`repo:OurTaiko@252237704/OurTaikoView_Web@1406471985:ref:refs/heads/main`。
只有名称、不含 `@ID` 的旧格式会导致 `sts:AssumeRoleWithWebIdentity` 被拒绝。
可以用 `gh api repos/OurTaiko/OurTaikoView_Web/actions/oidc/customization/sub` 核对当前 `sub_claim_prefix`。
若 Unity 构建已成功而 AWS 授权失败，修正 IAM 信任策略后在原运行选择 **Re-run failed jobs**，可复用已保存的构建产物。

### 3. GitHub 配置

打开 `OurTaiko/OurTaikoView_Web` → Settings → Secrets and variables → Actions。

Secrets（Personal license，与 OurTaikoPlay 的 GameCI 配置相同）：

- `UNITY_LICENSE`：已有 `.ulf` 文件的内容。
- `UNITY_EMAIL`：对应 Unity 账号邮箱。
- `UNITY_PASSWORD`：对应 Unity 账号密码。

可以使用允许本仓库访问的组织 secrets。OurTaikoPlay 的 repository secrets 不会自动共享，也不能从 GitHub 读回原值。
变量放在 Variables 页：`AWS_ROLE_ARN`，值为步骤 2 创建角色的 ARN。
不需要 `AWS_ACCESS_KEY_ID` 或 `AWS_SECRET_ACCESS_KEY`，本机也不需要安装 AWS CLI。

### 4. 首次运行

确保 CloudFront 清单 behavior 已部署，然后将工作流及发布脚本提交/推送到 main。
在 Actions 查看 `Build and publish Web player`；如需重试，选择 main 后 Run workflow。
首次切换音频字节传输时，先发布新版播放器并确认云端清单已指向它，再部署新版 Fanmade 前端，并完成真实浏览器验收；两端都必须使用音频字节传输。此前后端 CORS 方案已撤销，无需部署后端。
后续播放器 Bridge 协议兼容的更新只需推送 View_Web main，不需要更新 Fanmade。

参考：[GameCI Activation](https://game.ci/docs/github/activation/)、[GitHub AWS OIDC](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-aws)。

## 本地手动发布（可选）

1. 使用 `OurTaiko.Editor.WebViewBuild.Build` 或 `QueueBuild` 重新生成 `Builds/Web`。脚本只发布现有产物，不自动构建，也不判断源码是否比产物更新。
2. 预览：`python3 scripts/publish-player.py`。
3. 发布：`python3 scripts/publish-player.py --publish`，需要指定已有 profile 时追加 `--profile <名称>`。

脚本复制构建快照，检查 HTML 引用的文件，计算 SHA-256 和版本号，设置 Content-Type、Gzip Content-Encoding 和一年 immutable 缓存。
上传所有文件后逐个通过 CloudFront 检查响应类型、压缩信息与字节哈希；全部成功后才覆盖 `player-build.json`，清单设置 `Cache-Control: no-store`。
上传/校验失败时旧清单不变；不删除旧版本、不修改桶策略、不运行 CloudFront invalidation。

首次发布后检查清单跨域响应头，并在 Fanmade 验证自动演奏、练习、音乐、全屏与卸载。
清单更新不需要刷新缓存，前提是对应 behavior 已部署为 CachingDisabled。
回滚时将之前保留的清单重新上传到同一 key，并继续设置 `application/json` 和 `no-store`。

## 本地测试

`python3 -m unittest discover -s scripts -p 'test_publish_player.py'`

Fanmade 开发和生产均通过 CloudFront 清单加载。前端已移除 `public/player/`、`public/player-build.json`、本地构建校验脚本及播放器 LFS 配置，并清理对应 Git 历史。Vite 使用普通 public 复制流程，无需为播放器增加过滤插件。
本地构建的浏览器测试可通过 `PLAYER_TEST_BUILD_DIR` 注入临时 CDN 响应；这仅是测试夹具，不向前端仓库安装构建产物。
