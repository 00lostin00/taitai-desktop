# 苔苔

<img src="docs/taitai.gif" width="144" alt="苔苔待机和跳跃的动画">

一个 Windows 毛线娃娃桌宠。显示 Codex 剩余额度、重置时间和当前会话状态，也能一键回到原来的 ChatGPT 聊天。

## 运行

到 [Releases](https://github.com/00lostin00/taitai-desktop/releases) 下载压缩包，解压后打开 `Taitai.exe`。需要 Windows 11 和 .NET 8 Windows Desktop Runtime。

额度查询需要已经登录的 Codex CLI。程序会尝试自动查找；找不到时，在额度页手动选择命令行版 `codex.exe`。

默认在系统托盘保留入口。再次启动不会打开第二只，退出可以用娃娃右键菜单或托盘菜单。

## 日常操作

| 操作 | 效果 |
| --- | --- |
| 单击、双击娃娃 | 摸头、跳跃 |
| 拖动娃娃 | 移动并记住位置 |
| 快速左右拖动 | 晃晕，再摸头安抚 |
| 拖到屏幕左右边缘 | 空闲后收起，鼠标靠近展开 |
| 点击底部额度 | 查看重置倒计时、刷新或修改设置 |
| 点击底部聊天图标 | 回到关联的原聊天 |
| 点击底部状态 | 展开或收起上方聊天气泡 |
| Ctrl+Alt+P | 隐藏或显示 |

支持三种显示层级：跟随 AI、始终置顶、普通窗口。所在屏幕进入全屏时可自动避让。设置里还可以改名、调整大小、减少动画、开启登录启动和电池省电模式。

空闲时会随机说话、挥手或跳跃，工作和等待回答时保持安静。这些台词是本地预设，不调用模型。频率和开关都可调整，也可以自己写[互动包](docs/interaction-packs.md)。

## 额度和聊天

额度来自本机 Codex 的 `account/rateLimits/read`，默认每两分钟更新。同步失败会保留上一次读数并标记，剩余不多于 20% 时可提醒。重置提醒需要新读数确认额度恢复。

聊天气泡显示本机 Codex 会话的标题、思考、工具调用、等待回答和完成状态。可以固定一个会话，也可以跟随最近活动。状态来自本地事件，通常有两秒左右延迟；长时间没有新事件时会标为未更新。

ChatGPT 按钮打开已有会话，继续使用原账号和历史。输入、发送仍在 ChatGPT 中完成。普通 ChatGPT 云端聊天的实时状态尚未接入，关联后会显示未接入，不会拿其他会话的状态代替。

## 构建

需要 .NET 8 SDK，无额外 NuGet 依赖。

```powershell
dotnet build Taitai.sln -c Release
dotnet run --project tests/Taitai.Checks -c Release
./build.ps1
```

运行文件输出到 `dist/Taitai`。检查程序覆盖额度解析、会话状态、聊天链接、互动包条件和冷却时间；加上 `-- --live` 可以检查本机 Codex 连接。

```text
src/Taitai.Desktop/   WPF 窗口、托盘、设置和角色素材
src/Taitai.Core/      额度、会话事件和随机互动逻辑
tests/Taitai.Checks/  无外部依赖的检查程序
docs/                动画预览和互动包说明
```

## 本地数据

设置保存在 `%LOCALAPPDATA%/TaitaiDesktop/settings.json`。会话事件从 `%CODEX_HOME%/sessions` 读取，默认是用户目录下的 `.codex/sessions`。程序不保存登录密钥，不上传会话内容。目前针对本机 Windows 会话，不读取远程或 WSL 状态。

## 许可与参考

代码采用 [MIT](LICENSE) 许可，角色图片和动画不在这份代码许可的范围内。

功能设计参考了 [CrossPet](https://github.com/lokicorvus/crosspet)，没有使用其角色素材。相关许可说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。接口依据 [Codex App Server](https://learn.chatgpt.com/docs/app-server) 和 [ChatGPT 深链接文档](https://learn.chatgpt.com/docs/reference/commands#chats)。
