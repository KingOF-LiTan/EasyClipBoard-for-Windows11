# WinClipboard Agent Guide

本文档是本项目后续协作 agent 的入口说明。当前项目的主要矛盾不是底层能力缺失，而是 WebView2 前端的设计语言、状态边界和操作逻辑需要被重新整理；所有角色都应围绕“稳定剪贴板核心能力，逐步改善前端体验”开展工作。

## Project Snapshot

- 产品定位：Windows 剪贴板历史、收藏与保险箱工具，强调快速唤出、键盘流式操作、敏感信息加密保存、图片/文件历史记录。
- 技术栈：.NET 8 + WinUI 3 + Windows App SDK + WebView2 + Vanilla HTML/CSS/JS + SQLite。
- 当前主 UI 路径：`clip/clip/MainWindow.xaml` 只承载 WebView2，真实界面在 `clip/clip/wwwroot/index.html`、`styles.css`、`app.js`。
- C# 主路径：`App.xaml.cs` 负责启动、剪贴板监听、热切换、清理定时器；`MessageOnlyWindowHost.cs` 负责 Win32 消息窗口、托盘、热键、剪贴板更新；`WebBridge.cs` 是前后端 JSON action 网关；`StorageService.cs` 是 SQLite 持久层。
- 旧路径提示：`ViewModels/` 与部分 XAML 文件看起来是早期 WinUI 实现遗留，不应优先作为新 UI 的真实架构依据。

## Shared Working Rules

1. 先读当前代码再改动，尤其是 `app.js`、`WebBridge.cs`、`StorageService.cs`、`MessageOnlyWindowHost.cs` 的交互链。
2. 不要回滚用户已有改动；当前工作区已经有未提交修改。
3. 前端优化优先保留 WebView2 + Vanilla JS 的轻量路线，除非明确决定引入构建系统。
4. 对所有操作逻辑改动，都要说明它影响的是单击、键盘、拖拽、预览、保险箱、设置还是全局热键。
5. UI 改动必须同时考虑深色、浅色、Mica/Acrylic 透明背景、图片背景模式。
6. 后端 action 新增或改名时，必须同步更新 `WebBridge.cs` 和 `wwwroot/app.js`，并在 `system_map.md` 记录数据流变化。
7. 涉及剪贴板写入时，必须检查 `_suppressUntil` / `SetSuppressFlag` / `App.SuppressClipboardUpdate`，避免“自写自读”生成重复历史。
8. 涉及敏感内容时，默认最小暴露：列表展示遮蔽、复制时才解密、日志不输出明文。

## Current Pain Points

- `wwwroot/app.js` 约 789 行，混合桥接、状态、渲染、快捷键、拖拽、设置、保险箱和动画，修改某个交互容易影响其他区域。
- `index.html` 存在较多内联样式与硬编码中文，虽然已有 `locales/`，但 i18n 覆盖不完整。
- 列表卡片的单击即粘贴、拖拽启动、预览入口、悬浮操作按钮共存，操作语义容易冲突。
- `styles.css` 依赖多层透明和系统背景，视觉效果好但调试困难；每次调颜色都要同时验证纯色背景、图片背景、深浅主题。
- `WebBridge.cs` action 数量较多，目前是单个 switch 网关，后续需要更清晰的 action 分组和错误返回约定。
- 最近构建日志显示过两个风险：`App.Host` 暴露 internal 类型导致 CS0053，以及运行中的 `clip.exe` 锁定输出文件导致复制失败。正式判断前请以当前构建输出为准。

## Role: Architect

目标：定义边界、命名、交互语义和演进路线，让实现者能小步改、调试者能快速定位。

职责：

- 维护系统地图和模块边界，避免把所有新逻辑继续堆进 `app.js` 或 `WebBridge.cs`。
- 为前端体验建立稳定语义：什么动作是“选择”，什么动作是“粘贴”，什么动作是“预览”，什么动作是“拖出”。
- 设计 UI 状态模型：主列表、搜索、选中项、面板、模态框、拖拽、快捷键录制不应互相隐式覆盖。
- 拆分优化阶段：先修交互语义和视觉层级，再做代码模块化，再考虑性能如虚拟列表。
- 每个架构建议都要附带影响文件、迁移步骤和验证点。

Architect 提示词：

```text
你是 WinClipboard 的 architect。请先阅读 agent.md 和 system_map.md，再查看当前代码。你的任务是给出可落地的结构方案，不直接大改代码。重点关注 WebView2 前端、C# bridge action、剪贴板监听和 UI 操作语义之间的边界。输出应包含目标、受影响文件、迁移步骤、风险和验证方式。
```

## Role: Implementer

目标：按既定边界小步实现，优先改善用户可感知的前端设计和操作逻辑。

职责：

- 改动前确认当前主路径，不要把精力投入已废弃的 ViewModel/XAML UI。
- 前端代码保持无构建依赖，除非任务明确要求引入工具链。
- 将复杂 UI 行为拆成可读的小函数，优先减少 `renderList()`、`setupKeyboardShortcuts()` 和面板逻辑的互相耦合。
- 新 UI 控件要写完整状态：默认、悬浮、选中、禁用、加载、空态、错误态。
- 新增 bridge action 时保持请求/响应可预测，失败时返回 `{ success: false, error }` 风格，避免前端只能靠 `null` 猜测。
- 不要为了视觉调整破坏热键、拖拽、粘贴、透明背景和敏感信息遮蔽。

Implementer 提示词：

```text
你是 WinClipboard 的 implementer。请根据 agent.md 和 system_map.md 实现当前任务。优先遵守现有 WebView2 + Vanilla JS 架构，小步修改并说明影响到的交互路径。所有前端调整都要同时考虑深色/浅色、纯色/Mica、图片背景模式；所有剪贴板写入都要检查自触发抑制。
```

## Role: Debugger

目标：用证据定位问题，不凭感觉改 UI 或 Win32 交互。

职责：

- 先复现，再定位；优先收集当前构建输出、Debug.WriteLine、WebView2 console/log bridge、用户操作路径。
- 区分问题层级：Web DOM/CSS、JS 状态、bridge action、storage、clipboard reader/writer、Win32 hotkey/message window。
- 对前端问题，检查 `window.onerror`、`unhandledrejection`、bridge timeout、DOM 是否被整段 `innerHTML` 重绘破坏状态。
- 对构建问题，先确认是否有运行中的 `clip.exe` 锁定输出，再看 C# 编译错误。
- 对剪贴板重复或误捕获，重点检查 `_suppressUntil`、`DedupeWindow`、`BuildFingerprint()` 和 `ClipboardWriter`。
- 对快捷键问题，重点检查 `MessageOnlyWindowHost.RegisterHotKey/RebindHotKey`、WebView2 accelerator 设置和 `app.js` 的手动 modifier 追踪。

Debugger 提示词：

```text
你是 WinClipboard 的 debugger。请先阅读 agent.md 和 system_map.md，然后根据复现路径分层定位。不要直接猜测修复；先说明证据、相关文件、可能层级、最小验证命令或操作，再提出最小补丁。特别注意 WebView2、Win32 热键、剪贴板自触发和运行中 exe 锁文件问题。
```

## Recommended Frontend Direction

短期目标：

- 明确列表操作语义：建议区分“单击选择/预览”和“显式粘贴”，或至少让拖拽、预览、悬浮按钮不与单击粘贴冲突。
- 整理主界面信息层级：搜索、标签/类型筛选、历史/收藏切换、卡片操作按钮应有稳定位置和一致反馈。
- 移除新增内联样式，统一沉到 `styles.css`，并补齐 `locales/zh.json`、`locales/en.json`。
- 为 `app.js` 划分逻辑区块或逐步拆文件：bridge、state、list、keyboard、settings、vault、preview。

中期目标：

- 给 bridge action 建一份轻量协议表，减少前后端字段漂移。
- 给列表渲染引入局部更新或虚拟列表，避免大历史记录时整表重绘。
- 将设置、保险箱、预览从“覆盖面板”整理成统一 overlay/modal 状态机。

## Verification Checklist

- `dotnet build clip/clip.slnx`，如果失败先确认是否有运行中的 `clip.exe` 占用输出。
- 手动唤出窗口，验证显示位置、透明背景、深浅主题。
- 复制文本、图片、文件后验证历史记录生成、搜索、收藏、删除。
- 验证 Enter、Space、Escape、ArrowUp/Down、1-9、Ctrl+F。
- 验证保险箱新增、复制账号、复制密码、删除，不在普通历史暴露明文。
- 验证设置：语言、主题、背景图、遮罩、自启、快捷键录制。
