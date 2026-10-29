# QtSharp

[English](README.md) | 简体中文

**使用 C# 编写 Qt 桌面应用，支持 Windows 和 Linux。**

QtSharp 的目标是让 C# 开发者获得像 PyQt/PySide 一样方便的 Qt 开发体验，并结合
C# 的语言特性与 .NET 生态调整 API 和模块设计。目前提供 Qt Widgets、绘图、网络及
可选的 Charts、Qt3D、Multimedia 等模块。

**项目尚未发布 1.0，后续 API 仍可能大幅调整，不保证版本间的源码或二进制兼容。**
请固定包版本，升级前查看[更新日志](CHANGELOG.md)并测试应用。

目前支持 **.NET 10、Qt 6.12.0、Windows x64 和 Linux x64（glibc）**。

## 安装与快速开始

安装 **.NET 10 SDK**，从 [v0.21.0 发布页](https://github.com/ProxyCheng/QtSharp/releases/tag/v0.21.0)
下载对应平台的 `QtSharp-0.21.0-win-x64.zip` 或 `QtSharp-0.21.0-linux-x64.zip`，
解压到本地 NuGet 源目录。请保留归档中的全部包，以便还原依赖。

包中包含原生绑定和 Qt 运行库，编写应用无需另装 Qt SDK 或 C++ 编译器。
当前安装源是 **GitHub Releases，并非 nuget.org**；nuget.org 上名为 `QtSharp` 的包
属于另一个项目。

下载或克隆本仓库，在仓库根目录执行以下命令，将本地路径替换成解压目录。

Windows / PowerShell：

```powershell
dotnet restore samples/hello/Hello.csproj --source C:/Downloads/QtSharp-packages
dotnet run --project samples/hello/Hello.csproj --no-restore
```

Linux：

```bash
dotnet restore samples/hello/Hello.csproj --source /home/you/Downloads/QtSharp-packages
dotnet run --project samples/hello/Hello.csproj --no-restore
```

示例按构建主机选择平台包。Linux 还需要系统 X11/OpenGL 依赖与可用的图形桌面，
详见 [Linux 环境说明](docs/development.md#linux)。

[Hello World 源码](samples/hello/Program.cs) 展示了创建 `QApplication`、显示窗口和进入
事件循环的完整过程。无需克隆仓库的新项目创建步骤见[英文快速开始](README.md#run-your-first-application)。

## 应用与示例

实际应用可参考同一作者维护的 [PolyMount](https://github.com/ProxyCheng/PolyMount)，
其界面使用 QtSharp。

仓库包含由 Qt 官方示例移植的 C# 程序：

| 场景 | 源码 |
|---|---|
| 桌面控件与列表 | [Widgets Gallery](examples/QtWidgets/WidgetsGallery.cs) |
| 文本编辑与打印 | [Notepad](examples/QtPrintSupport/Notepad.cs) |
| 自定义绘图与鼠标交互 | [Scribble](examples/QtWidgets/Scribble.cs) |
| 数据排序与筛选 | [Custom Sort/Filter](examples/QtWidgets/CustomSortFilter.cs) |
| 图表 | [Charts 示例](examples/Charts) |
| 3D 场景 | [Basic Shapes](examples/Qt3D/BasicShapesDemo.cs) |
| 媒体播放 | [Player](examples/QtMultimedia/Player.cs) |

这些示例通过仓库的[开发构建流程](docs/development.md)运行，验证范围见
[示例与测试覆盖](docs/conformance.md)。门禁把它们当作一致性测试套件：十个可执行文件共
**73 个示例（1544 条断言）**，另有 **41 个绑定测试（1029 条断言）**。

当前覆盖：**483 个类 / 9947 个方法 / 1901 个虚函数 / 517 个枚举 / 834 个 Q_PROPERTY
属性 / 475 个信号 / 72 个命名空间函数**（QtCore、QtGui、QtWidgets、QtNetwork、QtSvg、
QtSvgWidgets、QtPrintSupport、QtOpenGL、QtOpenGLWidgets）。

## 模块取舍与 .NET 集成

- 让信号、属性、继承和对象生命周期等常见 Qt 用法易于使用。
- 合理使用 C# 属性、事件、泛型、集合和委托，让 API 符合 C# 的使用习惯。
- 复用 .NET 的库和基础设施，按需适配给 QtSharp 的其他组件使用。

QtSharp 不追求绑定全部 Qt 模块。对于 .NET 已有成熟、适合 C# 使用的库，优先采用
.NET 实现：

| 明确不引入的 Qt 模块 | .NET 替代 | 与 QtSharp 的衔接 |
|---|---|---|
| QtXml | `System.Xml` | QtCore 中的 `QXmlStreamReader`/`QXmlStreamWriter` 仍可使用。 |
| QtSql | ADO.NET 数据库驱动 | `QtSharp.Sql` 将 `DbConnection` 包装成查询和表格模型。 |
| QtWebSockets | `ClientWebSocket` | 直接使用 .NET API，目前没有 Qt 接口适配。 |
| QtWebEngine | 系统 WebView（Windows 上为 Edge WebView2） | 暂未绑定。`QtSharp.WebView` 在 `QWidget` 中承载 WebView2（仅 Windows x64）；如需跨平台可考虑 Qt WebView 模块。调研见[网页视图方案](docs/webview.md)。 |

现有的 `QtSharp.Sql` 和将 .NET `Stream` 包装为 `QIODevice` 的
[`StreamIODevice`](runtime/QtCore/IoDeviceInterop.cs) 已采用这种方式。后续也会按需考虑
包装 C# 库，让 QtSharp 的模型、IO 等接口及其他组件使用。

## 与相似项目的比较

| 项目 | 定位 | 与 QtSharp 的关系 |
|---|---|---|
| [ddobrev/QtSharp](https://github.com/ddobrev/QtSharp) | 基于 CppSharp 生成的早期 C# Qt 绑定。 | 本项目是独立实现，使用不同的生成器和运行时；API 对应情况见[迁移说明](docs/legacy-issues.md)。 |
| [Qt Bridges](https://doc.qt.io/qtbridges/) | 将 C# 等语言编写的应用逻辑接入 QML/Qt Quick 界面。 | QtSharp 侧重 QWidget 和其他选定 Qt API 的通用 C# 绑定。 |

本项目与 ddobrev/QtSharp 无隶属关系，不兼容其源码或二进制 API。
QtSharp 目前没有绑定 QtQml/QtQuick。如果需要使用 QML/Qt Quick 编写界面、用 C# 编写
应用逻辑，可以使用官方的 [Qt Bridge for C#](https://github.com/qt/qtbridge-csharp)。

## 模块与限制

基础包提供 Core、Gui、Widgets、Network、Svg、SvgWidgets、PrintSupport、OpenGL 和
OpenGLWidgets 九个模块程序集。Charts、Qt3D、Multimedia、Positioning、SQL 模型和
WebView2 适配器（`QtSharp.WebView`，仅 Windows x64）为可选包，具体包名见
[英文包列表](README.md#packages)和[包架构](docs/module-architecture.md)。

目前不提供 macOS、ARM64 或 musl 包，尚未绑定 QtPdf 和 QtWebEngine，也不支持 Linux
单文件发布。部分原生窗口退出场景仍不稳定。完整说明见[已知限制](docs/limitations.md)。

## 文档与贡献

- [API 使用说明](docs/api-guide.md)
- [源码构建与测试](docs/development.md)
- [Qt 开发工具与资源编译](tools/QtSharp.Tools/README.md)
- [贡献指南](CONTRIBUTING.md)

欢迎提交问题、修复和示例。在 [Issues](https://github.com/ProxyCheng/QtSharp/issues) 中
报告问题时，请附平台、版本、操作步骤和最小复现。

QtSharp 自有代码采用 [MIT 许可证](LICENSE)，Qt 和其他依赖遵循各自的许可证。
分发前请阅读[第三方声明](NOTICE.md)。
