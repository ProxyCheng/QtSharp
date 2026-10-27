# QtSharp

[English](README.md) | 简体中文

**使用 C# 编写 Qt 桌面应用，支持 Windows 和 Linux。**

QtSharp 将 Qt 6 的 Widgets、绘图、网络，以及可选的图表、3D、多媒体模块带到 .NET。
你可以使用熟悉的 Qt 类，通过 C# 属性、事件和虚方法重写编写界面逻辑。
发布包包含 Qt 运行库与原生绑定，应用开发者无需另装 Qt SDK 或 C++ 编译工具。

**项目尚未发布 1.0，API 仍可能在后续版本中大幅调整，包括类型名称、方法签名和行为。
1.0 之前不保证版本间的源码或二进制兼容。** 使用时请固定包版本，升级前查看
[更新日志](CHANGELOG.md) 并测试你的应用。

目前支持 **.NET 10、Qt 6.12.0、Windows x64 和 Linux x64（glibc）**；
暂不支持 Qt Quick/QML、macOS 或 ARM64。

实际应用可参考同一作者维护的 **[PolyMount](https://github.com/ProxyCheng/PolyMount)**，
其界面使用 QtSharp。

## 可以做什么？

| 场景 | C# 示例源码 |
|---|---|
| 桌面控件、表单与列表 | [Widgets Gallery](demos/QtWidgets/WidgetsGallery.cs) |
| 文本编辑与打印 | [Notepad](demos/QtPrintSupport/Notepad.cs) |
| 自定义绘图与鼠标交互 | [Scribble](demos/QtWidgets/Scribble.cs) |
| 数据排序与筛选 | [Custom Sort/Filter](demos/QtWidgets/CustomSortFilter.cs) |
| 图表 | [Charts 示例](demos.charts)，可选模块 |
| 3D 场景 | [Basic Shapes](demos.qt3d/BasicShapesDemo.cs)，可选模块 |
| 媒体播放 | [Player](demos/QtMultimedia/Player.cs)，可选模块 |

以上示例通过仓库开发流程构建。只想体验独立应用，可以先运行下面的 Hello World。

## 运行第一个窗口

1. 安装 **.NET 10 SDK**。
2. 在 [Releases](https://github.com/ProxyCheng/QtSharp/releases) 下载对应平台的
   `QtSharp-0.19.0-win-x64.zip` 或 `QtSharp-0.19.0-linux-x64.zip`，解压得到 NuGet 包目录。
3. 下载或克隆本仓库，在仓库根目录执行以下命令，将本地路径替换成解压目录。

Windows / PowerShell：

```powershell
dotnet restore samples/hello/Hello.csproj --source C:\Downloads\QtSharp-packages
dotnet run --project samples/hello/Hello.csproj --no-restore
```

Linux：

```bash
dotnet restore samples/hello/Hello.csproj --source /home/you/Downloads/QtSharp-packages
dotnet run --project samples/hello/Hello.csproj --no-restore
```

示例按构建主机选择平台包。Linux 还需要系统 X11/OpenGL 依赖与图形桌面，详见
[Linux 环境说明](README.md#getting-the-pieces)。

[示例代码](samples/hello/Program.cs) 使用 `QApplication` 创建应用、显示一个 `QWidget`
窗口并进入事件循环。无需克隆仓库的新项目创建步骤见[英文快速开始](README.md#run-your-first-application)。

**目前安装源是 GitHub Releases，并非 nuget.org。** 请解压完整的平台归档，保留依赖包。
nuget.org 上名为 `QtSharp` 的包属于另一个项目；本项目基础绑定的包名为
`QtSharp.Api.win-x64` / `QtSharp.Api.linux-x64`。

## 适合谁？

- 熟悉 Qt，希望使用 C# 和 .NET 库编写桌面应用的开发者。
- 希望评估 Qt Widgets 能否满足 Windows / Linux 桌面工具需求的开发者。
- 对 .NET 与 C++ 互操作、自动生成绑定感兴趣的贡献者。

选型前请查看[已知限制](README.md#known-limitations)和[示例验证范围](docs/conformance.md)。
QtSharp 自有代码采用 MIT 许可证；Qt 和可选模块有各自的许可证，分发前请阅读
[第三方声明](NOTICE.md)。

## 与 ddobrev/QtSharp 的关系

本项目是独立实现，与 [ddobrev/QtSharp](https://github.com/ddobrev/QtSharp) 无隶属关系，
也不兼容旧项目的源码或二进制。字符串列表、对象存取、布局所有权及缺失 API 的对应情况，
见[迁移说明](docs/legacy-issues.md)。

## 反馈与参与

第一次试用时遇到安装失败、缺少 API 或原生崩溃，请通过
[Issues](https://github.com/ProxyCheng/QtSharp/issues) 提供平台、版本、操作步骤和最小复现。
也欢迎分享你希望用 QtSharp 构建的应用，以及阻碍你采用它的问题。

开发与贡献方式见 [CONTRIBUTING.md](CONTRIBUTING.md)，架构、模块与完整构建说明见
[英文 README](README.md)，版本变化见 [CHANGELOG.md](CHANGELOG.md)。
