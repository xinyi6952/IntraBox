# IntraBox（内聚）

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

面向内网工作者与开发者的**绿色免安装聚合效率工具**，纯本地运行、无网络依赖、无遥测。

> **免责声明**：本工具仅供合法、授权的用途（系统管理、开发调试、安全测试等）。其中包含 Hosts 编辑、端口/文件占用查看、加解密、证书解码等能力，请勿用于任何未经授权的活动，使用者需对自身行为负责。

## 特性

- **兼容 Windows 7 SP1+**（.NET Framework 4.8）
- **绿色免安装**：解压即用，单文件夹部署
- **纯本地**：无网络、无遥测、无自动更新，数据不出本机
- **低资源**：按需加载、用完销毁；工具输入靠历史落盘，不缓存控件
- **43 个实用工具**，覆盖截图 / 文件 / 格式化 / 文本 / 编码 / 安全 / 网络 / 系统等

## 功能清单（43 个，详见[产品设计文档](docs/产品设计文档.md)）

截图（2）：屏幕截图（含 OCR）、屏幕标尺
文件管理（4）：文件整理、文件查找、批量重命名、重复文件查找
格式化（1）：格式化（JSON/YAML/XML/SQL/HTML/CSS/Markdown/Nginx）
文本工具（7）：文本比对、正则测试、命名转换、行处理、文本统计、Markdown 预览、正则可视化
效率工具（4）：剪贴板历史、任务计划、笔记、账号备忘
编码转换（4）：Base64、URL/HTML/Unicode 编解码、URL 解析、Unicode 字符检查
转换工具（1）：时间戳转换
安全工具（3）：哈希计算、加解密、证书解析
数据转换（1）：文本转 Excel
网络工具（2）：端口连通性测试、端口占用查看
设计工具（2）：屏幕取色、色盲模拟
生成器（3）：ID 与密码生成、JSON 转实体类、二维码生成
系统工具（6）：本机信息、文件占用查看、Hosts 编辑器、窗口置顶、防止睡眠、注册表浏览
开发工具（2）：SemVer 版本比较、JSON Schema 验证
图片工具（1）：图片压缩缩放转换

## 目录结构

```
IntraBox/
├── IntraBox.sln                 解决方案
├── build.bat                    构建脚本（编译 + 同步到 dist）
├── test.bat                     测试脚本（跑 MSTest 单元测试）
├── src/IntraBox/                主项目（net48 WPF）
│   ├── App.xaml(.cs)            应用入口（单实例/托盘/全局异常/热键/剪贴板监听）
│   ├── MainWindow.xaml(.cs)     主窗口（导航 + 工作区 + 状态栏）
│   ├── Core/                    核心框架（模块加载、配置、历史、各类 Helper）
│   ├── Controls/                自定义控件（AvalonEdit 代码编辑器、折叠、括号高亮）
│   └── Modules/                 功能模块（每个工具一个子目录）
├── tests/IntraBox.Tests/        单元测试项目（MSTest）
├── dist/IntraBox/               绿色发布目录（主程序 + 依赖 + 文档）
├── redist/                      运行时离线安装包（.NET Framework 4.8 + VC++ 运行库）
├── docs/                        产品设计、测试用例、代码评审清单、代码提交规范
└── tools/                       开发辅助脚本（图标生成、BOM 检查）
```

## 构建

- **增量构建**：双击 `build.bat`（已含进程占用检查），产物同步到 `dist/IntraBox/`。
- **完全重建**：双击 `rebuild.bat` —— 删除编译缓存、`dist/IntraBox/`，并清空运行时数据（`%LOCALAPPDATA%\IntraBox\Data`：配置/历史/待办等），下次启动回到首次默认（欢迎引导、示例待办）。

> 运行环境：Win10 1903+ 已预装 .NET Framework 4.8；Win7 SP1 需先装 [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) 与 VC++ 运行库。

## 测试

双击 `test.bat`，跑 `tests/IntraBox.Tests` 的全部 MSTest 用例。确定性逻辑走自动化测试；交互型功能按 `docs/测试用例.md` 验证；已有功能的防回归见 `docs/代码评审清单.md`。

## 发布

- **Win10 / Win11**：只给 `dist/IntraBox/`。
- **Win7 内网机**：需额外安装 .NET Framework 4.8 与 VC++ 运行库（微软官方下载）：
  - [.NET Framework 4.8 离线安装包](https://dotnet.microsoft.com/download/dotnet-framework/net48)
  - [VC++ 2015-2022 运行库（x64）](https://aka.ms/vs/17/release/vc_redist.x64.exe) / [x86](https://aka.ms/vs/17/release/vc_redist.x86.exe)
- 环境要求与检查方法见 `dist/IntraBox/使用说明.txt`。

## 开发约定

- 新增功能模块：在 `Modules/` 下新建子目录，实现 `UserControl`（可选实现 `IModuleView` 做激活/销毁、`ILeaveGuard` 做离开确认），在 `ModuleRegistry.RegisterDefault()` 注册，并写入 `NavOrder` 的默认分类/工具顺序。
- 确定性逻辑尽量抽成 `Core/*Helper` 纯静态类，便于单元测试。
- 每个模块遵循「按需加载、用完销毁」：切换离开时释放，进入时从 `HistoryManager` 恢复输入。
- 产品设计文档（`docs/产品设计文档.md`）与代码保持同步。

## 许可证

本项目采用 [MIT License](LICENSE)。

## 第三方依赖

本项目使用了多个第三方开源组件，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 贡献与安全

- 贡献指南见 [CONTRIBUTING.md](CONTRIBUTING.md)；AI 助手见 [AGENTS.md](AGENTS.md)
- 评审见 [代码评审清单](docs/代码评审清单.md)，提交见 [代码提交规范](docs/代码提交规范.md)
- 行为准则见 [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- 安全漏洞上报见 [SECURITY.md](SECURITY.md)
