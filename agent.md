# WinClipboard Agent Guide

本文档是本项目后续协作 agent 的入口说明。当前 implementer 上下文已丢失时，先读本文件，再读 `system_map.md` 和 `docs/architecture/2026-05-24-runtime-ux-feedback-task-cards.md`。

## Current Project State

- 产品：Windows 剪贴板历史、收藏、保险箱、图片/文件历史、快捷唤出工具。
- 技术栈：.NET 8 + WinUI 3 + Windows App SDK + WebView2 + Vanilla HTML/CSS/JS + SQLite。
- 当前分支：`codex/boundary-hardening-and-ux-cards`。
- 当前工作区：有未提交改动。不要回滚用户或其他 agent 的修改。
- 主 UI：`clip/clip/MainWindow.xaml` 承载 WebView2，真实界面在 `clip/clip/wwwroot/`。
- 前端已拆分：`app.js` 只负责启动编排；交互逻辑在 `wwwroot/js/*.js`。
- 后端 bridge 已拆分：`Bridge/WebBridge.cs` 只做 action dispatch，具体 action 分到 `Bridge/*Actions.cs`。

## Current Progress Snapshot

已完成并应保持稳定：

- C# 边界拆分：bridge、storage、native service 已从大文件拆出。
- 前端边界拆分：`state.js`、`bridge.js`、`history.js`、`listView.js`、`listActions.js`、`keyboard.js`、`overlays.js`、`settings.js`、`vault.js`、`drag.js`、`toast.js`、`windowMove.js`。
- 构建/检查脚本：
  - `scripts/verify-build.ps1`
  - `scripts/check-frontend-js.ps1`
  - `scripts/check-runtime-smoke.ps1`
  - `scripts/check-interaction-affordance.ps1`
- 运行时防重入：`App.xaml.cs` 使用 `Local\WinClipboard.SingleInstance`。
- 交互基础语义：
  - 单击卡片：只选择。
  - 双击卡片：粘贴并隐藏窗口。当前实现使用 click counter，避免 DOM 重绘导致原生 `dblclick` 丢失。
  - Enter：粘贴选中项。
  - Space / 预览按钮：预览。
  - 1-9：快速粘贴。
  - Escape：关闭最上层 overlay 或隐藏窗口。
- 过滤：搜索框保留 `/img`、`/file`，并新增 All/Text/Image/File chips。
- 操作提示：顶部 `?` 按钮和设置里的“查看”入口可打开 help modal。
- 粘贴关闭：普通粘贴和保险箱复制不再等 toast 再隐藏。
- 预览：文本/文件预览进入 modal，支持 close 按钮和右键关闭逻辑；文本选择样式已加入。
- 窗口移动：新增 `windowMove.js`，通过 `startWindowMove` bridge action 调用原生窗口移动。

当前仍需优先处理/验收：

1. `FB03` 预览体验：人工确认文本/文件预览可选中、Ctrl+C 可复制、右键空白处关闭、选中文本时右键不误关。
2. `FB05` 窗口移动：人工确认顶部移动把手可用，且不影响卡片拖拽、搜索、预览选中文本。
3. `FB04` 原生拖拽：图片拖到 QQ/Codex、文本拖到编辑器、文件拖到接收目标；重点看是否卡住、是否清理 `.card-dragging` 和 `window.__isDragging`。
4. 清理本地杂项：当前 status 里可能出现 `.vscode/...vita-loader-test.html` 之类临时文件，未确认用途前不要随手提交。

## Role Rules

### Architect

职责：

- 只定义边界、任务卡、验收标准、风险和下一阶段顺序。
- 维护 `agent.md`、`system_map.md`、`docs/architecture/*task-cards.md`。
- 不直接实现代码，除非用户明确要求。
- 发现实现偏离任务卡时，先 review，再更新任务卡或要求 implementer 收口。

Architect 提示词：

```text
你是 WinClipboard 的 architect。请先阅读 agent.md 和 system_map.md，再查看当前代码和 docs/architecture 最新任务卡。你的职责是维护边界和验收标准，不直接大改代码。输出应包含当前状态、已完成项、待做项、风险、验证方式和推荐执行顺序。
```

### Implementer

职责：

- 按任务卡小步实现，不扩大范围。
- 每次只碰当前任务涉及的 owner 文件。
- 不把逻辑重新堆回 `app.js` 或 `WebBridge.cs`。
- 不引入 Node 构建链；继续使用 Vanilla JS。
- 不使用 inline handlers：禁止 `onclick=`, `onchange=`, `oninput=`, `.onclick`, `.onchange`, `.oninput`。
- 交互改动必须同步更新 `scripts/check-interaction-affordance.ps1`。
- 涉及 bridge action 时同步更新：
  - `Bridge/WebBridge.cs`
  - 对应 `Bridge/*Actions.cs`
  - `system_map.md`
- 涉及剪贴板写入时确认 suppress flag，避免自写自读。

Implementer 提示词：

```text
你是 WinClipboard 的 implementer。先读 agent.md、system_map.md 和 docs/architecture/2026-05-24-runtime-ux-feedback-task-cards.md。只实现当前被指定的 task，不扩大范围。保持现有 WebView2 + Vanilla JS 架构，新增交互必须同步更新 check-interaction-affordance.ps1。完成后说明改动文件、交互路径、验证命令和人工验收点。
```

### Debugger

职责：

- 先复现，再定位；不要凭感觉改。
- 按层分诊：DOM/CSS、JS 状态、bridge action、storage、clipboard writer/reader、Win32 hotkey/message window、native drag/drop。
- 对前端问题优先检查：
  - 事件是否被 `innerHTML` 重绘破坏。
  - overlay 是否有 z-index/display/position。
  - `window.__isDragging` 是否卡住。
  - bridge 是否 timeout 或返回 `{ success:false, error }`。
- 对运行时 smoke 问题优先确认是否有残留 `AppX/clip.exe` 或剪贴板被占用。

Debugger 提示词：

```text
你是 WinClipboard 的 debugger。先读 agent.md 和 system_map.md，再按复现路径分层定位。不要直接猜补丁；先给证据、相关文件、故障层级、最小验证命令，再给最小修复建议。特别注意 WebView2 事件重绘、overlay 层级、Win32/OLE drag、剪贴板自触发和残留进程。
```

## File Ownership

前端：

- `wwwroot/app.js`：启动编排。不要放业务逻辑。
- `wwwroot/js/state.js`：共享状态。
- `wwwroot/js/bridge.js`：WebView2 bridge、超时、C# 回调。
- `wwwroot/js/history.js`：历史/收藏加载、搜索、chips、清空历史。
- `wwwroot/js/listView.js`：列表 DOM、事件委托、双击 click counter、缩略图。
- `wwwroot/js/listActions.js`：选择、粘贴、删除、收藏、tag。
- `wwwroot/js/keyboard.js`：快捷键、搜索 debounce、快捷键录制。
- `wwwroot/js/overlays.js`：预览、确认框、help modal、面板关闭顺序、窗口动画。
- `wwwroot/js/settings.js`：设置面板事件与持久化。
- `wwwroot/js/vault.js`：保险箱。
- `wwwroot/js/drag.js`：卡片拖拽到外部应用。
- `wwwroot/js/windowMove.js`：窗口移动把手。
- `wwwroot/js/toast.js`：轻量反馈。

后端：

- `Bridge/WebBridge.cs`：action dispatch。
- `Bridge/WindowActions.cs`：隐藏窗口、日志、拖拽、背景选择、窗口移动。
- `Native/DragDropService.cs`：OLE drag/drop。
- `MainWindow.xaml.cs`：WebView2 宿主、窗口显示/隐藏/定位。

## Verification Commands

常规验证：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\check-frontend-js.ps1
powershell -ExecutionPolicy Bypass -File scripts\check-interaction-affordance.ps1
powershell -ExecutionPolicy Bypass -File scripts\verify-build.ps1
powershell -ExecutionPolicy Bypass -File scripts\check-runtime-smoke.ps1
```

注意：

- `verify-build.ps1` 是当前稳定构建入口。
- `dotnet build clip/clip.slnx` 在当前 .NET 8 SDK 下会因 `.slnx` 格式失败，属于已知 solution 格式问题。
- 默认 `dotnet build clip/clip/clip.csproj` 会走 MSIX packaging 并可能报 `APPX0002`，不是当前本地编译入口。
- 如果构建输出被锁，先关闭正在运行的 `clip.exe`。

## Manual Smoke Checklist

- 热键唤出窗口，Escape 隐藏。
- 单击选择，双击粘贴，Enter 粘贴，1-9 快速粘贴。
- 文本/图片/文件复制进入历史。
- All/Text/Image/File chips 与 `/img`、`/file` 都可用。
- 预览文本和文件，选择文字并 Ctrl+C。
- Help 按钮和设置里的“查看”可打开提示。
- 窗口移动把手可拖动窗口。
- 图片卡片拖到 QQ/Codex，文本拖到编辑器，文件拖到可接收目标。
- 删除历史/保险箱项需要确认，确认不会重复触发。
- 保险箱复制不暴露明文到普通列表或日志。
