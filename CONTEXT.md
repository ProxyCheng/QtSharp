# QtSharp 任务上下文 (session 续接用)

## 总体目标
让 Qt Widgets 示例页 (examples-widgets.html) 的 demo 都能用 QtSharp 跑起来,示例尽量与官方 C++ 版本一致;并已修复 CppSharp 绑定生成器的 vtable hook sret 返回值(RAX)bug。`testapp.exe` 不带参数即可同时平铺显示全部 13 个 demo 顶层窗口。

## 关键背景
- 仓库:`D:\QtSharp`(QtSharp + vendored CppSharp)
- 之前的 "sret 参数在前" 修复 **方向是错的**:MS x64 ABI 的成员函数 sret 顺序是 `this`(RCX)在前、sret 指针(RDX)在后;Itanium 才是 sret 在前。之前的修复把 MS ABI 的 vtable hook 也改成 sret 在前,导致 `_SizeHintDelegateHook` 等 vtable hook 的 `__instance` 拿到垃圾指针 → `No managed instance was found` 崩溃。
- 本轮正确修复见下。f1/f2 类型退化 bug 与 sret 顺序无关,是 scope 裁剪/默认参数处理的独立问题。

## 关键构建知识
- `FunctionExtensions.cs` 在 `CppSharp/src/AST/` 中,编译进 **CppSharp.AST.dll**。改它必须重建 `CppSharp.AST.csproj`(带 `-o D:\QtSharp\lib`)。
- `CSharpSources.cs`、`SymbolsCodeGenerator.cs` 在 `CppSharp/src/Generator/` 中,编译进 **CppSharp.Generator.dll**(`tools/rebuild-generator` 项目,引用预编译 lib)。
- gen 运行**易 flaky**(类型退化 f1/f2、QColorDialog 错位、mask 未定义等随机出现),必须用 retry 循环直到 `dotnet build testbind` 0 错误。
- 生成产物 `out/`、`testbind/QtSharp.*.cs` 被 .gitignore,不入库。

## 构建/重新生成命令序列
1. `dotnet build D:\QtSharp\CppSharp\src\AST\CppSharp.AST.csproj -c Release -o D:\QtSharp\lib`
2. `dotnet build D:\QtSharp\tools\rebuild-generator\CppSharp.Generator.csproj -c Release` 然后拷贝 dll 到 `lib\CppSharp.Generator.dll` 和 `gen\bin\Release\net10.0\win-x64\`
3. `dotnet build D:\QtSharp\gen\gen.csproj -c Release`
4. 删除 `out\qt_min\*.cs,*.cpp`,运行 `qtgen.dll`(retry 直到干净)
5. 拷贝 .cs 到 testbind + `build_glue.bat` 重建 native dll + `dotnet build testbind`

## 本轮完成:ABI 顺序修复(正确方案)
1. `FunctionExtensions.cs` 的 `GatherInternalParams` 增加 `realAbiOrder` 参数:
   - `realAbiOrder=false`(wrapper DllImport/C++ glue):sret 恒在 `this` 前(内部约定一致即可)。
   - `realAbiOrder=true`(vtable hook):MS ABI `this` 在前、sret 在后;Itanium sret 在前(匹配真实 ABI)。
2. `CSharpSources.cs` 的 `GenerateVTableMethodDelegates` 传 `realAbiOrder: true`。
3. `SymbolsCodeGenerator.cs` 的 `WriteMethodWrapper` 保持 sret 在前(与 wrapper 一致)。
4. 验证:`_SizeHintDelegateHook(__IntPtr __instance, __IntPtr @return)`(this 在前);wrapper 仍是 `DisplayText(__IntPtr @return, __IntPtr __instance)`(sret 在前)。两者类型都是 `(IntPtr,IntPtr)`,delegate 类型名不变,靠 hook 参数名区分语义。

## 已完成:f1/f2 类型退化(独立 bug)
- 症状:参数类型退化 `QObject* parent → int f1`、`Qt::SortOrder → int f2`,或默认参数 `nullptr → f1`。是 scope 裁剪导致类型不可达。
- `gen/Program.cs` 修复:`CollectRoots` 对 `Qt` 命名空间所有 enum 调 `AddReachable`;`QAbstractTableModel` 加入 RootNames;`QVariant` 从 ForceIgnoreNames 移除;`PrivateShared` 加入 ForceIgnoreNames。临时 `DebugDumpParams` 已删。
- 注意:该 bug 是 flaky 的,retry 时仍可能随机出现,需重试到干净。

## 已完成:Widgets Gallery 移植
- 新增 `testbind/WidgetsGallery.cs`(`gallery` 命令),覆盖 Buttons/ItemViews/SimpleInput/Text/ProgressBar/Style 切换。
- 关键坑:
  1. **必须 keep alive**:所有 `new` 的 widget 用 `keep()` 加入 `List<object> keepAlive` 字段,否则 GC 触发 finalizer `FreeHGlobal` 导致原生崩溃。
  2. **QCommandLinkButton 会原生崩溃**(0xC000041D,~10s 后,GC 触发)。已确认:不放进 layout 就 OK,放进 layout(被 reparent)后 GC 崩溃,疑似 reparent 后 ownership 未更新导致 double-free。已从 demo 移除。
  3. **QTimer 信号回调本身是好的**(单独测试 11 次 tick 无崩溃)。gallery 的 progress bar 动画已恢复。

## 已完成:Document Viewer(纯文本版)
- 新增 `testbind/DocumentViewer.cs`(`docviewer` 命令):QTextBrowser + QFileDialog.GetOpenFileName + System.IO.File.ReadAllText 显示文本文件。PDF/JSON 部分依赖 QtPdf/QJson(未绑定),未实现。

## demo 状态(20s 冒烟测试全 OK)
sliders / spinboxes / windowflags / lineedits / shapedclock / calendar / systray / scribble / shortcut / analogclock / calculator / gallery / docviewer

## 本轮完成:抽象类构造修复
1. **抽象类 ctor 修复**:之前抽象类(QAbstractTableModel 等)的构造函数被 `.def` 直接 re-export,导致 C# 直接调用抽象类 ctor(UB)。修复:
   - `GenerateSymbolsPass.NeedsSymbol`:抽象类的 ctor 强制生成 concrete subclass wrapper(加 `IsGenerated` 检查避免包装被裁剪的类)。
   - `ClassExtensions.GetAbstractMethods`:用「所有 override」(非仅 pure)来移除基类纯虚方法,避免对 `final` 方法生成 override。
   - `gen/Program.cs AddMethodDef`:`wrapped` 优先于 `exports`,让抽象 ctor 映射到 wrapper。
   - 结果:`.def` 现在有 `??0QAbstractTableModel@@QEAA@PEAVQObject@@@Z = QAbstractTableModel_QAbstractTableModel____S_QObject`,glue 正常编译。

## 本轮核心修复:模型 vtable hook 的 sret 返回值(RAX)★★★
**症状**:任何 C# 子类化的 `QAbstractItemModel` / `QStandardItemModel` / `QAbstractTableModel`(即全量 vtable hook)设为 view 的 model 后 native 崩溃 `0xC0000005`。

**根因(用自建 native VEH + StackWalk64 tracer 抓到真实栈 + capstone 反汇编确认)**:
- 崩溃点 `QHeaderView::sectionSizeFromContents` → `QVariant::toString()`,`RCX=0`。
- 反汇编该处:`lea rdx,[rbp+0x60]`(sret 缓冲)→ `call [vtable+0x98]`(headerData)→ **`mov rcx, rax`** → `call [IAT: QVariant::toString]`。
- **MS x64 / Itanium ABI:返回非平凡 struct 的函数在隐藏 sret 缓冲之外,还要把该缓冲指针放到返回寄存器(RAX)**。native caller 直接 `mov rcx, rax` 复用该指针。
- 我们的 vtable hook 是 `void`,CLR thunk 不设置 RAX → RAX=0 → native 侧 `QVariant::toString()` = `this=nullptr` → 崩。**与 QVariant 内容无关**(写 null QVariant 也崩)。
- widget 之所以"正常":其 hook 的返回值多为 void/bool/QSize,调用方不依赖 RAX;sret 类 hook(`inputMethodQuery` 返回 QVariant)极少被调用,所以没暴露。

**修复(3 处,均在 CppSharp.Generator)**:
1. `DelegatesPass.VisitMethodDecl`:若 `method.HasIndirectReturnTypeParameter`,把 delegate 的 `ReturnType` 设为 `void*`(⇒ delegate 类型名从 `Action_…` 变 `Func___IntPtr_…`),使 CLR thunk 把返回值放 RAX。
2. `CSharpSources.GenerateVTableMethodDelegates`:同样条件下 `retType = TypePrinter.IntPtrType`(`__IntPtr`),让 hook 签名返回指针。
3. `CSharpSources.GenerateVTableManagedCall`:sret 分支在写完拷贝后追加 `return <sretParam.Name>;`(即 `return @return;`)。
- 生成效果:`private static __IntPtr _HeaderDataDelegateHook(__IntPtr __instance, __IntPtr @return, …) { … cctor(@return, ___ret.__Instance); return @return; }`,delegate = `__IntPtr Func___IntPtr___IntPtr___IntPtr_int_…(…)`。

**验证**:`probe`(QAbstractTableModel 子类)、`shortcut`(真实 ShortcutEditor)、13 个 demo 全 8s+ 无崩溃。

**保留的调试工具**:`native/CrashTracer.cpp` + `build_crashtracer.bat`(VEH+StackWalk64+dump 寄存器/反汇编字节到 `crash_stack.txt`)、`tools/disasm.py`(capstone 按 RVA 反汇编)。C# 侧 `InstallCrashTracer()` P/Invoke 即可。

## demo 状态(8s 冒烟测试全 OK)
sliders / spinboxes / windowflags / lineedits / shapedclock / calendar / systray / scribble / shortcut / analogclock / calculator / gallery / docviewer

**"all" 模式**:`testapp.exe` 不带参数(或未知参数)= 同时创建全部 13 个 demo 各自的顶层窗口并平铺显示(`testbind/AllDemos.cs` 的 `AllDemos.CreateAll()` + `ShowTiled()`),一个进程即可同时测试一整套。`Program.cs` 的 `switch` 在未匹配时返回 `null` → 走 all 分支。(曾误把 `"sliders"` 映射为 `ShortcutWindow`,已修正。)

## 本轮完成:示例与官方 C++ 对齐
对照 Qt 6.11 官方源码(`code.qt.io`/`raw.githubusercontent.com`,`qt/qtbase` 分支 6.11)逐个核对并改写:

- **Calculator**(`examples/widgets/widgets/calculator`):原 C# 是简易 4 则运算,现完整重写为官方版——新增 `CalcButton : QToolButton`(带 `sizeHint` 重写),完整键盘布局(内存 MC/MR/MS/M+,Sqrt/x²/1/x,±,Backspace,Clear/Clear All,.),以及官方的 pending-operator 计算逻辑与 `calculate/abortOperation`。
- **Scribble**(`examples/widgets/widgets/scribble`):补齐 File(Open/Save As…/Print/Exit)、Options(Pen Color/Pen Width/Clear Screen)、Help(About/About Qt)菜单;`openImage`/`saveImage`/`maybeSave`/`saveFile`/`closeEvent`;画笔改为 `QPen(color, width, SolidLine, RoundCap, RoundJoin)`;`paintEvent` 用 dirty rect;`update(QRect)` 局部刷新。
- **CalendarWidget**(`examples/widgets/widgets/calendarwidget/window.cpp`):从 1 个分组扩为 4 个分组(Preview / General Options / Dates / Text Formats),含 weekday/weekend 颜色、header 文本格式、First Friday in blue、May 1 in red、min/current/max date edits。
- **ShapedClock**:补 `mousePress/Move/ReleaseEvent` 拖拽(`dragPosition`)、Ctrl+Q 快捷键。
- **SpinBoxes**:补 `SetFormatString(formatComboBox->currentText())` 首次调用;补 double precision 分组的 group-separator spin box + 复选框。
- **SystemTray**(`examples/widgets/desktop/systray/window.cpp`):补 Icon 分组(icon label/combo)、"Disable…"类文案、type combo 5 项 + itemData、Duration warning label、Show Message 默认按钮、消息网格布局与 stretch、`setIcon`、`closeEvent`(常驻托盘)、`showMessage` 的 custom-icon 分支等。
- **WidgetsGallery**:补 "&Disable widgets" 复选框(联动 4 个分组 disabled)、tool button 与 menu button 改为并排 HBox、帮助文案。
- 已核对 **LineEdits / Sliders / WindowFlags / AnalogClock** 与官方一致(仅命名风格/`QPainterStateGuard` 等 C# 等价改写)。

新增 `testbind/QtCompat.cs`:`QTextCharFormat.SetForeground` 的公开包装被生成器裁剪,用同程序集内的 `QTextFormat.__Internal.SetForeground` 重新暴露;另加了 Shortcut Editor 需要的几个 marshal 辅助(见下)。

## 本轮完成:Shortcut Editor 完整移植(官方 4 文件对齐)
对照 `examples/widgets/widgets/shortcuteditor` 完整改写 `testbind/ShortcutEditor.cs`,现在与官方一致:
- `ActionManager`(C# 单例,等价官方 `Application` 内的 `ActionManager`):`RegisterAction(name, shortcut, context, category)`,维护 context/category 映射;
- `ShortcutEditorModel : QAbstractItemModel`:真正的**分层只读/可写模型**(context→category→action 三层),`Index/parent/RowCount/ColumnCount/Data/HeaderData/Flags/SetData` 全部按官方重写,用 `CreateIndex(row,col,ulong id)`+`InternalId` 存取节点,SortedDictionary 复刻 QMap 排序;
- `ShortcutEditorDelegate : QStyledItemDelegate`:`CreateEditor/SetEditorData/SetModelData/UpdateEditorGeometry` 重写,编辑器为 `QKeySequenceEdit`,`EditingFinishedSignal` 触发 `CommitData/CloseEditor`;
- `ShortcutEditorWidget`:QTreeView + `SetItemDelegateForColumn(1, delegate)` + `sizeAdjustPolicy = AdjustToContents` + `AllColumnsShowFocus` + `Header.ResizeSection(0,250)`;
- `ShortcutWindow : QMainWindow`:顶/底两个按钮,注册 top/bottom 的 color 动作并连 `TriggeredSignal` 改按钮文字(等价官方 MainWindow)。

### 为移植新增的生成器/绑定能力
1. **跨 TU 重复声明修复**:parser 会为同一 C++ 类型产生多份 `Declaration`(如 `QWidget`/`QStyleOption` 分别经 `qstyle.h`/`qcommonstyle.h` 到达),导致 `TagType.Equals`/`ResolvesTo` 用引用比较失败,重载/抽象方法匹配不到(典型:`QCommonStyle` 不 override `QStyle` 纯虚 → CS0534)。`CppSharp/src/AST/Type.cs` 的 `TagType.Equals/GetHashCode` 改为用 clang **USR** 比较(无 USR 时回退引用)。
2. **QFlags 返回的 sret 处理**:放开 `QAbstractItemModel::flags`(`gen/Program.cs` 的 `QFlagsReturnWhitelist`),并让 `CSharpSources.cs` 的间接返回(sret)分支对 `QFlags` 写 `int __ret;` 而不是不存在的 `QFlags.__Internal`(QFlags 映射为 int)。
3. **新头文件/类型**:`gen/Program.cs` widgets 增加 `qabstractitemdelegate.h`/`qstyleditemdelegate.h`/`qstyleoption.h`;RootNames 增加 `QAbstractTableModel`/`QAbstractItemDelegate`/`QStyleOptionViewItem`/`QStyleOption`。生成出 `QStyledItemDelegate`、`QStyleOptionViewItem`(含 `Rect`)、`QAbstractItemModel.Flags(int)`。
4. **手写 native shim**(`native/QtSharp_inline.cpp`,经 `__declspec(dllexport)` 导出,**不需改 .def**):`qs_QKeySequenceEdit_setKeySequence_string`/`keySequence_string`、`qs_QAction_setShortcut_string`/`shortcut_string`(QKeySequence 的 QString 构造/toString 未被绑定)、`qs_QAbstractItemModel_emit_dataChanged`(dataChanged 参数含 QModelIndex/QList<int>,信号生成器无法表达);Document Viewer 又加了 `qs_QSettings_*_string`(Qt6 的 QSettings key 是 `QAnyStringView`,C# 无法构造)与 `qs_QPixmap_fromImage`/`qs_QPixmap_scaled`(见下)。C# 侧包装在 `testbind/QtCompat.cs`。
- **注意**:生成器对「返回非平凡值类型」的直接调用(sret)只做位拷贝后销毁临时对象,会破坏 `QPixmap` 等隐式共享数据(表现为 null pixmap)。故 `QPixmap.FromImage`/`Scaled` 改用 native shim 做拷贝赋值。同理,`QAnyStringView`/`internalPointer` 等内联符号无导出,运行时 EntryPointNotFound,必须走 shim 或改用已包装的等价 API(`InternalId`)。
- 注意:`QModelIndex::internalPointer/internalId` 是 Qt 头内联函数,无导出符号;用的是 `internalId()`(`QModelIndex.InternalId`,该项目已生成 wrapper),不要用 `index.InternalPointer`(运行时会 EntryPointNotFound)。

## 本轮完成:Document Viewer 移植(官方 app 骨架对齐)
对照 `qtdoc/examples/demos/documentviewer/app` 重写 `testbind/DocumentViewer.cs`:
- `DocumentViewerWindow : QMainWindow`:官方 `mainwindow.ui` 的等价结构 —— File(Open/Recently opened…/Print/Exit)、Edit(Cut/Copy/Paste)、Help(About/AboutQt)菜单,ToolBar(Open/Print/Back/Forward),StatusBar,QSplitter 左 QTabWidget(Pages/Bookmarks)+ 右 widgetResizable QScrollArea;`CloseEvent` 里用 `QSettings` 存 WorkingDir/最近文件;
- `RecentFiles`(QSettings 持久化,`QVariant(string[])` 存 QStringList)+ 每次变更重建最近文件子菜单;
- 查看器按内容类型内联:Text=`QPlainTextEdit`、JSON=`JsonTreeModel : QAbstractItemModel`(System.Text.Json → Key/Value 两列树)、Image=`QLabel` + Back/Forward 与缩放(`QtCompat.PixmapFromImage/PixmapScaled`);Print 动作禁用(QtPrintSupport 缺失)。
- 新增绑定:`gen/Program.cs` RootNames 增加 `QSettings`(qsettings.h 已在头文件列表)。

### 受绑定限制、与官方仍有差异的地方
- **DocumentViewer**:已按官方 `qtdoc/examples/demos/documentviewer/app` 重写主窗口骨架(File/Edit/Help 菜单、ToolBar、StatusBar、QSplitter + 西侧 QTabWidget(Pages/Bookmarks) + widgetResizable QScrollArea、QSettings 持久化 WorkingDir/最近文件、About/AboutQt、Back/Forward 滚动)。官方查看器是**原生插件**(QPluginLoader,无法从托管加载),C# 改为按内容类型内联选择:TxtViewer=QPlainTextEdit、JsonViewer=自建 `JsonTreeModel : QAbstractItemModel`(System.Text.Json 解析,非 Qt QJsonDocument)、ImageViewer=QLabel+缩放。**PDF 不可用**(运行时不带 QtPdf DLL);打印不可用(QtPrintSupport DLL 缺失);**拖放未实现**(QMimeData/QUrl 列表未绑定);i18n(Translator/.qm)未移植。
- **Calendar**:官方 General Options 的 **Locale 行**依赖 `QLocale::matchingLocales()`/`territory()`(未绑定)已省略;`QDateTimeEdit::dateChanged` 信号未绑定 → 用 `editingFinished` 近似。
- **SystemTray**:官方图标来自示例资源 PNG(未随绑定分发)→ 用 `QMessageBox::standardIcon` 替代;`QStyle::standardIcon`、`QSystemTrayIcon::activated`、`QDialog::setVisible` 重写均未绑定。
- **Scribble**:`QImageWriter::supportedImageFormats()` 未绑定 → Save As 用固定格式列表;`QMouseEvent::buttons()` 未绑定 → 以 `scribbling` 状态等价替代;打印部分(QtPrintSupport)不可用,`Print()` 为空实现。
- **WidgetsGallery**:`QFileSystemModel`、`QStandardItem`/图标模型未绑定 → Tree View 用 `QTreeWidget` 占位;Color Scheme 组合框(需 `QStyleHints`)未加;`QCommandLinkButton` 因 native 崩溃仍不加入。
- 共性缺口(未绑定):`QMouseEvent/PointerEvent::buttons()`、`QPainter::setPen(QPen)`、`QStyleHints`、`QFileSystemModel`、`QStandardItem`、`QTextFormat::setForeground` 公开包装、`QDateTimeEdit` 信号、`QLocale::matchingLocales`、`QSystemTrayIcon::activated`。若要 100% 对齐,需扩展生成器可达性/信号集并重生成(注意 gen flaky)。

## 未解决的问题
- **QCommandLinkButton** 放进 layout 后原生崩溃(0xC000041D,GC 触发)。已从 gallery 移除。
- 3 个示例不可移植:Cube RHI / Simple RHI(需 QRhi)、Window Embedding(需非 Qt 窗口嵌入);Document Viewer 的 PDF 需 QtPdf。

## 相关文件
- `CppSharp/src/AST/FunctionExtensions.cs`(GatherInternalParams 的 `realAbiOrder`)
- `CppSharp/src/AST/ClassExtensions.cs`(GetAbstractMethods 用所有 override)
- `CppSharp/src/AST/Type.cs`(TagType.Equals/GetHashCode 用 clang USR,修复跨 TU 重复声明)★
- `CppSharp/src/Generator/Passes/DelegatesPass.cs`(sret ⇒ delegate 返回 `void*`)★
- `CppSharp/src/Generator/Generators/CSharp/CSharpSources.cs`(GenerateVTableMethodDelegates 返回类型 + GenerateVTableManagedCall 的 `return @return` + QFlags sret 用 `int __ret`)★
- `CppSharp/src/Generator/Passes/SymbolsCodeGenerator.cs`(WriteMethodWrapper sret 在前)
- `CppSharp/src/Generator/Passes/GenerateSymbolsPass.cs`(抽象类 ctor 生成 concrete subclass)
- `CppSharp/src/AST/CppSharp.AST.csproj`(Version)
- `gen/Program.cs`(scope 裁剪/CollectRoots + AddMethodDef wrapped 优先 + QFlagsReturnWhitelist + itemviews 头文件 + QSettings RootName)
- `testbind/Program.cs`(命令分发;未匹配 = all)
- `testbind/AllDemos.cs`(`CreateAll()` + `ShowTiled()`,全部 demo 顶层窗口平铺)
- `testbind/QtCompat.cs`(`QTextCharFormat.SetForeground` + Shortcut Editor marshal 辅助 + QSettings/QPixmap shim 包装)
- `testbind/WidgetsGallery.cs`、`testbind/DocumentViewer.cs`、`testbind/ShortcutEditor.cs`、`testbind/Calculator.cs`、`testbind/Scribble.cs`、`testbind/CalendarWidget.cs`、`testbind/ShapedClock.cs`、`testbind/SpinBoxes.cs`、`testbind/SystemTray.cs`
- `native/QtSharp_inline.cpp`(手写 shim:QString/QKeySequence/QAction/QSettings/dataChanged/QPixmap)
- `native/CrashTracer.cpp`、`build_crashtracer.bat`、`tools/disasm.py`(ABI 调试工具)

## 提交状态
- 已提交(master):
  1. `Generator: fix sret/vtable ABI, cross-TU type identity, QFlags returns, abstract ctors`(CppSharp/gen/native shim/def)
  2. `Port official QtWidgets examples and align them with upstream`(全部 testbind,含 ShortcutEditor/DocumentViewer/QtCompat/AllDemos/WidgetsGallery)
  3. `Add native crash tracer and disassembly tooling for ABI debugging`(CrashTracer/build_crashtracer/disasm.py)
  4. 本文件(CONTEXT.md)
- 生成产物 `out/`、`testbind/QtSharp.*.cs`、`testbind/Std.cs`、`testbind/GeneratedSignals.cs`、`native/QtSharp_signals.cpp` 被 .gitignore;`native/QtSharp.def` 被跟踪(随生成器变化提交)。

## 扩展示例覆盖(进行中)
目标:把 Qt 官方 **所有** examples/widgets 都改写成 C# 并跑通(不止 examples-widgets.html 那 16 个)。

### 已完成的新增示例(除原有 13 个 demo 外)
- **Menus**(`examples/widgets/mainwindows/menus`):菜单栏 File/Edit/Format/Help、可勾选格式动作、status tip、右键 `QContextMenuEvent` 上下文菜单、状态标签回显。
- **Basic Layouts**(`examples/widgets/layouts/basiclayouts`):layout 内嵌 QMenuBar、horizontal/grid/form 分组、大编辑器、QDialogButtonBox 的 accept/reject、QScrollArea + 自定义竖直滚动条样式。
- `AllDemos` 现在平铺 **15** 个窗口;`Program.cs` 新增命令 `menus`、`basiclayouts`。

### 本轮为覆盖更多示例新增的绑定
- 头文件/根类:`QAbstractAnimation`/`QPropertyAnimation`/`QEasingCurve`、`QSortFilterProxyModel`、`QStringListModel`、`QStandardItem`、`QSyntaxHighlighter`、`QImageReader`/`QImageWriter`、`QPainterPath`、`QCompleter`、`QFileSystemModel`、`QDataWidgetMapper`、`QItemDelegate`、`QWizard`/`QWizardPage`、`QErrorMessage`、`QFocusFrame`、`QScroller`、`QUndoStack`/`QUndoCommand`/`QUndoView`。
- 事件类型:`QInputEvent`/`QContextMenuEvent`/`QHoverEvent`/`QDragEnterEvent`/`QDragMoveEvent`/`QDragLeaveEvent`/`QDropEvent`/`QHelpEvent`/`QActionEvent` + `QMimeData`(qmimedata.h)。
- **信号生成器改进**(`gen/Program.cs`):解析**无名参数**;忽略 `QPrivateSignal` 标记(既不出现在托管签名,也不出现在 connect lambda;成员指针强转只在**重载信号**时使用,因为 QPrivateSignal 无法在类外命名)。
- 注意:`QSpinBox/QDoubleSpinBox::valueChanged` 现在正确带值(此前因无名参数 bug 变成 0 参);`Sliders.cs`/`SpinBoxes.cs` 已改为 `_ =>`。

### 踩过的坑(生成器脆弱点,务必 retry)
- 生成**很不稳定**:同一份源码反复跑会出现 `mask`/`other` 未定义、`nint +=`、`PopBack`、`QFlagsStorage`/`QTextEdit.operator_` 之类**错误重命名**、`QGridLayout` 不实现抽象成员、`QSettings(string,string)` ctor 消失、`QScrollArea.WidgetResizable` 变只读等。**必须**用"生成→编译 testbind→编译 glue"全部通过的 retry 循环(见下)。
- 加入 `qmdiarea.h`/`qmdisubwindow.h` 会触发 CppSharp 的 `event`→`QFlagsStorage` 重命名 bug(QPushButton 等被污染),**已放弃 MDI**。
- `QSyntaxHighlighter` 仍未生成(头文件已加、RootName 已加,原因未查明)。

### retry 命令(临时脚本,未入库)
```
powershell -File %TEMP%\codemaker\regen2.ps1            # 重建 AST/Generator/gen + 重试直到 testbind+glue 干净
powershell -File %TEMP%\codemaker\regen2.ps1 -SkipBuildTools   # 只重跑生成
powershell -File %TEMP%\codemaker\regen_once.ps1        # 单次生成(排查用)
```

### 下一批示例需要补的绑定(已确认缺口)
1. **painting 组**(affine/basicdrawing/composition/deform/gradients/imagecomposition/painterpaths/pathstroke/transformations)需要:
   - `QLinearGradient`/`QRadialGradient`/`QConicalGradient` 加入 RootNames(qbrush.h 已解析)。
   - `QPainter::setPen(QPen)`/`setBrush(QBrush)` 实际是 **`QPainter.Pen`/`QPainter.Brush` 属性**(可用);`new QPainter(QWidget)` 用 `QPainter(IQPaintDevice)`。
   - 示例用的 `:/images/*.png` **资源不存在**(无 qrc),`Pixmap`/`Texture` 分支需用程序化贴图或跳过。
2. **animation/easing** 需要 `QGraphicsScene`/`QGraphicsView`/`QGraphicsObject`(+ `QPropertyAnimation` 已绑定)以及自定义 `PixmapItem : QGraphicsObject`。
3. **itemviews**(addressbook/simpletreemodel/editabletreemodel/spreadsheet 等)大体已可用(`QSortFilterProxyModel`/`QStandardItem` 已绑定);`stardelegate` 需 `QStyledItemDelegate`(已绑定)。
4. **richtext**(orderform/syntaxhighlighter)需修好 `QSyntaxHighlighter`;textobject 需 `QTextObjectInterface`(未绑定)。
5. **graphicsview** 整组需要绑定 `QGraphicsView/Scene/Item/...` 家族(未绑定)。
6. **tools**(completer/regexp/settingseditor/undoframework 已具备大部分;plugandpaint 需插件机制,不可行)、**touch**(需 `QTouchEvent`/`QEventPoint`)、**gestures**(需 `QGesture`)、**draganddrop**(`QMimeData`/QUrl 列表,部分可行)。
7. **不可行**:RHI 两个(需 QRhi + `.qsb` shader blob)、Window Embedding(需嵌入非 Qt 窗口)、quartz/other 平台示例、QtStateMachine(模块未安装)、QtSvg/PrintSupport(运行时不带 DLL)。

## 架构 PoC:通用 moc 信号派发(已验证成功)
**动机**:现有 `QtSharp_signals.cpp` 是"扫头文件 → 每个信号生成一段编译期 C++ glue → `static_cast<void (Class::*)(...)>` 定位成员指针",因此**重载信号有歧义、`QPrivateSignal` 是 private 无法命名、无名参数解析不出**。PySide 之所以没这些问题,是因为它走 moc 元数据 + 通用 metacall。

**PoC**(`native/QtSharp_meta.cpp` + `testbind/MetaSignal.cs` + `testbind/MetaSignalTest.cs`,`testapp metasig`):
- 用一个 `QtPrivate::QSlotObjectBase` 子类作**唯一转发器**,通过**私有 API** `QObjectPrivate::connect(sender, signal_index, slotObj, type)` 挂接;
- 信号在**运行期**由 `sender->metaObject()->indexOfSignal(normalizedSignature)` 定位,类型 id 取自 `QMetaMethod::parameterMetaType(i).id()`,实参 `void**`(args[0] 是返回槽)直接交给托管侧按 QMetaType id 解包;
- 需要私有头:`-I"%QT_INC%\QtCore\6.11.0" -I"%QT_INC%\QtCore\6.11.0\QtCore"`(已加进 `build_glue.bat`)。

**验证结果(全部 PASS)**:int / QString / bool 参数投递;**头文件里无名参数**(`QSpinBox::valueChanged(int)`)正常;**`QCompleter::activated` 两个重载解析成不同 index(4/5)并各自可连**;伪造签名被拒;`QTimer::timeout` 可连。

**关键发现(直接影响架构决策)**:
```
[info] QTimer signal(0) = destroyed(QObject*)
[info] QTimer signal(1) = destroyed()
[info] QTimer signal(2) = objectNameChanged(QString)
[info] QTimer signal(4) = timeout()
```
**moc 在运行期签名里把 `QPrivateSignal` 标记剥掉了**(`timeout(QPrivateSignal)` → `timeout()`),而且重载在元数据里就是两条不同签名。也就是说:我们之前拼命绕的那个"命名不了 QPrivateSignal"的问题,**在 moc 元数据层面根本不存在**——这套通用派发天然解决它。

**结论**:产品的信号层应当走"moc 元数据 + 单一转发器",而不是逐信号生成 glue;这条路线已被 PoC 证明可行,且与最终是否换掉 CppSharp 无关,值得优先落地。

## 架构 PoC #2:通用虚函数派发(已验证成功)
**动机**:现在 C++→C# 的虚函数/事件重写靠 CppSharp 的**运行期 vtable thunk**(`Marshal.GetFunctionPointerForDelegate`),我们为此踩了 MS x64 的 sret/`this` 顺序/RAX 回填一堆坑(见前面 `_HeaderDataDelegateHook` 那段)。

**PoC**(`native/QtSharp_vdispatch.cpp` + `testbind/VirtualDispatch.cs`,`testapp vdispatch`):
- 生成**真正的 C++ 子类**(`QsHostWidget : QWidget`、`QsHostModel : QAbstractItemModel`),override 里把参数装进 `void* argv[]`(约定 `argv[0]` 是返回槽),转调**唯一**的托管入口 `void(void* userData, int methodId, void** argv)`;
- 托管入口用 `[UnmanagedCallersOnly(CallConvs = new[]{ typeof(CallConvCdecl) })]` 静态方法,函数指针由 `(delegate* unmanaged[Cdecl]<...>)&Dispatch` 取得,`userData` 是 `GCHandle`。

**验证结果(全部 PASS)**:
- `QWidget::sizeHint()`(**QSize 按值返回**)由 C# 算出 137x42 并正确传回;
- `paintEvent` 在 Qt 真正渲染(`grab()`)时回调到 C#;
- `QAbstractItemModel::rowCount()` 回调到 C#;
- `GC.Collect()` + `WaitForPendingFinalizers()` 之后派发依然正常(**函数指针指向静态代码,不会被移动/回收,不需要 delegate keep-alive**);
- **Qt 库内部**调用虚 `data()`(经 `QAbstractItemModel::match()`)**返回托管构造的 QVariant 成功**——正是当年需要 sret/RAX 回填补丁的那条路径。

**结论**:
1. **ABI 归编译器管**:只要虚函数由 C++ 子类 override,managed 侧永远只看到普通指针,**sret/this 顺序/RAX 这类问题从根上消失**;`void** argv` + `argv[0]=返回槽` 的约定对所有虚函数统一(和信号派发同一套思路)。
2. **回调生命周期问题消失**:`[UnmanagedCallersOnly]` + `delegate*` 是静态代码地址,不像委托需要保活/防止被移动。
3. 代价:每个被绑定的类需要一份**生成的 C++ wrapper 并编译**(与 PySide 的 shiboken wrapper 同构),也就是产品按 Qt 版本/平台出原生包的原因。

### 两个 PoC 合起来说明的架构结论
**信号层用"moc 元数据 + 单一转发器"(PoC #1),虚函数层用"生成的 C++ wrapper + 单一托管入口"(PoC #2)** —— 两条都不再依赖 CppSharp 的运行期 thunk/逐信号 glue,且都验证过。这与"最终用不用 CppSharp"无关,是产品可以独立落地的最小骨架。

## 下一 agent 可继续的方向
1. **继续按上面的批次补绑定 + 移植示例**,每批完成后 `all` 冒烟。
2. **落地通用信号派发**(PoC 已成功,见上节):把 `GeneratedSignals.cs` 的逐信号 glue 换成 `MetaSignal` 的 moc 元数据派发,可一次性解决重载/无名参数/QPrivateSignal/任意参数类型问题。下一个 PoC 建议做 `[UnmanagedCallersOnly]` 虚函数派发,验证能否替代现在的 vtable thunk(并验证 sret 问题是否消失)。
3. **提交整理**:本轮已提交 `Expand bindings…`、`Bind event types…`、`Port Menus and Basic Layouts…`、`PoC: generic moc-driven signal dispatcher`。
4. **优先修 `QPainter` 相关绑定**(gradients + 确认 QPainterPath 方法),它一次解锁 9 个 painting 示例,性价比最高。
5. 复现/验证任何 native 崩溃:用 `native/CrashTracer.cpp`(VEH+StackWalk64)+ `tools/disasm.py`(capstone)。
