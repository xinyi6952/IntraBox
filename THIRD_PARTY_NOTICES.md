# 第三方依赖声明（Third-Party Notices）

本项目使用了以下第三方开源组件，遵循其各自的许可证。感谢这些项目的贡献者。

## 运行时依赖（NuGet）

| 组件 | 版本 | 许可证 | 用途 |
|---|---|---|---|
| [Newtonsoft.Json](https://www.newtonsoft.com/json) | 13.0.3 | MIT | JSON 解析 / 序列化 |
| [DiffPlex](https://github.com/mmanela/diffplex) | 1.9.0 | MIT | 文本差异计算 |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | 13.7.1 | MIT | YAML 解析 |
| [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) | 6.1.3.50 | MIT | 代码编辑器控件 |
| [ClosedXML](https://github.com/ClosedXML/ClosedXML) | 0.97.0 | MIT | Excel 导出 |
| [QRCoder](https://github.com/codebude/QRCoder) | 1.4.3 | MIT | 二维码生成 |
| [ZXing.Net](https://github.com/micjahn/ZXing.Net) | 0.16.9 | Apache-2.0 | 二维码 / 条码 |
| [Tesseract](https://github.com/charlesw/tesseract) | 4.1.1 | Apache-2.0 | OCR 文字识别 |
| [Markdig](https://github.com/xoofx/markdig) | 0.37.0 | BSD-2-Clause | Markdown 渲染 |

## 测试依赖

| 组件 | 版本 | 许可证 |
|---|---|---|
| MSTest.TestFramework / MSTest.TestAdapter | 2.2.10 | MIT |
| Microsoft.NET.Test.Sdk | 17.8.0 | MIT |

## 传递依赖

上述组件引入的传递依赖（如 ClosedXML 依赖的 DocumentFormat.OpenXml、ExcelNumberFormat；Tesseract 依赖的 leptonica / tesseract 原生库等）同样遵循其各自的许可证。

## 完整许可证文本

各组件许可证全文参见对应 NuGet 包及上游仓库。如有遗漏或错误，欢迎提交 Issue 指正。
