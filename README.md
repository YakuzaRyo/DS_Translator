# Configer

Configer 是一个基于 **WinUI 3 (.NET 8)** 的桌面控制台，它把 DeepSeek 字幕翻译流水线（`main.py` 与 `ds_translator` 模块）打包在一起，提供可视化配置、依赖体检、集成 PowerShell 终端以及在线帮助，方便在同一窗口内完成「配置→运行→验证」的闭环。

> ✅ **核心目标**
>
> - 统一管理 `.env`、词库 (`lexicon.csv`) 与字幕目录
> - 一键导入/跟踪 `.srt` 文件的翻译状态
> - 在内置 PowerShell 中直接调用 `uv run python main.py`
> - 自动检测 uv/Python 等依赖是否齐全
> - 在 Help 视图里实时阅读本 README


## ✨ 功能速览

| 模块 | 功能 | 入口 |
| --- | --- | --- |
| **Data Config** | 编辑 `.env`、词库与字幕列表，支持批量导入 `.srt` 并查看 roast 结果。 | 主侧边栏「配置管理器」 |
| **Run** | WebView2 + xterm.js 打造的内嵌 PowerShell，集成 `PowerShellTerminalSession` 支持 Ctrl+C、重启、清屏。 | 主侧边栏「运行」 |
| **Dependency Check** | 基于 `DependencyCheckerRegistry` 自动发现的 `IDependencyChecker`，目前包含 UV 依赖检查，可扩展更多规则。 | 主侧边栏「依赖检查」 |
| **Help** | 读取并渲染仓库根目录的 README（即本文），支持刷新与在系统默认编辑器中打开。 | 主侧边栏「帮助」 |
| **Python CLI** | `main.py` 调用 `ds_translator` 模块完成字幕翻译，插件系统 (`plugins/verify.py`) 用于挑选待处理文件。 | 通过 Run 视图或命令行执行 |


## 🗂️ 项目结构

```
├─ App.xaml / App.xaml.cs           # WinUI 入口 & Application 对象
├─ MainWindow.xaml(.cs)             # Shell + 侧边导航，托管各个视图
├─ Views/
│   ├─ DependencyCheckView          # 依赖检测仪表盘
│   ├─ RunView                      # WebView2 + xterm 终端
│   ├─ HelpView                     # README 渲染器
│   └─ DependencyCheckView.xaml     # 对应的 XAML 布局
├─ Services/
│   ├─ IDependencyChecker.cs        # 检查器接口 + 结果模型
│   ├─ DependencyCheckerRegistry.cs # 反射发现/注册检查器
│   └─ UvDependencyChecker.cs       # 示例：检测 uv.exe/pyproject/uv.toml
├─ Utilities/
│   ├─ PowerShellTerminalSession.cs # Pseudo Console ↔ WebView 管道
│   ├─ AnsiRichTextFormatter.cs     # 把 ANSI 转成富文本片段
│   └─ ProjectRootLocator.cs        # 通过 sentinel 文件定位仓库根
├─ Models/                          # Data Config 视图的数据模型
├─ Assets/Terminal/                 # Web 终端静态资源 (xterm + 自定义 UI)
├─ data/                            # 运行时生成，包含 lexicon/subtitle/roast 等目录
├─ ds_translator/                   # 翻译核心模块 (API、DB、SRT 解析等)
├─ plugins/verify.py                # `register_commond` 注册的待处理筛选器
├─ utils/registry.py                # 轻量命令/插件注册表
├─ main.py                          # CLI 入口，串联插件→翻译→统计
├─ init_env.ps1                     # 使用 uv 同步 Python 运行环境的脚本
├─ pyproject.toml & uv.toml         # Python 依赖与镜像配置
└─ Configer.csproj / Configer.slnx  # WinUI 项目的 MSBuild/解决方案文件
```

> 所有 Python 相关文件会在构建/发布时自动复制到输出目录（参见 `Configer.csproj` 的 `<Content Include="...">` 配置）。


## 🔧 前置条件

| 组件 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 20H2 (10.0.19042) 或更高，推荐 Windows 11 |
| .NET | .NET SDK 8.0 + Windows App SDK 1.8（安装 VS 2022 17.11+ 的 “WinUI 3” 工作负载即可） |
| WebView2 | Microsoft Edge WebView2 Runtime 126+（通常系统自带，如无可从微软官网安装） |
| PowerShell | 5.1+（Windows 自带即可） |
| Python 依赖 | `uv`（仓库已自带 `uv.exe/uvx.exe/uvw.exe`，亦可使用系统 PATH 中的 uv） |
| DeepSeek API Key | `.env` 中的 `DEEPSEEK_API_KEY` 必填，用于调用翻译接口 |


## 🚀 快速开始

1. **克隆代码**
	 ```powershell
	 git clone https://github.com/<your-org>/Configer.git
	 cd Configer
	 ```

2. **构建 WinUI 客户端**
	 - Visual Studio：打开 `Configer.slnx` → 选择 x64/Debug → `F5` 启动。
	 - CLI：
		 ```powershell
		 dotnet build .\Configer.csproj -c Debug -p:Platform=x64
		 ```

3. **运行应用**
	 - VS 中 `F5`/`Ctrl+F5`
	 - 或 `dotnet run --project .\Configer.csproj`


## 🖥️ 图形界面使用指南

1. **启动与登录**：运行客户端后会直接进入 `MainWindow`，左侧为导航栏，右侧为当前视图的内容区域，无需额外账号即可开始使用。
2. **主导航**：侧边栏提供「配置管理器」「运行」「依赖检查」「帮助」四大入口，任意时刻都可以切换，状态会持久化在视图内部。
3. **常规操作流**：
	- 先在「配置管理器」检查 `.env`、词库与字幕列表，必要时导入或编辑数据；
	- 切换到「依赖检查」点击「重新检测」，确认 uv/Python 等依赖就绪；
	- 进入「运行」视图，用内置终端执行 `uv run .\main.py` 并观察实时输出；
	- 若遇到文档或操作疑问，随时打开「帮助」查阅 README。
4. **界面小技巧**：顶部命令栏会根据视图展示上下文按钮（如重启终端、导入字幕等）；右上角的刷新/打开文件夹等操作与列表联动，方便快速定位文件；在 Run 视图中可使用标准快捷键（Ctrl+C、Ctrl+L）控制终端。
5. **窗口管理**：应用支持 DPI 缩放与最大化；若需要更大终端空间，可在 Run 视图中折叠左侧导航后再进行命令行操作。


## 🧭 使用指南

### 1. Data Config（配置管理器）

1. 点击「重新加载」读取当前 `.env` 与 `data/lexicon/lexicon.csv`。
2. 在列表中直接编辑 Key/Value、词条/标签，支持新增/删除行；保存会同步写入对应文件。
3. 「字幕文件」卡片显示 `data/subtitle/*.srt` 与对应 `data/roast/*-roasted.srt` 的完成状态。
4. 通过「导入字幕」可批量从文件系统复制 `.srt` 到 `data/subtitle`，自动去重并追加序号。
5. 「刷新状态」会重新扫描 roast 目录，更新每个字幕的 `DONE` 标签。
6. 「打开数据文件夹」快速定位 `data/` 目录以便手动操作。

> 💡 `.env` 中至少需要设置 `DEEPSEEK_API_KEY`，可选 `VERIFY_TYPE`（默认 `roasted`），以及 `INPUT_DIR`/`OUTPUT_DIR` 来覆盖数据目录。

### 2. Run（集成终端）

- 首次进入会加载 `Assets/Terminal/index.html`（xterm.js）并与 `PowerShellTerminalSession` 建立伪控制台通道。
- 「重启终端」会销毁当前 PowerShell 进程并重新创建；「停止」发送 `exit`；「Ctrl+C」用于终止正在运行的脚本；「清屏」调用 xterm API。
- 默认工作目录为应用根目录，可直接运行翻译命令：
	```powershell
	uv run python .\main.py
	```
- Web 端收到 stdout/stderr 会通过 WebMessage 回传到 xterm，支持 ANSI 颜色与超链接（点击 `open-link` 事件即可在默认浏览器中打开）。

### 3. Dependency Check（依赖检查）

- 点击「重新检测」时，`DependencyCheckerRegistry` 会反射扫描所有实现 `IDependencyChecker` 的类型。
- 每个检查结果包含总体状态与若干子项，并使用 `DependencyCheckResult.Items` 显示详细文件。
- 默认的 `UvDependencyChecker` 会确认以下文件存在：
	- `uv.exe`
	- `uv.toml`
	- `pyproject.toml`
- 若需扩展，创建新类实现 `IDependencyChecker` 并提供无参构造函数即可自动被发现。

### 4. Help（帮助）

- `HelpView` 会向上回溯目录 6 层寻找 `README.md`，成功后用 `CommunityToolkit` 的 `MarkdownTextBlock` 渲染。
- 「在资源管理器中打开」使用系统默认编辑器查看原文件；「刷新」可加载最新修改，便于写文档时实时预览。


## 🐍 Python 翻译流水线

1. **插件发现**：`plugins/verify.py` 通过 `@register_commond("plugin", "roasted")` 注册，运行时根据 `VERIFY_TYPE` 选择合适的函数过滤字幕文件。
2. **数据目录**：`main.py` 会创建 `data/subtitle`, `data/roast`, `data/logs`, `data/cache_db`, `data/lexicon`, `data/proofread` 等路径并确保存在。
3. **运行命令**：
	 ```powershell
	 uv run python .\main.py
	 ```
4. **流程**：加载 `.env` → 校验 `DEEPSEEK_API_KEY` → 加载词库/数据库 → 启动 `ds_translator.api` 的重试线程 → 依次处理字幕（进度条由 Rich 渲染）。
5. **结果**：每个输入 `xxx.srt` 会在输出目录生成 `xxx-roasted.srt`，同时更新数据库统计。


## 🛠️ 开发者贴士

- **新增依赖检查**：
	1. 在 `Services/` 中创建 `FooDependencyChecker`，实现 `IDependencyChecker` 并返回 `DependencyCheckResult`。
	2. 无需手动注册，启动应用或点击「依赖检查」时会自动生效。

- **自定义终端资源**：
	- `Assets/Terminal/index.html`、`terminal.js`、`terminal.css` 使用了 `xterm.js`、`xterm-addon-fit`、`xterm-addon-web-links`。
	- 修改后会被 `CopyToOutputDirectory=PreserveNewest` 复制至运行目录，热重载只需重新编译/部署。

- **扩展插件**：
	- 在 `plugins/` 下新建 Python 文件，使用 `@register_commond("plugin", "your_mode")` 装饰函数。
	- 在 `.env` 中设置 `VERIFY_TYPE=your_mode`，Data Config 保存后即可在下次 `main.py` 运行时应用。


## ❗ 常见问题排查

| 现象 | 解决方案 |
| --- | --- |
| Run 视图显示「找不到终端资源目录」 | 确认 `Assets/Terminal/` 存在且已复制到输出目录；重新构建项目。 |
| PowerShell 控制台没有输出 | 检查 Windows 是否允许运行伪控制台（需要 Windows 10 1809+）；若仍失败，可在事件查看器查看应用日志。 |
| uv 检查失败 | 在仓库根目录确认 `uv.exe/uvx.exe` 是否存在；或安装官方 uv 并确保 PATH 可访问。 |
| Python 依赖安装卡住 | 可编辑 `uv.toml` 更换镜像；`init_env.ps1 -VerboseLogging` 查看详细日志。 |
| WebView2 相关异常 | 安装 [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) 并重启应用。 |


## ✅ 下一步 & 贡献

- 继续完善 `DependencyChecker`（如检测 `.env` 必填键、`data/lexicon/lexicon.csv` 是否存在）。
- 为 `RunView` 增加常用命令快捷按钮（例如「运行翻译」「查看日志」）。
- 提交 PR 前请确保：
	1. C# 代码通过 `dotnet build`
	2. Python 端通过 `uv run python -m compileall ds_translator` 或最小化单元测试（如适用）
	3. 更新本文档中涉及的新特性说明

欢迎通过 Issue/PR 分享改进建议，一起让翻译工作流更加丝滑！

