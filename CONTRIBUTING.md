# 贡献指南

感谢你对 IntraBox（内聚）的关注！欢迎提交 Issue 和 Pull Request。

## 环境要求

- Windows（Win10 / Win11 建议；产物需兼容 Win7 SP1）
- [Visual Studio 2022+](https://visualstudio.microsoft.com/)（含 .NET Framework 4.8 开发工具），或 .NET Framework 4.8 Developer Pack
- 目标框架：.NET Framework 4.8（经典 csproj + WPF）

## 构建与测试

- 双击 `build.bat`：编译 + 同步到 `dist/IntraBox/`
- 双击 `test.bat`：构建测试项目并运行全部 MSTest 用例
- 命令行等价：

```bat
msbuild IntraBox.sln /t:Restore;Build /p:Configuration=Release /m
vstest.console.exe tests\IntraBox.Tests\bin\Release\IntraBox.Tests.dll
```

## 开发约定

- 新增功能模块：在 `src/IntraBox/Modules/` 下新建子目录，实现 `UserControl`（可选实现 `IModuleView` 做激活/销毁、`ILeaveGuard` 做离开确认），在 `ModuleRegistry.RegisterDefault()` 注册，并写入 `NavOrder` 的默认分类/工具顺序。
- 确定性逻辑尽量抽成 `src/IntraBox/Core/*Helper` 纯静态类，便于单元测试。
- 每个模块遵循「按需加载、用完销毁」。
- UI 文本使用简体中文。

## 提交流程

1. Fork 本仓库并创建分支（如 `feature/xxx` 或 `fix/xxx`）
2. 提交信息按 [代码提交规范](docs/代码提交规范.md)：多改动点用无序列表；一条需求默认一条 commit；不要附带 AI 工具身份
3. 确保 `test.bat` 全绿
4. 提交 Pull Request，描述改动动机与影响；涉及已有功能时对照 [代码评审清单](docs/代码评审清单.md) 的触发表做防回归检查

## AI 助手

仓库根目录 [AGENTS.md](AGENTS.md)（Claude Code 另见 [CLAUDE.md](CLAUDE.md)）写明语言版本、BOM、模块注册等约束。要求「代码评审」或「提交」时分别按 [代码评审清单](docs/代码评审清单.md)、[代码提交规范](docs/代码提交规范.md) 执行，无需再贴标准。

## 代码风格

- 与现有代码保持一致（C# 7.3；沿用现有的中文注释与命名风格）

## 源码编码约定（为什么用带 BOM 的 UTF-8）

源码（`.cs` / `.xaml`）统一使用**带 BOM 的 UTF-8**，不是无 BOM。

原因：本项目目标框架是 .NET Framework 4.8，其 C# 编译器对**无 BOM** 的源码默认按「系统 ANSI 代码页」解析，而非 UTF-8。在中文 Windows（GBK 代码页 936）上，无 BOM 的 UTF-8 中文源码会被误读，导致界面文字乱码。加 BOM 能让编译器在任何代码页下都正确识别 UTF-8。

- 新增含中文的源码后，请运行 `tools/check-bom.ps1` 校验（CI 也会自动检查，漏了会红）。
- 若漏加，运行 `tools/add-bom.ps1` 批量补 BOM。
- `build.bat` / `test.bat` 例外：刻意保持纯 ASCII（cmd 在 GBK 下解析中文易乱码）。
- 若项目未来升级到 .NET 8+（现代 Roslyn 默认 UTF-8），可回归无 BOM 的纯净 UTF-8。
