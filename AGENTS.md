# IntraBox Agent 约定

面向 Cursor、Codex、Claude Code 等会读取本文件的助手。人读贡献流程见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 项目约束

- 目标：.NET Framework 4.8 WPF，**C# 7.3**，Win7 SP1+ x64。
- 含中文的 `.cs` / `.xaml`：UTF-8 **带 BOM**（`tools/add-bom.ps1` / `tools/check-bom.ps1`）。
- 不擅自加 NuGet；确定性逻辑优先 `src/IntraBox/Core/*Helper` + MSTest（`test.bat`）。
- 新工具：`Modules/` 子目录，注册 `ModuleRegistry`，写入 `NavOrder`；按需加载、用完销毁。
- 托盘里的 IntraBox 会锁 exe；`build.bat` / `rebuild.bat` 会询问，确认后才结束进程再编，取消则中止。
- 行为变更时同步 `docs/产品设计文档.md`、`docs/测试用例.md`，必要时 `docs/使用说明.txt`。
- 未经仓库主人明确要求：不 `git commit`、不 push、不改 git config。
- 远程：`origin` 为 [Gitee 主仓库](https://gitee.com/xinyi6952/IntraBox)，`github` 为镜像。若主人要求 push，只推 `origin`；不要默认 `git push github`。托管说明见 [docs/开源仓库.md](docs/开源仓库.md)。

## 代码评审

用户或 PR 要求「代码评审」「评审」「code review」时：

1. 不要再索要评审标准。
2. 执行 [docs/代码评审清单.md](docs/代码评审清单.md)：按其中 §5 触发表只跑相关专节。
3. 默认范围：未提交 diff，或指定的 PR/模块。
4. 产出：结论（通过 / 部分 / 失败）+ 问题列表 + 是否建议补测试或文档。

详细手工步骤见 [docs/测试用例.md](docs/测试用例.md)。个人评审流水账不要写入本仓库。

## 代码提交

用户要求「提交」「commit」时：

1. 不要再索要提交说明格式。
2. 执行 [docs/代码提交规范.md](docs/代码提交规范.md)。
3. 多个改动点用无序列表写备注；禁止 `Co-authored-by` 及任何 Agent / 模型身份。
4. 未说明分多次或分阶段时，只做 **一条** 提交。
