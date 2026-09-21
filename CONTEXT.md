# QtSharp 任务上下文 (session 续接用)

## 总体目标(已明确)
**做一套长期可用的 Qt for .NET 绑定**;"把 Qt 官方示例改写成 C# 并跑通"只是 **conformance suite / 验证手段**。
产品骨架已经全部就位(PoC×3 + model/codegen/runtime + 前端换成 clang),现在是**扩覆盖面 + 把旧 demo 搬过来**的阶段。
**旧链路(CppSharp)已整块删除**(见"㉔/㉕/㉖/㉗/㉘"):14 个旧 demo 全部搬成新链路的 `api-*`,3 个架构 PoC、CppSharp 源码、legacy native dll、`QtSharp.def`、`build_glue.bat` 都不在了(唯一保留的第三方是 `lib/lib/clang/19` 的 builtin headers)。新链路自测命令:`api` / `api-analogclock` / `api-lineedits` / `api-widgets` / `api-graphics` / `api-core` / `api-events` / `api-ownership` / `api-custommodel` / `api-calculator` / `api-spinboxes` / `api-shortcut` / `api-metaobject` / `api-shapedclock` / `api-sliders` / `api-calendarwidget` / `api-menus` / `api-basiclayouts` / `api-scribble` / `api-gallery` / `api-systray` / `api-docviewer` / `api-basicdrawing` / `api-gradients` / `api-painterpaths` / `api-completer` / `api-customsortfilter` / `api-stardelegate` / `api-composition`。

## 当前状态速览(给新 session)
> **想直接体验新框架**:`powershell -File D:\QtSharp\tools\regen-api.ps1`(生成 model/C#/native 并跑门禁)→ 打开 `QtSharp.slnx`(gen + testbind + samples/hello + tools/clangprobe)→ 启动 `testapp`,参数给一个 demo 名(`api` / `api-widgets` / `api-core` / `api-events` / `api-calculator` …)。`testapp` 不带参数**只打印用法**(旧的"平铺全部 demo"模式已随旧链路删除,见"㉗")。
> **想自己写界面**:照抄 `samples/hello/` —— 一个 `net10.0` 控制台工程,里面只有 `Program.cs` + `<Import Project="..\..\QtSharp.Api.props" />`(props 负责:编译生成的 `out/api/QtSharp.Api.cs` + `runtime/ApiRuntime.cs`,并把 `QtSharp.Api.dll`、Qt 运行时 DLL、`platforms\qwindows.dll` 拷到输出目录);`tools/regen-api.ps1` 会 build+run 它,所以这条路不会烂。
> **API 的几条命名约定**(不知道叫什么名字时按这几条猜):
> - `Main` 要 `[STAThread]`;开头建应用对象 —— **PyQt 那种写法 `var app = new QApplication(args);` 已可用**(见"㉓"),或者不关心命令行时用 `ApiRuntime.StartApplication()`;结尾 `ApiRuntime.Exec()`(退出用 `ApiRuntime.Quit()`)。
> - **`Q_PROPERTY` 变成 PascalCase 属性,访问器退回 C++ 原名**:`edit.Text`(属性)/`edit.text()`/`edit.SetText(...)`;`widget.Width`/`widget.width()`;`timer.Interval`/`timer.interval()`;`QMimeType.Name`。编译报 `CS1955 "X 不是函数"`/`CS1501 没有采用 N 个参数的重载` 就是这个信号 —— 去掉括号或改用属性名。
> - **信号**是 `XxxSignal` 属性(`button.ClickedSignal`、`edit.TextChangedSignal`),`Connect(handler)` 即可;`QSignal<T>` 的 `T` 就是信号的参数类型(无参数是 `bool`,所以写 `_ => ...`)。
> - **托管子类**直接 `override`,`sizeHint`/`paintEvent`/`keyPressEvent`/`mousePressEvent`/`closeEvent`/模型与委托的虚函数都在白名单里;native 侧由生成的 C++ 子类转发。
> - **所有权**:`new` 出来的对象由你负责(`Dispose()`/终结器会真的 delete);一旦交给 Qt(放进 layout、设成 parent、交给 scene/view)**就不能再删** —— 生成的析构会先问 `parent()`,所以 `Dispose()` 是安全的 no-op;Qt 删除对象后再访问包装器会抛 `ObjectDisposedException`。
> `testbind/QtSharp.Api.cs` 是生成物(被 gitignore,由 `regen-api.ps1` 产出)。

| 项 | 状态 |
|---|---|
| 旧链路(CppSharp) | 🗑️ **已整块删除**(见"㉘");旧 native dll、`.def`、`build_glue.bat`、CppSharp 源码、`rebuild-generator`、14 个 legacy demo、3 个 PoC 全不在了 |
| examples-widgets.html 16 个 | 14 个完成;剩 RHI×2、Window Embedding(不可行)、DocumentViewer(PDF/打印缺模块) |
| 架构 PoC #1 信号(moc metacall) | ✅ 已验证(源码已删,机制进了产品:`native/QtSharpApiRuntime.cpp` 的 moc 转发器) |
| 架构 PoC #2 虚函数(生成的 C++ wrapper + `[UnmanagedCallersOnly]`) | ✅ 已验证(源码已删,机制进了产品:`ApiCodeGen` 生成的 native wrapper) |
| 架构 PoC #3 所有权(Qt-owned 弱引用 + SafeHandle + destroyed 失效) | ✅ 已验证(源码已删,机制进了产品:`ApiRuntime` 的 destroyed 注册表,见"⑰") |
| 产品化第 1 步:model(入库)+ codegen | ✅ model **逐字节可复现**、codegen 纯函数(两次一致) |
| **前端迁移(已决策)** | ✅ **已完成**:`qtgen extract2`(ClangSharp)取代 CppSharp 成为默认前端,~2.5 秒产出**逐字节一致**的 model;原 44 类 model 复刻 **1472/1472** 条事实(见"路线 A 可行性验证"一节) |
| **demo 都在跑** | ✅ **29 个 demo** 全进门禁,共 **1074 项断言**(`api` 8 / `api-analogclock` 27 / `api-lineedits` 9 / `api-widgets` 85 / `api-graphics` 9 / `api-core` 108 / `api-events` 22 / `api-ownership` 24 / `api-custommodel` 19 / `api-calculator` 12 / `api-spinboxes` 25 / `api-shortcut` 18 / `api-metaobject` 14 / `api-shapedclock` 13 / `api-sliders` 40 / `api-calendarwidget` 59 / `api-menus` 55 / `api-basiclayouts` 55 / `api-scribble` 66 / `api-gallery` 78 / `api-systray` 63 / `api-docviewer` 100 / **`api-basicdrawing` 34 / `api-gradients` 18 / `api-painterpaths` 32**(见"㉚")/ **`api-completer` 29 / `api-customsortfilter` 19**(见"㉛")/ **`api-stardelegate` 17**(见"㉜")/ **`api-composition` 16**(见"㉝")),外加 **`samples/hello`**(独立工程,只 `Import` 一个 props 文件)也被门禁 build+run;另有一个**停牌**的 `WindowFlagsApi`(115 项断言全过但退出时 3/10 崩,见"㉔/㉗")。**旧链路(CppSharp)已整块删除** —— 14 个 legacy demo 全部搬成 `api-*`,3 个 PoC、CppSharp 源码、legacy native、`QtSharp.def`、`build_glue.bat` 都不在了;`testapp` 不带参数只打印用法(见"㉔/㉕/㉖/㉗/㉘") |
| **覆盖面** | 🚧 **445 类 / 9523 方法 / 858 虚函数 / 448 枚举 / 801 `Q_PROPERTY` 属性 / 428 信号**(QtWidgets 184 / QtGui 136 / QtCore 125)。**信号全类型可连、可 `Emit`,且托管类能在 C# 里声明自己的信号/属性**(`QMetaSignal`/`QMetaProperty` + 运行期 meta-object,见"㉒")。**输入事件整族已绑**(`QKeyEvent`/`QWheelEvent`/`QDrag*`/`QDropEvent`/`QTabletEvent`/`QTouchEvent`…,见"⑲"):托管 `keyPressEvent` 现在**读得到按键码**。**QtCore batch 3**(事件循环 / `QBuffer` / `QMetaProperty`/`QMetaEnum` / 哈希 / 正则匹配 / `QUrlQuery` / 排序 / mime / 文件监视 / 共享内存 / 平台表)**与富文本整族**(`QTextFormat` 及其派生 / `QAbstractTextDocumentLayout` / `QTextLine` …)已入。**托管子类能写自定义 `QAbstractTableModel`/`QStyledItemDelegate`**(见"⑮")。**Qt 删除对象后访问包装器抛 `ObjectDisposedException`**(见"⑰")。**类型类别**:`const char*`、`QList<T>`、`QStringList`、`QMap`/`QHash`→`Dictionary`、`QBindable<T>`、`QByteArrayView`/`const uchar*`→`byte[]`、`void*`→`IntPtr`、`QStringView`/`QAnyStringView`、`std::chrono`→`TimeSpan`、out 参数、非 const `QString&`→`ref string`。剩 `QList<std::pair<...>>`(6)、`QFont::Tag`(8)、`QLatin1String`(15)等低收益项(见"⑨/⑩/⑲") |

## 关键背景
- 仓库:`D:\QtSharp`(单一链路:libclang/ClangSharp → model → codegen → 生成的 C# + C++ wrapper;旧 CppSharp 已删)
- 之前的 "sret 参数在前" 修复 **方向是错的**:MS x64 ABI 的成员函数 sret 顺序是 `this`(RCX)在前、sret 指针(RDX)在后;Itanium 才是 sret 在前。之前的修复把 MS ABI 的 vtable hook 也改成 sret 在前,导致 `_SizeHintDelegateHook` 等 vtable hook 的 `__instance` 拿到垃圾指针 → `No managed instance was found` 崩溃。
- 本轮正确修复见下。f1/f2 类型退化 bug 与 sret 顺序无关,是 scope 裁剪/默认参数处理的独立问题。

## 已决定:前端从 CppSharp 换成 ClangSharp(路线 A)
### 为什么
新链路**只**从 CppSharp 要两件事:①驱动 clang 解析 Qt 头(MSVC 兼容配置)②把 clang AST **投影**成 `CppSharp.AST` 托管对象。passes / type maps / codegen / glue 早就不用了。而本轮所有故障都出在第 ② 层:

| 症状 | 位置 |
|---|---|
| 参数类型张冠李戴,37 类连续 5 次运行 5 个哈希 | `DeclConverter` / `TypeConverter` |
| `InvalidOperationException: Only dependent classes have template parameters` | `DeclConverter.VisitClassTemplate`(它自己的断言) |
| 跨 TU 类型身份必须用 USR 才相等 | `Type.cs`(我们 patch 了 fork) |
| `Type.Desugar()` 顺序依赖、`CompleteDeclaration` 永远 null | `Type.cs` / 转换器 |
| `Files not found: 'a.h,b.h,...'`(列整块) | `ParserOptions` |

clang 本身没问题,我们的 model/codegen/runtime 也没问题 —— **是那个转换层**。

### 核心判断:不是"从零",是"换掉一层"
```
根头文件 → clang → [CppSharp 投影] ← 换掉这一层
        → ApiModel(词汇表/规范化) → model.json → codegen → C# + C++ wrapper → 薄运行时
```
**上游接口(model JSON)已定死,下游一行都不用动** —— 这是本轮架构最大的红利:"换前端"变成一次**可验证的替换**(用现成的 model diff 门禁对拍),不是重写产品。

### 三条路线(已比较,选 A)
| | A. clang C API(ClangSharp / libclang) ✅选它 | B. 自写 C++ dumper 吐 JSON(备选) | C. 继续 CppSharp |
|---|---|---|---|
| 新代码 | ~800–1200 行 C# | ~600–1000 行 C++ + 构建 | 0 |
| 可复现性 | **构造上成立**(cursor 是无状态游标,没有"转换"这一步) | 同 A | 靠"分块 + 两次一致"(现 165s) |
| 崩溃/诊断 | 不会 / 完全可控 | 同 A | 已遇到 / 不可控 |
| extract 耗时 | 预计 **10–20s**(一次解析) | 同 A | 165s(每块两次) |
| 依赖 | libclang + ClangSharp 包 | 同 A + native 工具 | 维护 ~20 文件 fork |

### 已探明的环境事实(新 session 可直接用)
- **`libclang.dll` 不在盘上**,`C:\Program Files\LLVM\` 不存在;`D:\QtSharp\lib\native\` 里只有 `CppSharp.CppParser.dll`(78MB)—— 那是 **CppSharp 自己的 C++ wrapper**(已确认**不导出任何 `clang_*` 符号**),不能复用。
- **NuGet 可达**;可用组合(已实测):`ClangSharp 21.1.8.2`(托管,提供 `ClangSharp.Interop` 的低层 C API + `ClangSharp` 高层 `TranslationUnit`/`Cursor`/`Decl`)+ `libClangSharp.runtime.win-x64 21.1.8.2`(native `libClangSharp.dll`,**不带 builtin headers**)。
  - 没有 19.x 的运行时包(`libclang.runtime.win-x64` 只有 8–18、20、21、22),所以 builtin headers 只能借 `D:\QtSharp\lib\lib\clang\19`(见"已知限制")。
  - `CppSharp.CppParser.dll` 里**没有** `lib/clang/*/include`,但 `D:\QtSharp\lib\lib\clang\19\include` 是完整的 clang 19 内置头。
- `dotnet --version` = **10.0.400**。
- 机器的 Qt: `D:\QtSharp\Qt\6.11.0\msvc2022_64`(include/lib/runtime 都在,`Qt\6.11.0\runtime\*.dll` 是运行时)。

### 路线 A 可行性验证(已完成 ✅ —— 2026-09-17 本轮)
**结论:换。** ClangSharp/libclang 复刻出的 model 事实**与现有 model 完全一致**,而且**一次解析即确定**。

工具:`tools/clangprobe/`(`ClangProbe.csproj`,引 `ClangSharp 21.1.8.2` + `libClangSharp.runtime.win-x64 21.1.8.2`;**不改 gen/**)。
```
# 单个类(打印事实,可与 model 逐条对拍)
clangprobe --class QLineEdit --header <header.h> --raw --out probe.txt
# 全部 Roots(一条 TU,44 类)
clangprobe --all --out all.txt
# 反射查 ClangSharp 的 API(写 probe 时用过)
clangprobe --dump CXCursor CXType clang
```
对拍脚本(临时,未入库):把 model 的 `QLineEdit` 段渲染成同样格式的文本,再逐条比。结果:

| 指标 | 数字 |
|---|---|
| model 事实总数(44 类) | **1472** |
| probe 精确复刻 | **1451** |
| probe 以"可 marshal 前缀"复刻(见下) | **21** |
| **未被复刻** | **0** |
| probe 多出来且无解释的 | 0(只多 2 个信号,见下) |
| `--all` 解析耗时 | **~1.4 s**(冷启动 ~3.1 s) |
| `--all` 提取总耗时 | **~2.1 s** |
| 可复现性 | **连续 3 次运行输出逐字节一致** |
| 对照:CppSharp extract | ~165 s,且**必须**分块 + 每块两次解析自校验 |

`QLineEdit` 单类结果:基类 `QWidget`、`isAbstract=false`、2 个 ctor、58 个 method、17 个 virtual、7 个 signal、嵌套枚举 `EchoMode`/`ActionPosition`(项+值与 model 完全一致),**默认参数逐个一致**(`nullptr`/`1`/具名枚举量)。

**"可 marshal 前缀"那 21 条是 model 的既有规则,不是丢失**:model 只保留"最长可 marshal 前缀"(如 `QPixmap::load(QString, const char* = nullptr, Flags = …)` → `load(QString)`);probe 拿到的是完整签名,规则由 codegen 侧继续套用即可。

**新前端需要的适配规则(全部已验证,逐一列出)**
1. **类型拼写直接用 clang 的**:枚举在类内拼作 `EchoMode`,要按 cursor 的语义父级补成 `QLineEdit::EchoMode`;`qreal` 要 desugar 成 `double`(clang 的 `CanonicalType` 或别名表)。`Qt::Alignment` 这类 typedef/QFlags 名字 clang 原样给出,**不需要** `Desugar()`/身份比较。
2. **默认参数**:libclang 没有 API;默认值是 ParmDecl 的**表达式子 cursor**(要跳过类型引用子 cursor),取子 cursor 的 SourceRange 切片即得文本(如 `nullptr`、`1`、`Qt::AutoColor`)。
3. **protected 也要看**:Qt 的事件处理器是 protected,model 的 virtual 列表不限 access(只有普通 method 限 public)。
4. **`Q_WEAK_OVERLOAD` 是函数模板**:Qt 用它实现"弱重载"(`void setObjectName(const QString&)` 实际是 `template <typename = void>`)。libclang 对 `CXCursor_FunctionTemplate` 的 `Cursor_getNumArguments` 返回 -1,且**不保证**给出模板内的声明;退化办法是读**函数类型的参数类型**(`CXType.NumArgTypes`/`GetArgType`)——只丢参数名和默认值。这一条只影响 1 个事实(`QObject::setObjectName`)。
5. **宏展开出来的成员**(`Q_OBJECT` 展开出的 `tr`/`metaObject`/`qt_metacall`/`qt_metacast`)其 location 折叠到 `Q_OBJECT` 那个 token 上;用 **SpellingLocation**(而不是 ExpansionLocation)才拿得到正确文本。这些成员 model 本来就排除(`qt_*`/`char*`)。
6. **`CursorChildren` 是惰性视图**,遍历父节点的同时再遍历子节点会互相干扰 —— 先 `ToList()` 再遍历(踩过,值得写进实现)。

**已知限制 / 待办(不影响结论)**
- `libClangSharp` 的 nupkg **不带 builtin headers**;probe 用 `-resource-dir D:\QtSharp\lib\lib\clang\19`(CppSharp 自带的那份)顶着,clang 21 的头文件树用 clang 19 的 builtin 会报 2 条 `prfchwintrin.h: definition of builtin function`(无实际影响,但正式实现要**自带匹配版本**的 builtin headers)。
- MSVC 工具链(STL/Windows SDK)是 **clang 自己探测**到的(probe 里没有任何 `-isystem`),与 CppSharp 的行为一致。
- 只多出 2 条 model 没有的东西:`QObject::destroyed(QObject*)`、`QWidget::customContextMenuRequested(QPoint)` —— 它们是**信号**,而信号仍由正则扫头文件产生(现在的扫描器本就漏了这两个),与前端无关。
- 还没做:枚举/`QFlags` 的**全量**对拍(只逐条验过 QLineEdit 的)、`bindCtors`/`owned` 等我们自己的开关(与 clang 无关,继续用)、以及把 probe 的"事实"**渲染成 model JSON**(正式 `extract2` 的第一版)。

### extract2 上线 + QtWidgets 覆盖面(本轮完成)
**① `extract2` 就是新的前端**(`gen/ApiModel2.cs`,`ApiModel` 现在是 partial:模型形状/排序规则/序列化/信号扫描与 CppSharp 版共用,只有"读 AST"这一层换掉了)。
- 一次性解析 **一条 TU**(第一个 root 头当源文件,其余 `-include`),不再分块、不再两次自校验。
- **~2.5 秒**产出 model(旧链路 ~165 秒),连续多次运行**逐字节一致**。
- 验收:`qtgen extract2` 对原 44 类产出的 model 与入库 model **哈希完全相同**(`CA7567…`),之后才扩大 `Roots`。
- 入口:`qtgen extract2 <model.json>`;`tools/regen-api.ps1` 默认用它(`-Extractor1` 才走 CppSharp 老路)。

**② 覆盖面:44 → 221 类**(`Roots` 表加了 177 行 = 唯一需要做的事)
| | 之前 | 现在 |
|---|---|---|
| 类 / 枚举 | 44 / 75 | **221 / 269**(QtWidgets 121、QtGui 69、QtCore 31) |
| 方法 / 虚函数 / 构造 / 信号 | 1309 / — | **5439 / 478 / 385 / 210** |
| 生成 C# / native | 15.8k / 9.8k 行 | **~66k / ~40k 行**(C# 3.4MB、native 1.6MB) |
| extract | ~165 s | **~2.5 s** |
选 Roots 行的办法:给 `clangprobe` 加了 `--index <header>`(一次解析 248 个头,2.5 秒列出"类 → 定义所在文件"),再人工过滤;`tools/clangprobe` 的 `--index` 已入库。**注意 6 个类在 Qt6 换了模块**(QAction / QActionGroup / QShortcut / QFileSystemModel / QUndoStack / QUndoCommand 都在 **QtGui**),写错模块解析照样过,但**信号扫描会扫到空的转发头**、信号全丢。

**③ model 格式新增/变化(都由实测的代码生成需求驱动)**
- `"subclassable"`:类的析构函数 private 时 `false`(`QClipboard`/`QStyleHints`)。C++ 根本不允许继承,所以**不生成 native wrapper**;否则 MSVC 直接报 `C2282 ~QsApi_X 不能重写私有析构`。
- 虚函数新增 `"pure": true`:`QAbstractButton::paintEvent` 是 `= 0`,没有实现可以 `CallBase_*`,不能生成 `__base` 转发。
- **不在 `VirtualAllow` 白名单里的虚函数现在进 `methods`**(可调用,不可重写)。之前它们被整个丢掉 → `QAbstractItemView::setModel` / `QDialog::accept` / `QWidget::sizeHint` / `QAbstractItemModel::rowCount` 全都不可调用。

**④ 为跑通 221 类修的代码生成 bug(都在 `gen/ApiCodeGen.cs`)**
1. `IsOwned(name)` 必须同时要求"有构造函数":`FromOwnedHandle`/`Dispose`/`Destroy_*` 是一起生成的(`QItemSelection` 的构造函数要 `QModelIndex`,被拒 → 只有瞬态包装)。
2. 纯虚函数不生成 `CallBase_*` 与 `__base`;托管侧默认实现改为空(注释写明原因)。
3. `QStringList` 信号的解码:运行时 `Decode` 加 typeId 11 + native `qs_api_qstringlist_count/at`;`ApiTypes.Managed` 加 `QStringList → string[]`。
4. boxed 参数传 null 时不再解引用:`(p ? *static_cast<T*>(p) : QsApiDefaultValue<T>())`(native 里新增 `template <class T> T& QsApiDefaultValue()`;引用参数需要左值,不能给临时值)。
5. **可选 boxed 参数若默认值表达不出来,整条尾部截断**(`QWidget::grab(QRect = QRect(...))`、`QComboBox::addItem(QString, QVariant = QVariant())`):否则 C# 传 `default`(null 句柄)进 native 就是解引用空指针 —— 这正是 `api-lineedits` 崩溃的原因。
6. **模板参数没有默认值的函数模板直接跳过**:`QVariant::canConvert<T>()` 无法在不给模板实参的情况下调用(`Q_WEAK_OVERLOAD` 的 `template <typename = void>` 可以)。

**⑤ 为跑通修的抽取规则(`gen/ApiModel2.cs`)**
- **非 const 引用参数 = out 参数,不可绑定**(`QClipboard::text(QString&, Mode)` 编译不过);但**指向绑定类**的非 const 引用保留(`QBrush::swap(QBrush&)` 的托管对象就是 native 对象,语义正确)。
- 枚举按语义父级限定(`EchoMode` → `QLineEdit::EchoMode`)、`qreal`→`double`、typedef 递归解糖(容器的 `const_iterator` → `QRect*`)、`QFlags<E>` typedef 用 typedef 名 + 展开 `E` 的项。
- 默认参数取 ParmDecl 的**表达式子 cursor**的源码切片;遇到 `static_cast<T>(x)`/括号要**剥一层**才和 CppSharp 的 `DefaultArgument.String` 一致(`static_cast<Qt::MatchFlags>(Qt::MatchExactly|Qt::MatchCaseSensitive)` → `Qt::MatchExactly|Qt::MatchCaseSensitive`)。

**⑥ 新增 demo:`testapp api-widgets`**(`testbind/WidgetsApi.cs`,44 条断言全 PASS,已进 `regen-api.ps1` 门禁)
一个窗口串起菜单/工具栏/状态栏、QTabWidget、QSplitter、按钮/数值输入/复选框/单选、QComboBox/QFontComboBox、QListWidget/QTreeWidget/QTableWidget(含 `QTreeWidgetItem`/`QTableWidgetItem`)、QTextEdit/QPlainTextEdit、QStandardItemModel + QTableView + QSortFilterProxyModel、QModelIndex/QVariant、QDataWidgetMapper、QCalendarWidget、QProgressBar、QDialogButtonBox、QCompleter、QUndoStack。每个都得通过生成的 API 驱动并回读状态。

**⑦ 类覆盖到什么程度**:QtWidgets 头文件里能绑的 198 个顶层类中,**184 个已入库**;剩下 14 个是:①需要别的 Qt 模块或不可绑(`QRhiWidget` 需要 QtRhi、`QTouchEventWidgetSequence`/`QWidgetData`/`QAccessibleWidget`/`QStylePlugin` 是内部实现);②纯静态工具(`QStyleFactory`)或工厂基类(`QItemEditorCreatorBase`);③X11 遗留(`QColormap`)与样式小结构(`QTileRules`)。另外 6 个类(QAction/QActionGroup/QShortcut/QFileSystemModel/QUndoStack/QUndoCommand)在 Qt6 属于 **QtGui**,已绑但不在 QtWidgets 头文件里。

**⑧ 类型类别:已补的那一批(本轮下半场)**
- **`const char*`** → C# `string`:参数直接透传 `const char*`(P/Invoke 用 `LPUTF8Str`),返回用 `QString::fromUtf8` 拷成 QString 交给既有的 QString 桥(native 侧 `NativeRetExpr`)。
- **整数 typedef** → `qsizetype`/`qint64` 是 `long long`、`uint`/`QRgb` 是 `unsigned int`、`quint64` 是 `unsigned long long`:在 `BuiltinName` 里把 canonical 类型映射成 model 类型,再 `ApiTypes.Managed`/`NativeParamType`/`NativeRetCpp` 各加一行(注意 `NativeRetCpp` 漏了会报 `cannot convert qsizetype to void*`)。
- **`QStringList`** → C# `string[]`(唯一支持的容器):参数由运行时 `ApiRuntime.NewStringList` 建一个 native QStringList、**`try/finally` 里释放**(codegen 新增 `MarshalParams`/`EmitGuarded`);返回走 `ToManagedStringList`(拷出来后 `qs_api_qstringlist_free`)。model 侧靠"canonical 拼写 == `QList<QString>`"识别。
- **`QStringView`/`QAnyStringView`** → 和 `QString` 一样当字符串(native 侧 `QString::fromUtf16`,C++ 的隐式转换负责变成视图);**`QLatin1StringView` 故意不收**(QString 不能隐式转成它)。`char32_t` → `unsigned int`。
- **泛型 `QList<T>`** → C# `T[]`(元素类型取模板实参,不解析拼写):codegen 为**每个元素类型**生成一组 `qs_api_list_<stem>_new/append/count/at/free`(元素按种类过 C ABI:标量直接传、绑定类指针传句柄、boxed 值传"副本指针"),C# 侧对应生成 `QsApiList_<stem>.New/Free/Read` 静态辅助类。参数走 `MarshalParams` 的 `try/finally`,返回走 `Read`(读完 `free`)。**boxed 元素用 `FromOwnedHandle`(元素是 native 端 new 出来的副本),指针元素用 `FromTransientHandle`**(元素归容器/视图所有,不能删)。
- **小标量**:`short`/`unsigned short`/`long`(Windows 上 32 位→C# `int`)/`unsigned long`/`char`→`sbyte`/`unsigned char`→`byte`/`char16_t`→C# `char`。每加一个都要同时改 `BuiltinName`(model 名)+`ApiTypes.Managed`+`CSharpScalar`+`NativeRetCpp`(C++ 拼写)。
- **不可拷贝的返回值**:`QDataStream::writeBytes` 之类的流式 API 返回 `QDataStream&`,而生成的 glue 要 `new QDataStream(call)`(拷贝)—— `QDataStream` 的拷贝构造是 deleted。抽取器新增 `IsCopyableResult`(找 copy ctor,deleted 就拒),这类方法直接不入 model。
- **裸指针**:`void*`/`const void*` → C# `IntPtr`(不解释,直接透传);`const uchar*`(长度另行传入的字节缓冲)→ C# `byte[]`,**只允许出现在参数位**(返回值没有长度,抽取器直接拒)。
- **`ApiTypes.Managed` 的坑**:`char*` 也以 `*` 结尾,必须先判字符串再判指针,否则 `char*`→`IntPtr` 而 codegen 渲染成 `string` → **同一 C# 签名出现两个重载(CS0111)**。
- **默认值的两处 C# 陷阱**:①`IntPtr` 是值类型,`IntPtr.Zero` **不是编译期常量**,所以可选的不透明指针参数默认值只能是 `default`(写 `null` 报 CS1750、写 `IntPtr.Zero` 报 CS1736);②`char*`/`uchar*` 映射成引用类型,默认值才是 `null`。
- **入口符号撞名**:构造函数导出原来叫 `qs_api_{name}_create{N}`,而 `QMetaType::create` 这个**方法**也叫 `create` → `qs_api_QMetaType_create` 定义两次(`C2733`)。已改名 `qs_api_{name}_ctor{N}` / `qs_api_{name}_dtor`。(重载后缀 `foo2` 与真实方法名 `foo2` 仍可能相撞,概率极低,先记在这。)
- 又补 10 个类(QtGui 值类型):`QPicture`/`QScreen`/`QVector2D`/`QVector3D`/`QVector4D`/`QQuaternion`/`QEventPoint`/`QColorTransform`/`QMatrix4x4`/`QPointingDevice`。
- **`QByteArrayView`** → C# `byte[]`(它自己带长度,所以**参数和返回都行**):参数由运行时 `ApiRuntime.NewByteArray` 建一个 native QByteArray(C++ 的隐式转换负责变成视图,同样 `try/finally` 释放),返回走 `ToManagedBytes`。
- **`sizeHint`/`minimumSizeHint`/`heightForWidth`/`hasHeightForWidth` 进 `VirtualAllow`**(自定义控件必须能说自己多大,官方 Calculator 的 `CalcButton` 就是重写 `sizeHint`)——这一步逼出三个 must-fix:
  1. **虚函数要带 `const`**:`QWidget::sizeHint() const` 不带 const 就**不构成重写**(MSVC `C3668`),model 里因此新增虚函数的 `"const": true`,生成 native wrapper 时写 `... const override`(注意顺序,`override const` 编译不过);
  2. **override 的参数表必须是**完整**的**:之前"可 marshal 前缀"的裁剪规则会把 `sizeHint(Qt::SizeHint, const QSizeF& = QSizeF())` 的第二个参数砍掉 → 签名对不上 → `C3668`。现在虚函数保留全部参数(裁剪规则只用于可调用成员),`CppVirtualParamType` 对 boxed 值类型统一发 `const T&`(Qt 的写法),`DispatchArgStore` 相应 `const_cast`;
  3. **虚函数返回值是 boxed 值类型时**(`QSize`)托管侧要写回调用方的槽:新增按类型生成的 `qs_api_assign_<T>`(native `*dst = *src`)+ `ApiNative.Assign_<T>`,Dispatch 按返回类型选择写回方式;顺带给 `ApiRuntime.WriteResult` 补齐了 double/float/long/ulong/uint/short/ushort/char/sbyte/byte/IntPtr 重载。纯虚函数(无实现可链)的托管默认实现改为 `return default;`。
- 顺带:生成的 ctor 局部变量 `context` 撞了 `QShortcut(... Qt.ShortcutContext context)`,已改名 `__context`。
- **QGraphics\* 整组入库**(scene/view/15 个 item 类/9 个 scene event/effect/transform/layout/anchor):item **不是 QObject**且由 scene 拥有,所以一律 `owned: false`(GC 不删,暂时会泄漏,等所有权服务);scene event 由 Qt 在栈上构造 → `bindCtors: false`。**新增 demo `testapp api-graphics`**(9 项断言,含 `scene.items()` 的 `QList<QGraphicsItem*>` 返回、`itemAt()` 的身份一致、离屏 `view.Grab()` 真的渲染出 240x120)。
- 又补 29 个类(其余 QtWidgets):`QSplashScreen`/`QToolTip`/`QWidgetAction`/`QWizardPage`/`QUndoGroup`/`QSplitterHandle`/`QStylePainter`/`QTableWidgetSelectionRange`/`QTreeWidgetItemIterator`/`QPlainTextDocumentLayout`/`QItemEditorFactory`/`QStyleOption*` 4 个/`QStyleHintReturn*` 3 个/`QScroller`/`QScrollerProperties`/手势家族 5 个/`QGestureEvent`/`QGestureRecognizer`。

**⑨ 已知瑕疵 / 下一步(按收益排序)**
1. **`QList<std::pair<...>>`**(x6)与 **`QFont::Tag`**(x8)是仅剩的两个"元素/嵌套类"类别;容器迭代器(`iterator`/`const_reverse_iterator`,x16)**故意不收**(PySide 也不暴露 C++ 迭代器)。
2. **剩下的都是"故意不收"或收益很低的**:`char*`(x6,out 缓冲区,没有长度)与非 const `QString&`(11,要 in-out 语义);`const uchar*`(10)只收参数位;`QGenericArgument`/`QGenericReturnArgument`(11)是 `QMetaObject::invokeMethod` 的变参包装;`Connection`(5)= `QMetaObject::Connection`;`QPaintEngine*`/`DataPtr&`/`QModelRoleDataSpan`/`QRgba64`/`QSpan<...>`/`std::chrono::milliseconds`/`void**`(各 5)都是独立的新词汇表条目,各自只值 5–10 个方法。
3. **item 类暂时 `owned: false`**(视图/布局接管所有权,GC 释放会 double free)→ 会泄漏;正解是 PoC #3 的所有权服务。
4. 派生类重声明的虚函数生成的是 **C# `virtual` 隐藏**(CS0114)而非 `override`:native 派发路径是对的(槽位按声明类解析),但从基类类型调用会走基类实现。要彻底修需要 codegen 记录"基类是否已声明同名虚函数"。
5. `QErrorMessage.FromHandle` 等静态成员隐藏了继承的同名静态(CS0108 警告),同理可加 `new`。
6. `QMetaObject` 已入库(动态派发能力到手:`metaObject().className()`/`indexOfSignal()` 都可用),但它的 `QMetaMethod`/`QMetaProperty` 还没绑,`method()`/`property()` 那族仍被挡。`QMetaMethod::getParameterTypes(int*)` 被 `IsOutBufferMethod` 挡住了(它写 N 个 int)。
7. **字段(public 数据成员)没有绑定机制**:`QJsonParseError`(只有 `error`/`offset` 两个公开字段)因此只能当 null 参数用,`FromJson(byte[], error)` 拿不到错误码。PySide 是把它当属性暴露的 —— 这就是 Q_PROPERTY 那条轴顺带要解决的事。
8. `QJsonObject::insert/remove` 返回 `QJsonValueRef`(嵌套类)被挡 → 从 C# 造 JSON 目前只能走 `QJsonObject::fromVariantMap(Dictionary<string,QVariant>)`。`QJsonObject::iterator` 同理(迭代器故意不收)。
9. **`QGuiApplication` 没绑 → `QClipboard` 够不着**(`QApplication::clipboard()` 也不在 model 里)。所以 `QClipboard::text(QString&, Mode)` 即使随"非 const `QString&`"一起入库也无法端到端验证,需要手写一个 `qs_api_app_clipboard()` shim(像 `qs_api_app_create` 那样)。

**⑩ 本轮完成:两个容器/模板类型类别(`QMap<K,V>` + `QBindable<T>`,+29 条事实)**
两个都是"照抄现成写法"的收尾工作,没有触碰架构;`gen` 三处改动(`ApiModel2` 词汇表 / `ApiTypes.Managed` / `ApiCodeGen`),门禁全绿(extract 逐字节可复现、codegen 两次一致、5 个 demo PASS)。

- **`QMap<K,V>` → C# `Dictionary<K,V>`**(`QAbstractItemModel/QAbstractProxyModel/QStandardItemModel/QStringListModel::itemData` + `setItemData`、`QVariant(QMap<QString,QVariant>)`/`toMap`、`QCalendarWidget::dateTextFormat`,共 11 条)。
  - 抽取器:`TryUnwrapMap` 取 specialization 的两个模板实参(与 `QList` 同法,**先剥引用再 `CanonicalType`**,否则引用的 `Declaration` 是无效的);`SupportedType` 对两半**递归**判可绑定(所以 `QMap<QFont::Tag,quint32>` 里的 `QFont::Tag` 自动被挡 —— 嵌套类在 `ModelName` 里只有裸名 `Tag`)。
  - codegen:新增一组**容器元素编解码**(`ElemAbiCppParam/Ret`、`ElemAbiCsParam/Ret`、`ElemToCpp`、`ElemFromCpp`、`ElemRead`、`ElemArg`),与 `QList` 的那套独立 —— 顺带补上了**枚举元素**(`QMap<Qt::GestureType,bool>`)与 **QString 元素**(`QMap<QString,QVariant>`)。注意**参数位与返回位的 ABI 类型不同**:参数位的 QString 是 `const char16_t*`(P/Invoke `LPWStr`),返回位是 `void*`(`new QString(...)` 的堆副本)。
  - native 用 `constBegin()` 走到第 index 个迭代器取 `it.key()/it.value()`(QMap 有序但无随机访问;map 都很小,不做游标)。C# `Read` 按 `count` 组装 `Dictionary`,参数走 `MarshalParams` 的 `New/Free` + `try/finally`。
  - **注意(不是 bug)**:`setItemData` 里塞 `Qt::EditRole` 会被 `QStandardItem` 折叠到 `DisplayRole`,所以"塞两个 role、读回两个"要用 `DisplayRole`(0)+ `ToolTipRole`(3)。
- **`QBindable<T>` → 生成的泛型 `QBindable<T>` 包装**(18 条,全是 `bindableXxx()` 访问器:QTimer×4、QSortFilterProxyModel×11、QObject/QStandardItemModel/QItemSelectionModel/QAbstractProxyModel 各 1)。
  - **只在返回位**:`bindableFoo()` 返回 `QBindable<T>` 的**值**;native 侧 `new QBindable<T>(call)` 交给托管,托管 `Dispose()` 释放这个副本(它持有的属性数据指针不属于我们)。
  - **只收返回位是抽取器的规则**(`SupportedType` 里 bindable 参数直接拒):托管侧没有构造 native `QBindable` 的办法。
  - 托管面是**一个**手写形态的泛型类 `QBindable<T>(IntPtr handle, Func get, Action set, Action free)` + `Value` 属性(get/set)+ `IsDisposed`;每种 `T` 生成一个 `QsApiBindable_<stem>` codec(`value()`/`setValue()`/`free()`)与 `ApiNative.Bindable_<stem>_*` P/Invoke。
  - **native 必须 `#include <qproperty.h>`**:类头只前向声明 `QBindable<T>`,不 include 就是 MSVC `C2027 使用了未定义类型`。
  - 顺带把 `NativeRetExpr` 补了 bindable 分支(否则 `无法从 QBindable<T> 转换为 void*`)。
  - 验证(`api-widgets` 4 项):`QBindable<int>` 写 250 后 `timer.Interval()==250`、`QBindable<bool>` 读属性、`QBindable<Qt::TimerType>` 读枚举默认 `CoarseTimer`、`QBindable<string>` 写 `objectName` 回读。

**⑪ 本轮完成:out 参数 + `QHash<K,V>`(+100 条事实,类型词汇表这一轴做完)**
- **out 参数**(93 个参数 / 40 个方法):非 const 标量指针(`QColor::getRgb(int *r,...)`、`QRect::getRect`、`QDate::getDate`、`QTransform::map`、`QLayout::getContentsMargins`、`QFontDialog::getFont(bool *ok)`、`QPolygon::point`)在 model 里记成参数级 `"out": true`,C# 侧发成 `out int r`(P/Invoke 也 `out int`,blittable 直通;`bool` 要 `[MarshalAs(UnmanagedType.I1)] out bool`)。非 const 标量**引用**(`int &pos`)记 `"ref": true` → `ref int`(**当前 model 里 0 个**,要等非 const `QString&` 一起才能到,见下一步)。
  - 抽取器:指针/引用按 `ParamModeOf` 分类(标量指针 = Out,`uchar*` 例外 —— Qt 只是**读**它,所以 QImage 的像素数据构造函数照样是输入 `byte[]`);**返回位**的标量裸指针一律拒(`QImage::bits()`/`QMatrix4x4::data()` 没有长度);带默认值的 out 参数**丢掉**(C# 里 `out int a = 0` 不合法)。
  - **`IsOutBufferMethod` 黑名单**(`copyDataTo`、`getParameterTypes`)是这套规则的**已知漏洞**:非 const 标量指针**无长度**,而这两个方法写的是 N 个元素(`QMatrix4x4::copyDataTo(float*)` 写 16 个),当成 `out float` 会**写坏调用方的栈**,所以整条声明不绑。加新类型时看到"非 const 指针"要先想清楚它是不是缓冲区。
  - 顺带修:`uchar*` 的 C ABI 类型改成**非 const** `unsigned char*`(Qt 有的入口是 const 有的是非 const,非 const 能转 const 反之不行)。
- **`QHash<K,V>` → `Dictionary<K,V>`**(7 条:`QAbstractItemModel::roleNames` ×4 + `setItemRoleNames`、`QVariant(QHash<QString,QVariant>)`/`toHash`):codegen 里把 `QMap` 的三个生成器参数化成**assoc 生成器**(`AssocClass/AssocPrefix/AssocTag`,native 导出 `qs_api_{map|hash}_<stem>_*`),两边共用同一套 `Elem*` 元素编解码。
- 验证:`api-widgets` 新增 12 项断言(7 个 out 参数家族 + `QHash` 返回 + `toHash`),共 75 项;门禁全绿。

**⑫ 距 PySide/PyQt 同等能力还差什么(量化,2026-09-17)**
用 `class Q…` 扫三个模块的公开头,减去 model 里的 306 类:
| 轴 | 现状 | 差距 |
|---|---|---|
| **类覆盖** | 306 类 | QtCore 缺 **132**、QtGui 缺 **59**、QtWidgets 缺 **32**(含很多 PySide 不绑的模板/内部类,但 `QFile`/`QDir`/`QThread`/`QProcess`/`QJsonDocument`/`QDebug`/`QStandardPaths`/`QCommandLineParser`/`QUuid`/`QRandomGenerator`/`QFuture` 这类是真实缺口)。**这是最大的一轴** |
| **虚函数可重写** | `VirtualAllow` 31 个名字(事件 + sizeHint 家族) | PySide 里**任何**虚函数都能重写 → 自定义 `QAbstractItemModel::data/rowCount/setData`、`QStyledItemDelegate::paint/createEditor` 现在**不行** |
| **方法签名** | out 参数已支持 | 非 const `QString&`(11,`QValidator::validate/fixup`、`QClipboard::text`)—— 需要 native 侧"语句体"而不是单个表达式,是下一步 |
| **属性** | 只有 getter/setter 方法 | PySide 有 `obj.propertyName`(Q_PROPERTY);`QMetaObject` 已入库但 `QMetaProperty` 未绑 |
| **所有权** | Qt-owned 弱引用 / boxed 自管 | item 类仍 `owned:false` **会泄漏**;没落地 PoC #3 的所有权服务 |
| **元对象/动态派发** | `QMetaObject::className/indexOfSignal` | `invokeMethod`(`QGenericArgument`)、`QMetaObject::Connection`(disconnect)未绑 |
| **容器** | `QStringList`/`QList<T>`/`QMap`/`QHash` | `QSet`(无方法用)、`QList<std::pair<...>>`(6)、迭代器(故意不收) |

**推荐的推进顺序**:①**QtCore 类批量入库**(收益最大、纯体力活:`Roots` 加行 + 每批暴露的词汇表缺口);②**解开虚函数白名单**(自定义 model/delegate 必须能重写,需要 Dispatch 支持容器/boxed 参数与 QVariant 返回);③非 const `QString&`;④Q_PROPERTY → C# 属性;⑤所有权服务。

**⑬ 本轮完成:QtCore batch 1(22 个值类)+ 三个真 bug + 四个轴的勘察方案**
用 4 个并行子 agent 做了只读勘察(2/3/4 轴的方案见本节末尾),然后串行落地了这一批:
- **`Roots` 加 22 行**:`QDir`/`QDirIterator`/`QTemporaryDir`/`QFileInfo`/`QStandardPaths`/`QDeadlineTimer`/`QUuid`/`QRandomGenerator`/`QElapsedTimer`/`QEasingCurve`/`QVersionNumber`/`QSysInfo`/`QJsonValue`/`QJsonObject`/`QJsonArray`/`QJsonDocument`/`QJsonParseError`/`QCommandLineOption`/`QCommandLineParser`/`QProcessEnvironment` + QtGui 的 `QSurfaceFormat`/`QGlyphRun`。**全部是非 QObject 值类**,所以"managed 拥有"的规则不变(346→**328 类**、+541 条事实,纯新增无删除)。`QFileInfoList`/`QByteArrayList`/`QVariantMap` 都是既有词汇表的 typedef,白捡。
  - QObject 派生的那批(`QFile`/`QSaveFile`/`QTemporaryFile`/`QProcess`/`QThread`/`QLibrary`/`QPluginLoader`)**故意留到所有权修复之后**:`DerivesFromQObject`(`ApiModel2.cs:815`)是**按 chunk 判断**的,跨头文件就返回 false → `QMainWindow`/`QCheckBox`/`QPushButton` 现在都是 `owned:true`,而它们放进 layout 后由 Qt 接管 → **finalizer 会 double free**(就是文档里那个 QCommandLinkButton 崩溃)。修复方案见下面"所有权"。
- **新增 demo `testapp api-core`**(`testbind/CoreApi.cs`,42 项断言,已进门禁)——它当场抓出三个真 bug:
  1. **`QVersionNumber` 的 `Q_WEAK_OVERLOAD` 构造函数被当成方法**:libclang 把 `template <typename = void> QVersionNumber(const QList<int>&)` 报成 `FunctionTemplate`,于是生成 `self->QVersionNumber(...)` 编译不过。修法:成员名 == 类名 ⇒ 一定是构造函数(`ApiModel2.cs` 的 `Member`,顺带白捡 `QVersionNumber(int[])` 这个 ctor)。
  2. **`QStringView`/`QAnyStringView` 返回值的 native 表达式错**:`new QString(view)` 没有这个构造函数,要写 `view.toString()`(`ApiCodeGen.NativeRetExpr`)。
  3. **★ enum/QFlags 默认参数大面积丢失或错位**(`DefaultLiteral2`):Qt 对 QFlags 参数写的是**裸枚举名**(`NoFilter`、`EnsureVisible`、`Off`),旧规则要求文本里含 `::` 才认,于是全部退化成 `default`(=0);而 `QDir::NoFilter` 的值是 **-1**,`EntryList()` 因此**一个文件都列不出来**。新规则:类型含 `::` 时,把 `A|B` 拆开、每个按标识符解析成 `Enum.Item`。model 里 129 条默认值随之修正(`match` 的 `Qt.MatchFlags.MatchStartsWith|MatchWrap` 之前被错写成只有 `MatchWrap`)。
- 门禁:model 重跑逐字节一致、codegen 两次一致、native+C# 编译、**6 个 demo 全 PASS**。

**⑭ 本轮完成:所有权修复 + QtCore batch 2(350 类,`QCommandLinkButton` 崩溃已消除)**
- **★ 所有权两处真 bug(都是"GC 删了 Qt 还持有的对象")**:
  1. **非拥有包装器也会 delete**:`FromHandle`/`FromTransientHandle` 造出来的包装器和 `new X()` 的包装器是**同一个类**,所以它有**同一个 finalizer**,`Dispose()` 无条件 `Destroy_X` → Qt 交给我们的对象(`QApplication::topLevelWidgets()`、`FromHandle` 拿到的任何东西)在回收时被 `delete`。修法:root 类新增 `internal bool __ownsNative`,**只有**我们发布的构造函数与 `FromOwnedHandle`(Qt 交给我们的 boxed 副本)把它置 true,`Dispose()` 只在为 true 时 delete。
  2. **reparent 之后仍然 delete**:`DerivesFromQObject` 在抽取器里**按 chunk 判断**(跨头文件就返回 false),于是 `QMainWindow`/`QPushButton`/`QLabel` 等 **89 个类**的 `owned` 是 true,而它们被 layout 接管后归 Qt → finalizer double free。修法:生成的 `qs_api_<X>_dtor` 对 **QObject 派生类**(codegen 沿 model 的 `bases` 图判断)先问 Qt——`if (object->parent()) return;`,没有 parent 的仍然释放(不泄漏)。
     - **注意**:`QWidget`/`QThread`/`QClipboard`/`QGraphicsScene` 等本来就是 `owned:false`(它们**永不**被托管释放,这是既有的设计),不在这次修复范围内。
  3. **顺带修的第三个真 bug(身份缓存)**:`ApiRuntime` 的 handle→包装器缓存是 `WeakReference` 列表,`Wrap` 取**第一个**活的 → 一个被 `Dispose`(其原生对象被 delete)的地址**被分配器复用**后,新对象会拿到**上一个死包装器**(`FromHandle` 返回错对象)。修法:新增 `ApiRuntime.Forget(handle, wrapper)` 并由生成的 `Dispose` 调用,`Wrap` 改成取**最新**的活包装器。
  - 验证:`testbind/OwnershipApi.cs`(`testapp api-ownership`,12 项)。**含文档里那个 `QCommandLinkButton` 崩溃的回归断言**(8 个放进 layout,反复 GC 后仍可用)、reparent 后 `Dispose` 不销毁原生对象(用 `parent.Children().Length` 证明)、无 parent 的对象 `Dispose` **真的**销毁(`QApplication::topLevelWidgets()` 数量减少)、`FromHandle` 返回同一个包装器。
- **QtCore batch 2(20 个类 + 2 个前置类,350 类)**:`QFileDevice`/`QFile`/`QSaveFile`/`QTemporaryFile`/`QProcess`/`QThread`/`QLockFile`/`QCoreApplication`/`QGuiApplication`/`QLibrary`/`QPluginLoader`/`QXmlStreamReader`/`QXmlStreamWriter`/`QTextDocumentWriter`/`QMutex`+`QBasicMutex`/`QReadWriteLock`+`QBasicReadWriteLock`/`QSemaphore`/`QWaitCondition`,外加两个**前置**:`QIODeviceBase`(**不加它 `QIODeviceBase::OpenMode` 这个 QFlags 的作用域就不可发布,`open()/read()/write()` 全部签名被丢**——这是本批最大的坑)与 `QWindow`(`QGuiApplication::allWindows/topLevelWindows/focusWindow` 需要)。
  - `QCoreApplication`/`QGuiApplication` 的构造函数要 `int &argc, char **argv`,model 表达不了 → 整条 ctor 被丢,类**只落地静态成员**(`instance()/arguments()/applicationDirPath()/clipboard()`),这正是想要的:Qt 已经把 application 造好了。
  - **`QGuiApplication` 入库后 `QClipboard` 终于够得着**(不用再写 `qs_api_app_clipboard` shim,见"⑨.9")。
- **`api-core` 扩到 66 项**:文件读写(`QFile`+继承来的 `QIODevice::Write/ReadAll`)、`QSaveFile::commit` 原子写、`QLockFile` 双进程拒绝、`QProcess::execute/waitForFinished/readAllStandardOutput`、`QThread` 静态 + `currentThread` 包装器可回收、`QSemaphore/QMutex`、`QCoreApplication/QGuiApplication` 静态、剪贴板往返、`QXmlStreamWriter/Reader` 往返。
- 门禁:model 逐字节一致(350 类)、codegen 两次一致、native+C# 编译、**7 个 demo 全 PASS**。

**⑮ 本轮完成:解开虚函数白名单(自定义 model/delegate 从 C# 可写;+193 虚函数 / +476 字段)**
按"⑭ 的方案 B"四阶段落地,每阶段都过门禁。**结果:`testapp api-custommodel` 用 C# 写了一个 `QAbstractTableModel` + 一个 `QStyledItemDelegate`,Qt 自己驱动它们**(view 调 `rowCount`/`data`/`flags`,delegate 在 `view.Grab()` 时被调 `paint` 6 次、`ResizeRowsToContents()` 时被调 `sizeHint` 6 次),20 项断言全 PASS。
- **阶段 1**:抽取器把 `VirtualAllow.Contains(name)` 换成 `PublishAsVirtual(name, className)`,虚函数集合从此是"白名单 + 扩展点名"。
- **阶段 2**:虚函数参数带上 **clang 原始拼写** `"cpp": "const QModelIndex &"`(model 类型早就丢了 pointee const 和引用),`CppVirtualParamType(p)` 优先用它 —— 重写签名必须**逐字符**一致,否则是静默的 `C3668`(基类实现照旧生效)。
- **阶段 3**:扩展点名 = `QAbstractItemModel/QAbstractListModel/QAbstractTableModel/QAbstractItemDelegate/QStyledItemDelegate/QItemDelegate` **自己声明的虚函数名**(54 个,剔掉 `metaObject`/`qt_*` 脚手架),并且**只在它们的继承子树里发布** —— 纯"名字闭包"会把 `QPicture::setData(const char*, uint)` 这种同名但毫不相干的方法也变成可重写点(实测编译失败)。
- **阶段 0(Dispatch 基建,先于打开白名单落地)**:
  - `DispatchRead` 的 **boxed 参数改成 clone**(`qs_api_clone_<T>` + `FromOwnedHandle`):之前用 `FromHandle` 会进身份缓存(栈地址复用 → 拿到上一个参数的包装),`owned` 类型还会被 finalizer **delete 掉调用方拥有的对象**;
  - **`qs_api_assign_<T>` 加了 null 判断**(之前 `*dst = *src` 解引用空指针:托管不重写 `data()` 时 `return default` 就是 0 → 崩);
  - 容器/`QStringList` 的 **`ReadTransient`**(`Read` 会 free 掉 Qt 自己的容器)与 **`Write`**(把托管数组写回调用方的槽);`QString` 的 **`ToManagedTransient`**(`ToManaged` 会 free Qt 的 QString);
  - `DispatchArgStore` 对指针/数组/引用参数做 `const_cast`(Qt 传的是 `const QMimeData *`/`const QModelIndex &`,而槽是 `void*`),并且数组要传 `name` 而不是 `&name`;
  - `DispatchStore` 区分**绑定类指针**(`value.Handle`)与**不透明指针**(`IntPtr` 直传),并补 `char*`/`QStringList`/容器分支;`DispatchRead` 里 `char*` 要排在指针分支**之前**(否则被当成句柄)。
- **期间修掉的三个真 bug**:
  1. **派生类重声明的虚函数生成的是 C# `virtual` 隐藏而不是 `override`** → 通过**基类类型**调用时走的是基类实现;新白名单下这会静默返回 0(`QAbstractItemModel::rowCount` 是纯虚 → 托管默认实现 `return default`)。修法:codegen 查 `ChainOf(baseName)` 里有没有同签名同返回类型的槽,有就发 `override`。**CONTEXT ⑨.4 那条老瑕疵到此修完**。
  2. **私有虚函数没法链回基类**(Qt 把 `QAbstractListModel::columnCount` 声明在 `private:`)→ model 新增 `"noBase": true`,不生成 `CallBase_*`/`__base`,托管默认实现改为"什么都不做"。顺带把四处 `__base` 后缀计数器改成**同一顺序**(`C export` 那处原先在校验前自增,重载集里有纯虚时会错位)。
  3. **抽象类没有可构造的 native 子类** → 托管侧根本无法子类化 `QAbstractTableModel`。新增 `"subclassCtor"`:当抽象类的**每一个**继承来的纯虚函数都在可重写点名里(`CanSubclassFromManaged`)时,生成一个无参 native 子类构造 + C# `protected <X>()`(不能 public —— 抽象 Qt 类不该被直接实例化)。实测正好 5 个类:`QAbstractItemModel`/`QAbstractListModel`/`QAbstractTableModel`/`QAbstractItemDelegate`/`QGraphicsLayoutItem`(QLayout 因 `itemAt`/`takeAt` 未被发布而**故意不生成** ✓)。
- **顺带补的一个重要缺口:公开数据成员 → C# 属性**(476 个)。`QStyleOption::rect` 这种聚合字段没有 getter,不绑的话托管 delegate **不知道该画在哪**;`QJsonParseError::error/offset` 同理(⑨.7 的老问题)。做法:抽取器收 public 非位域字段(类型须在词汇表内,并把字段类型引到的枚举也收进 model),codegen 生成 `qs_api_<X>_get/set_<field>`(inline 存储只能生成访问器,没有导出符号)+ C# 属性;flags 字段走 `fromInt`/`toInt`,指针字段的 getter 要 `const_cast`(`const QWidget *widget`)。
- 数字:虚函数 657 → **850**(native override 槽 2877 → 3308,native 2.65 → 2.98 MB,**没有出现"体积爆炸"**),字段 476,枚举 369 → 392;model 仍然逐字节可复现。

**⑯ 本轮完成:非 const `QString&` + `Q_PROPERTY` → C# 属性(签名这一轴收尾)**
- **非 const `QString&` → C# `ref string`**(12 条:`QValidator::validate/fixup` 在 `QValidator`/`QIntValidator`/`QDoubleValidator`/`QAbstractSpinBox`/`QDoubleSpinBox` 上 + `QClipboard::text(QString&, Mode)`,外加新入 model 的 `QValidator::State` 枚举)。
  - **一个 model 参数 → 两个 native 参数**:in 字符串(`const char16_t*`)+ out 槽(`void**`);native 侧是**语句体**而不是单个表达式(`QString qs_in_x = ...; auto qs_result = call; *x_out = new QString(qs_in_x); return ...;`),为此 codegen 新增 `NativeBody(m, call)` 返回语句列表,`NativeRetExpr` 变成它的一条特例。
  - 托管侧 `MarshalParams` 新增 **`Postlude`**(调用之后、return 之前执行):`input = ApiRuntime.ToManaged(__out_input);`;有返回值时先把结果存进 `__result` 再写回再 return。`NativeParamsFor`/`CppParamsFor`/`CppArgsFor` 改成返回 `List<string>`(1..n 映射),6 个声明点改成 `SelectMany`。
  - 顺带:抽取器拒掉**构造函数**上的 by-ref 参数(构造转发器没有 out 槽的位置)。
  - 验证:接受 `"42"`(Acceptable)、`"5"`(Intermediate)、`"abc"`(Invalid)、`fixup("+0042") → "42"`、经 `QSpinBox` 继承来的 `Validate`、`QClipboard::text(ref subtype)` 把 subtype 填成 `"plain"`。
- **`Q_PROPERTY` → C# 属性**(**1004 个**):头文件扫描器 `Program.ScanProperties`(照抄信号扫描器:括号平衡取多行声明、`EnclosingClass` 回溯最近的 class)在 `extract2` 里与信号一起收集;model 新增 `properties` 数组(**放在 `signals` 之前**,不动 `WriteSignals` 的"最后一个 key"约定)。
  - 解析规则:READ getter 必须是**已发布的**方法、WRITE setter 必须恰好 1 个参数且**托管类型与 getter 返回类型一致**,否则跳过属性(方法照旧) —— `QAction::icon`(QIcon 型)就是被这条挡掉的。
  - C# 侧**只做语法糖**:`public string Text { get => text(); set => SetText(value); }` —— 调用已生成的访问器,**不重复生成 native glue**。
  - **命名冲突**:属性占 Pascal 名,访问器退回 C++ 原名(`QLineEdit.text()` 保持小写、属性叫 `Text`;`QObject.objectName()`/`QWidget.width()`/`QTimer.interval()` 同理)—— **demos 里 47 处调用点随之改回原名**(这是文档预告过的churn)。
  - 三类**跳过**并保留方法:`echoMode`(名字撞嵌套枚举 `EchoMode`)、`QWidget::sizeHint`(名字撞**虚函数**,虚函数名必须沿继承链一致,否则派生类会去 override 一个属性)、字段同名者。
  - NOTIFY **仍不生成 C# `event`**(信号层还没有 disconnect,`+=` 会泄漏),`XxxSignal` 属性照旧。
- 门禁:model 逐字节一致、codegen 两次一致、native+C# 编译、**8 个 demo + 3 个 PoC 全 PASS**。

**⑰ 本轮完成:A2 所有权服务(Qt 删除后失效;item 类不再裸悬空)**
- **native 只加一个钩子**:`QsApiDestroyedSlot`(照抄信号转发器的 `QSlotObjectBase` 写法)+ `qs_api_set_destroyed_callback(void(*)(void*))` + `qs_api_watch_destroyed(object)`;**userData 就是对象地址**,没有 per-object GCHandle/delegate(PoC #3 那样会泄漏),slot 由 Qt 在对象析构时释放;watch 幂等(`std::unordered_set` 记账)。
- **托管侧注册表**:`ApiRuntime` 新增 `destroyed`(已销毁地址集合)、`ownedItems`(owner → items)、`IQtObjectView.MarkDestroyed()`;`OnDestroyed` 用 `[UnmanagedCallersOnly]` + 函数指针装一次(静态构造里),收到通知就把该地址的**所有**包装器标记失效,并递归失效它拥有的 item。
- **生成的句柄变成"会抛"的属性**:root 类发出 `internal IntPtr __handle; internal bool __invalidated;` + `public IntPtr Handle { get => __invalidated ? throw new ObjectDisposedException(...) : __handle; protected set; }`(所有生成的方法都走 `Handle`,所以**只改一处就全局生效**);`Dispose` 改用**后备字段**(Qt 已销毁的包装器是 invalid 的,finalizer 仍要安全跑完),并在 `__invalidated` 时**不再 delete**;包装构造函数(以及公开构造函数)对 QObject 调 `ApiRuntime.Watch`。
- **item 类**(`QGraphicsRectItem` 这种非 QObject、由 scene 拥有):`addRect`/`addItem`/`addText`… 生成 `ApiRuntime.AssociateOwner(Handle, item)`,owner 被销毁时 item 的包装器一起失效 —— 这是 item 唯一可用的生命周期信号。
- **关掉时的那颗雷**:Qt 在进程退出阶段析构对象 → 回调进托管 → CLR 断言 `Attempt to execute managed code after the .NET runtime thread state has been destroyed`。修法:`ProcessExit` 里先 `SetDestroyedCallback(null)`,并且生成的 `Dispose` 加 `!ApiRuntime.ShuttingDown` 守卫 —— 退出阶段**完全不进托管、也不删对象**(退出时不泄漏无意义)。
- 验证(`api-ownership` 12 → **19** 项):被父对象带走的子对象 `Text()` 抛异常;`scene.AddRect(...)` 的 item 反复 GC 后仍可用;**owner 被 Dispose 后 item 与 scene 都抛异常**。
- 门禁:model 逐字节一致、codegen 两次一致、native+C# 编译、**8 个 demo(232 断言)+ 3 个 PoC 全 PASS**。

**⑱ 本轮完成:QtCore batch 3 上半(`std::chrono`→`TimeSpan`、`QRunnable`/`QThreadPool`、`.slnx`)**
- **`std::chrono` duration → C# `TimeSpan`**(14 条):`QTimer::setInterval/intervalAsDuration/remainingTimeAsDuration/singleShot`、`QObject::startTimer`、`QThread::sleep/start`、`QLockFile::tryLock/setStaleLockTime/staleLockTimeAsDuration`、`QDateTime::addDuration`、`QTimeZone::fromDurationAheadOfUtc`。
  - 抽取器:`ModelName` **最前面**判 `std::chrono::{nano,micro,milli,}seconds/minutes/hours`(要放在 typedef 分支**之前** —— `milliseconds` 的 canonical 是 `duration<...>`,会丢单位)。
  - **单位靠 `"cpp"` 拼写带着走**:模型的类型是 `TimeSpan`,参数额外带 `"cpp": "std::chrono::milliseconds"`(只有这一处会因此多出 `cpp`,不影响别的参数的 diff);C ABI 统一是**纳秒 int64**,C++ 侧 `duration_cast<单位>(nanoseconds(ns))`、返回侧 `duration_cast<nanoseconds>(expr).count()`、C# 侧 `TimeSpan.FromTicks(ns/100)` / `(long)(span.Ticks * 100)`。
- **`QRunnable` + `QThreadPool`**(+2 类):把 `run` 加进可重写点名(`VirtualExtensionClasses` 加 `QRunnable`/`QThread`)——`QRunnable` 是抽象类且唯一纯虚是 `run`,于是自动拿到 `subclassCtor`;验证里用 C# 写了一个 `QRunnable` 子类交给 `QThreadPool::start`(**Qt 在自己的线程上调 `run()`**,跨线程 reverse P/Invoke 正常),以及一个 `QThread` 子类重写 `run()`。**注意 `QRunnable` 默认 `autoDelete=true`(pool 会删它),托管子类要 `SetAutoDelete(false)`** —— demo 里写了。
  - 顺带修:抽象类的 `Destroy_<X>` DllImport 条件漏了 `subclassCtor`(只有 `ctors.Count > 0`)。
  - 顺带修:**`setInterval` 现在有两个重载**(`int` / `TimeSpan`),属性解析原来按"名字"取 setter,会取到 `TimeSpan` 那个 → `QTimer.Interval` 属性整个消失。改成**按托管类型匹配** setter(`QTimer` 恢复 5 个属性)。
- **`QtSharp.slnx` 更新**:加进 `tools/clangprobe/ClangProbe.csproj`(新前端工具),**排除** legacy 的 `testapp/testapp.csproj` 与 `tools/rebuild-generator`(前者是旧链路 PoC,后者只在改 vendored CppSharp 时要);这样 VS/VS Code 打开就是新链路的三个工程,`dotnet sln QtSharp.slnx list` 可校验。
- 门禁:model 逐字节一致(352 类 / 8367 方法 / 852 虚函数)、codegen 两次一致、native+C# 编译、**8 个 demo + 3 个 PoC 全 PASS**(`api-core` 扩到 83 项)。

**⑲ 本轮完成:输入事件整族 + QtCore batch 3(325→445 类)+ 富文本类型,并把 Calculator/SpinBoxes 搬过来**
一句话:**把"事件只是不透明指针"这个遗留限制彻底去掉了**,顺带把 QtCore 的运行时服务与富文本类型补齐。
- **输入事件整族入库(30 个类,`bindCtors: false, owned: false`)**:`QInputEvent`/`QKeyEvent`/`QWheelEvent`/`QHoverEvent`/`QEnterEvent`/`QTabletEvent`/`QNativeGestureEvent`/`QTouchEvent`/`QScroll*Event`/`QExposeEvent`/`QFileOpenEvent`/`QChildWindowEvent`/`QWindowStateChangeEvent`/`QScreenOrientationChangeEvent`/`QPlatformSurfaceEvent`/`QShortcutEvent`/`QInputMethod*Event`/`QHelpEvent`/`QStatusTipEvent`/`QWhatsThisClickedEvent`/`QToolBarChangeEvent`/`QIconDragEvent`/`QDrag*Event`/`QDropEvent`/`QInputDevice`/`QPointingDeviceUniqueId`;`VirtualAllow` 加 `tabletEvent`。
  - 这些原先在 `OpaquePointers` 里 —— 那是 **CppSharp 前端**的限制(含 `QInputEvent` 的 chunk 解析永不可复现),`extract2` 用 clang 一次解析即确定,**根本没有这个限制**;`OpaquePointers` 现在只对 legacy 前端有效(注释已改)。
  - 验证(`api-events`,22 项):`QWheelEvent::angleDelta`(合成 120° → 960 八分之一度)/`position`/`modifiers`、`QKeyEvent::key`(65)/`text`、鼠标事件仍从**已绑基类链**上取 `position`。
- **QtCore batch 3**(30 类):`QEventLoop`/`QEventLoopLocker`/`QBasicTimer`/`QBuffer`/`QMetaClassInfo`/`QMetaEnum`/**`QMetaProperty`**(元对象终于可反射)/`QItemSelectionRange`/`QRegularExpressionMatch(+Iterator)`/`QUrlQuery`/`QCollator(+SortKey)`/`QCryptographicHash`/`QMessageAuthenticationCode`/`QTranslator`/`QLibraryInfo`/`QStorageInfo`/`QMimeDatabase`/`QMimeType`/`QFileSystemWatcher`/`QSharedMemory`/`QSystemSemaphore`/`QNativeIpcKey`/`QOperatingSystemVersion(+Base+Unexported)`/`QTextBoundaryFinder`/`QSignalMapper`/`QRandomGenerator64`/`QRecursiveMutex`/`QSocketNotifier`/`QXmlStream*`/`QCborValue` 家族。
  - `QOperatingSystemVersionUnexported` **必须**一起入库:`QOperatingSystemVersion : QOperatingSystemVersionUnexported : QOperatingSystemVersionBase`,少一层 `majorVersion`/`minorVersion` 就整个消失(和 batch 2 的 `QIODeviceBase` 同一个坑)。
  - 验证(`api-core` 83 → **110** 项):MD5(对已知 `900150983cd24fb0d6963f7d28e17f72`)、`QRegularExpressionMatch` 捕获组、`QUrlQuery` 编码、`QMetaProperty::read` 走 QVariant、`QMetaEnum::valueToKey`、嵌套 `QEventLoop` 被定时器退出、`QBasicTimer`、`QLibraryInfo::path`、`QStorageInfo::root`、mime 类型、`QCollator` 数字模式、边界查找、文件监视(信号真的投递)、共享内存、`QRgba64`/`QPixelFormat`/`QStaticText`/`QPixmapCache`/`QItemSelectionRange`。
- **富文本整族(21 类)**:`QTextFormat`(**它一入库,已绑的 `QTextCharFormat`/`QTextBlockFormat`/`QTextListFormat`/`QTextTableFormat` 才第一次有基类**)/`QTextLength`/`QTextFrameFormat`/`QTextImageFormat`/`QTextTableCellFormat`/`QTextDocumentFragment`/`QTextTableCell`/`QTextBlockGroup`/`QTextFragment`/`QTextBlockUserData`/`QTextLine`/`QTextInlineObject`/**`QAbstractTextDocumentLayout`**(布局对象,`blockBoundingRect` 是触发排版的入口)/`QTextObjectInterface`(加进 `VirtualExtensionClasses`,自定义文本对象)/`QRegularExpressionValidator`/`QStaticText`/`QRawFont`/`QPixmapCache`/`QPixelFormat`/`QRgba64`/`QMovie`。
  - **注意**:`QTextBlock::layout()` 只有在文档排版之后才有行(直接读 `lineCount()` 得 0,且对无效 `QTextLine` 调 `naturalTextWidth()` 会 **0xC0000005**);正解是先 `documentLayout().blockBoundingRect(block)` 逼出排版。
- **为跑通新类修的抽取器规则(4 条,全部来自实测)**:
  1. **`= delete` 的成员不是 API**:`QCborValue(const void *) = delete` 被当成真构造函数发布 → 生成 `QCborValue(void*)` 与 `(int)`/`(bool)` 重载**歧义**(C2668)。现在 `CXXMethod_isDeleted` 直接丢。
  2. **没有枚举项的 `enum class` 是强整数,不是枚举**:`enum class QCborTag : quint64 {}` 会被当成"枚举"放行(枚举是开放词汇表),但枚举表只收有项的 → 名字落到 codegen 里被当成**绑定类** → `QCborValue::tag()` 生成出返回 `void*`。**故意不收**(映射成底层整数的话,每个签名的 `static_cast<QCborTag>` 又无从得知;作为枚举则是没有成员、调用方无法命名的类型)——只损失 4 条 QCbor 事实。
  3. **默认参数里"像枚举"的常量必须核验**:`QNativeIpcKey::DefaultTypeForOs` 长得和枚举项一模一样,其实是**类的 static constexpr 成员**;旧规则只看拼写 → 生成 `QNativeIpcKey.Type.DefaultTypeForOs`(不存在的成员)→ CS0117。现在 `IsEnumeratorOf` 用解析索引确认"某个作用域匹配的枚举里真有这一项";核验失败且参数是枚举/`Class::Enum` 类型时**整条尾部截断**(C# `default`=0 对 `QNativeIpcKey::Type` 恰好是**保留的非法值**)。
     顺带修默认值的两种 Qt 写法:`= {}` / `Type()`(值初始化 → `0`)与 `Type(A|B)`(花括号/圆括号里包枚举表达式,递归解)。
  4. **`ExtraEnums`:不在任何签名里出现、但调用方必须要的枚举**。`QKeyEvent::key()` 返回的是 `int`,于是 `Qt::Key`(每个按键处理器都要比对的表)整个不在 model 里;修饰键也只以 QFlags 名(`Qt::KeyboardModifiers`)进过 model。现在 `ExtraEnums = { Qt::Key, Qt::KeyboardModifier, QTextFormat::FormatType }` 显式补进枚举表(`api-events` 里 `Qt.Key.Key_A` 就是它)。
- **为跑通新类修的 codegen 规则**:`QList<const T*>` 返回(如 `QInputDevice::devices()` → `QList<const QInputDevice*>`)不能 `new QList<T*>(call)` 拷贝(QList 不协变,C2665)→ class 指针元素的容器返回改为**逐元素 `const_cast` 拷贝**。
- **旧 demo 搬进新链路 2 个**(都进了门禁):
  - **`api-calculator`**(12 项,对齐官方 `examples/widgets/calculator`):托管 `QToolButton` 子类 `CalcButtonApi` 重写 `sizeHint`(Qt 布局真的用了它:managed 41×41 vs plain 22×21)+ **`keyPressEvent` 重写**(读 `QKeyEvent::key()` 驱动按钮)—— 正是"输入事件没绑"时做不到的那一半;完整 pending-operator 逻辑(含 `2+3×4=14` 的优先级与除零 `####`)。
  - **`api-spinboxes`**(25 项,对齐官方 `examples/widgets/spinboxes`):spin box 属性面(range/singleStep/prefix/suffix/specialValueText/displayIntegerBase/**`showGroupSeparator`** —— 注意 Qt 里属性名是 `showGroupSeparator` 而旧链路 CppSharp 叫 `GroupSeparatorShown`)、`QDateEdit`/`QTimeEdit`/`QDateTimeEdit`、精度 spin box 联动 3 个 double spin box。
    - **官方用 `textActivated`(只有真实用户交互才发射)**,测试造不出来 → 用 `currentIndexChanged` 代替,handler 与官方一致(与 `api-lineedits` 的处置相同)。
- **native 新增两个探针**:`qs_api_probe_wheel`(合成滚轮,delta 走 **Y** 轴 —— 第一版写成 X 轴,折腾了一轮)与 `qs_api_probe_key_press`;`ApiRuntime.SendWheel/SendKeyPress`。
  - 注意:滚轮事件必须送给**已 Show 的** widget(Qt 会按 widget 树路由),隐藏 widget 上时投递不稳定。
- **数字**:352 → **445 类**(QtCore 81→125、QtGui 85→136、QtWidgets 184)、8367 → **9523 方法**、852 → **858 虚函数**、393 → **444 枚举**、772 → **801 属性**、258 → **266 信号**;model 仍逐字节可复现、codegen 仍两次一致。
- **发现的既有瑕疵(下一步的好入口)**:`owned: false` 现在是"**永不 delete**"的意思,而 `DerivesFromQObject` 是**按 chunk** 判断的,于是**直接继承 `QObject` 的类**(`QSharedMemory`/`QEventLoop`/`QTimer`/`QFileSystemWatcher`…)没有 `Dispose`(只能靠 Qt 的 parent 释放,或泄漏),而 `QPushButton : QAbstractButton : QWidget : QObject` 这类跨头文件的却有。A1 的 `if (parent()) return;` 已经让"owned=true 也安全",所以正解是**让 owned 沿全量基类图判断**(或按"能不能终结"统一给 Dispose + dtor 守卫),但 item 类(`QGraphicsRectItem`,非 QObject、由 scene 拥有)**必须保持 owned:false**。`api-calculator`/`api-spinboxes` 里已经没有"必须 keepAlive"那套旧写法了(有 parent 的对象由 Qt 持有)。

**⑳ 本轮完成:绑定的"用户侧"入口(props + samples/hello)+ 统一 `owned` 语义(那条瑕疵已修)**
- **`QtSharp.Api.props` + `samples/hello/`(回答"我想自己写界面怎么做")**:以前新绑定只能从 `testbind` 里用 —— 一个用户拿到仓库**没有可用入口**。现在:
  - `runtime/ApiRuntime.cs` 从 `testbind/` 移到 `runtime/`(**它是随绑定发布的托管运行时,不是测试代码**);`QtSharp.Api.props` 把"生成的 `out/api/QtSharp.Api.cs` + 这份运行时 + `QtSharp.Api.dll` + Qt 运行时 DLL + `platforms/qwindows.dll`"收进一个 `<Import>`;`regen-api.ps1` 不再把生成文件拷到 testbind(两个工程都直接编译 codegen 写出的那一份,少一个会漂移的副本)。
  - `samples/hello` = 一个 70 行的**真界面**(窗口 + layout + `ClickedSignal` 处理器 + 一个从 C# 画自己的 `CircleWidget`,`Rect`/`Width` 都是属性),`dotnet build` 后直接跑;门禁 **build+run 它**(`HELLO_AUTOCLOSE_MS` 自动退出),所以"用户侧入口"这条路径不会烂。
- **★ 统一 `owned` 语义**(② 里排第一的下一步):**"Qt 是否拥有这个对象"是关于对象的问题,不是关于类的问题** —— 模型不再回答它。
  - 旧规则 `owned = Roots 标志 && 不继承 QObject`,而"是否继承 QObject"是**按 chunk(同头文件)**判断的 → 结果恰好反了:程序自己创建、本该能释放的 `QSharedMemory`/`QTimer`/`QThread`/`QEventLoop`/`QUndoStack`/`QIntValidator`/`QTextDocument` 一个 `Dispose` 都没有,而 `QPushButton` 有。现在 `owned` **只**取 Roots 标志;运行期的两半由生成代码负责:`__ownsNative`(只释放自己创建的)+ dtor 里的 `if (object->parent()) return;`。
  - **41 个类因此第一次有了 `Dispose`/终结器**(`QAbstractItemModel`/`QAbstractTableModel`/`QAbstractItemDelegate`/`QTimer`/`QThread`/`QEventLoop`/`QFileSystemWatcher`/`QCompleter`/`QTextDocument`/`QGraphicsScene`/`QSharedMemory`/`QSystemTrayIcon`/手势家族/…),另有 13 个"拿到了标志但没有公开构造函数"的抽象类照旧没有释放路径(`QLayout`/`QValidator`/`QStyle`/`QClipboard`/`QScreen`/`QCoreApplication`…)。
  - **`QObject` 自己永远不给释放路径**:释放路径是**继承**的,否则一个标了 `owned:false` 的子类(`QMimeData`、图形 item)会通过基类拿到 `Dispose`,那正是 `owned:false` 要防的事(实测:加上 QObject 的 `Dispose` 后,`QMimeData` 就真的"可释放"了)。
  - **Qt 用"没有 QObject parent"的方式收养的类保持 `owned:false`**:`QMimeData`(剪贴板/`QDrag` 收养)、**图形 item 整族**(item 的归属是 `parentItem()/scene()`,QObject 根本看不见 —— 新加了 `QGraphicsObject`,它虽然是 QObject 但同样是 item)、图形 effect/transform(`setGraphicsEffect`/`setTransformations` 收养)。这些正是"按类判断"永远判断不准的部分,现在按"收养方式"逐个标注并写明理由。
  - `api-ownership` **19 → 24 项**:新增"我们自己创建的 `QTimer` 的 `Dispose` 真的删掉了它"(用 `ApiRuntime.Connect(handle, "destroyed()", ...)` 收到 Qt 的 `destroyed` 信号作为证据)、"有 parent 的 `QTimer` 的 `Dispose` 不会删"、"parent 是编辑器的 `QTextDocument` 的 `Dispose` 不会删"、"`QMimeData` 故意不可 `Dispose`"。
- **踩到的坑(写进"给新 session 的提醒")**:改 `build_api.bat`(native)后**必须**重建托管侧再跑 —— 中间态(native 是新的、托管仍是旧的)会让 `ApiNative.Destroy_<X>` 落到错误的函数上,表现为**偶发**的 `0xC0000005`(实测过一次 `QAbstractButton_text`,重建后 10/10 干净)。

**㉑ 本轮完成:托管**树模型**(protected `createIndex`/`beginInsertRows`…)+ Shortcut Editor 搬过来(旧的那个点一下就崩)**
起因是用户点开旧链路 Shortcut Editor 的树节点就崩(实测:手工点崩、无人值守也有 ~1/7 概率崩在 8 秒后)。结论:**旧链路那个 demo 的 model/delegate 生命周期不可靠**(旧绑定会释放 Qt 还持有的东西),而新链路此前**根本写不了树模型** —— 因为 `QAbstractItemModel::createIndex` 是 **protected**。所以修的是新链路的能力,顺手把 demo 搬过来。
- **抽取器:发布扩展类里的 protected 成员**(`gen/ApiModel2.cs`)。规则:公开成员照旧;**扩展树内**(`IsInExtensionTree`)且类可子类化时,protected 成员也发布,并在 model 里记 `"protected": true`。排除两种形态:protected **static**(没有对象可挂,`QThread::setTerminationEnabled` 直接不要)与**带 by-ref 参数**的(生成的是表达式 helper,out 参数是语句)。
  `QAbstractItemModel` 因此多出 **21 个方法**:`createIndex`×2、`beginInsertRows`/`endInsertRows`、`beginRemoveRows`/`endRemoveRows`、`beginMoveRows`(及 Columns)、`beginResetModel`/`endResetModel`、`changePersistentIndex(List)`、`persistentIndexList`、`encodeData`/`decodeData`。
- **codegen:C++ 只允许"成员"调 protected 成员**,所以这些方法由生成的 native 子类暴露 `static CallXxx(QsApi_X* self, ...)`(就是 `CallBase_*` 的那个形状),extern "C" 转发器先 `static_cast<QsApi_X*>(self)` 再调 helper ✓。helper 的参数用**扁平 C ABI 类型**(它自己做转换),否则 `QDataStream&` 这类"编译器才知道 const 不 const"的引用没法拼。
- **顺带修两个静默 bug**:
  1. **生成的 `ToString()` 之前是 `new`(隐藏)而不是 `override`** → 字符串插值 `$"{variant}"` 打印出 `QtSharp.Api.QVariant` 而不是值(显式调用 `.ToString()` 却是对的 —— 极难查)。现在 `QString toString() const` 一律发 `public override string ToString()`。
  2. **`Qt::ItemDataRole` 不在 model 里**:role 在签名里全是 `int`,`data(index, role)` 那张表因此缺了整个 —— 加进 `ExtraEnums`(和 `Qt::Key` 同一机制)。
- **新 demo `api-shortcut`**(`testbind/ShortcutEditorApi.cs`,18 项断言,进门禁),对齐官方 `examples/widgets/widgets/shortcuteditor`:
  - **三层树模型**(context → category → action)用 C# 写:`CreateIndex(row, column, (ulong)id)` 建索引、`index.InternalId()` 取回、`BeginInsertRows/EndInsertRows` 公告结构变化、`BeginResetModel/EndResetModel` 重建。
  - `QStyledItemDelegate` 子类用 `QKeySequenceEdit` 编辑快捷键列(`CreateEditor`/`SetEditorData`/`SetModelData`/`UpdateEditorGeometry` 全重写)。
  - 断言覆盖**崩过的那条路径**:`ExpandAll()` + `SetCurrentIndex` + `Grab()`(触发 Qt 回调模型的 `data`/`rowCount` 与委托的 sizeHint)、索引 id 往返、委托的编辑器契约、插入一行后视图仍能渲染,以及**反复 full GC 后 model/view 仍然可用**(旧 demo 的崩溃模式)。
  - **注意 `Data(index, role)` 的第二个参数是 role 不是 column**(我一开始写成 `Data(index, 1)` 想读第二列,结果拿到空 variant;两列要用两个 column 索引)。
- **旧链路 `shortcut` demo 已从 `Program.cs` 与 `AllDemos` 移除**(15 → 14 个窗口):它崩,而且 `api-shortcut` 已经取代它。类 `ShortcutWindow` 保留在源码里(旧链路随时可删)。
- **`ApiRuntime.SendMousePress` 现在在 `Exec()` 之前发**(`api-analogclock`):往一个已经 `quit()` 的应用里投递鼠标事件的返回不可靠(Qt 自己的 dispatch 会看应用状态),表现为偶发失败 —— 和绑定无关。
- **环境相关的 SKIP**:`QClipboard` 是单所有者资源,别的进程占着时 `setText` 静默失败、读回空串(PowerShell 自己的 `Set-Clipboard` 同时也会报错);`QClipboard::error()` 在 **Qt 6.11 已经不存在**,没法查错误码。`api-core` 因此把剪贴板往返改成**读回非空才断言、否则打 `[SKIP]`**(实测用户机器上就遇到了这个状态)。
- 验收:model 逐字节一致、codegen 两次一致、**12 个 demo + sample + 3 个 PoC 全 PASS**(347 项断言);旧链路 14 个 demo 各存活 9 秒 + all-mode 12 秒无崩溃。
- **下一步(㉑ 暴露出的最大缺口)**:已由 ㉒ 完成。

**㉒ 本轮完成:信号的"收"与"发" + **C# 自己声明 Signal/Property**(PyQt/PySide 的 `pyqtSignal`/`Property` 对等物)**
一句话:信号从"只能连标量"变成**全类型可连、可发**,并且**托管类可以在 C# 里声明自己的信号和属性**,运行期用 `QMetaObjectBuilder` 生成 meta-object 装到对象上。
- **① 信号采集换成"头文件扫描定位 + AST 取参数"**(`gen/ApiModel2.cs` 的 `SignalFactsOf`):`Q_SIGNALS` 在 clang 里只展开成 `public`,没有可识别的痕迹,所以**哪些声明是信号**仍由头文件扫描决定;但**参数类型改从 AST 的 `FnFact`/`ParamFact` 拿** —— 于是类指针(`commitData(QWidget*)`)、boxed 值(`sizeHintChanged(QModelIndex)`)、枚举(`closeEditor(..., EndEditHint)`)、容器(`dataChanged(..., QList<int>)`)**全都能进信号**,而旧规则只有 bool/int/qint64/QString/QStringList。
  - **moc 签名 = model 类型**,**不是** clang 拼写!实测 `indexOfSignal("objectNameChanged(const QString &)")` = **-1**,而 `"objectNameChanged(QString)"` = 2;`"destroyed(QObject *)"` = -1,`"destroyed(QObject*)"` = 0。`QMetaObject::normalizedSignature` 在 Qt 6.11 **不会**去掉 `const`/`&`/空格,而 moc 存的就是规范化后的名字(`Qt::Orientation`/`QAbstractItemDelegate::EndEditHint` 是**限定名**)。所以 model 的 `type` 直接当签名用,信号参数**不需要** `cpp` 字段。
  - 结果:**266 → 428 个信号**(+164),只"少"了 2 条(`QIODevice::bytesWritten` 的 `qint64` 从旧映射的 `long` 变成正确的 `long long`)。枚举表 444 → 448(信号带进来的枚举第一次入库)。
  - 同名重载信号(QCompleter 的 `activated(QString)` / `activated(QModelIndex)`)按方法的 2,3,… 后缀规则发成 `ActivatedSignal` / `Activated2Signal`。
- **② `Emit`**:native `qs_api_emit(sender, signature, args, argc)`。**关键坑**:`indexOfSignal` 给的是**绝对方法索引**,而 `activate` 要的是**信号序号**;两者不同(slot 与 signal 共用方法表),`index - methodOffset()` 会**静默激活错误的信号**。正解是 `QMetaObject::metatype` 那套:`QMetaObjectPrivate::signalIndex(method) - QMetaObjectPrivate::signalOffset(metaobject)`,再 `QMetaObject::activate(object, metaObject, local, argv)`。argv 约定是 moc 自己的(`argv[0]` = 返回槽,总是 nullptr;`argv[i]` 指向第 i 个参数的**存储**)。
- **③ 信号从 `QSignal<Action<...>>` 改成**按参数个数**的 `QSignal` / `QSignal<T1>` / … / `QSignal<T1..T8>`**:`Connect(Action<T1,T2>)` 与 **`Emit(T1,T2)`** 都是强类型。codegen 为每个信号发两个闭包:connect 侧用 `SignalRead(t, i)` 按类型解码 argv(**注意信号 argv 已经跳过返回槽,索引从 0 开始** —— 与虚函数的 argv 约定差一位,第一版写成 `i+1` 表现为"字符串信号拿到空串"),emit 侧用 `EmitParams`/`EmitLambda` 造出 moc 信号函数栈上那样的实参存储(`stackalloc void*[n]` + `&local`,字符串/容器走已有的 New/Free)。`ApiSmoke` 新增 3 项:emit 触发 handler、字符串参数、**类指针 + 枚举**两个参数各按自己的 ABI 走。
  - `ApiRuntime.Connect` 的签名从 `Action<object[]>` 换成 `SignalSlot(IntPtr argv, IntPtr typeIds)`,旧的手写调用点(`OwnershipApi` 两处)改成 `(_, _) =>`。
- **④ C# 自己声明信号/属性(本轮的重头)**:
  ```csharp
  public class Counter : QObject
  {
      public static readonly QMetaSignal<int> ValueChanged = new("valueChanged");
      public static readonly QMetaProperty<int> Value = new("value", o => ((Counter)o)._value, (o, v) => ((Counter)o)._value = v);
      public void Bump() { _value++; ValueChanged.Emit(this, _value); }
  }
  ```
  - native `qs_api_attach_metaobject`:用 **`QMetaObjectBuilder`**(私有但随 Qt 发布)+ `QAbstractDynamicMetaObject` 建一张 meta-object 并装到对象上(`QObjectPrivate::get(obj)->metaObject = ...`);`toMetaObject()` 的 `QMetaObject` 用 `memcpy` 拷进动态对象的 `QMetaObject` 基类(它自己的字符串数据要**保活**,按"成员清单"缓存,进程内共享)。属性读写由 `metaCall` 里 `id >= base->propertyCount()` 的分支转给托管的 `[UnmanagedCallersOnly]` 回调(值就在 `args[0]`,和 moc 的 `void *_v = _a[0]` 一致)。
  - **两个坑**:①`signals`/`slots` 是 Qt 的宏(`public`),参数名不能叫这个(C2143);②**不要用 `QMetaObjectBuilder(base)` 拷贝原型** —— 它把基类自己的方法/属性抄一份、`superClass` 设成基类的基类,结果 `indexOfSignal` 把新信号认成 **slot**(实测:1 个新信号是 slot、3 个里前两个是 signal)。正解是**空 builder + `setClassName` + `setSuperClass(base)` + 只加自己的成员**(`methodOffset` 由 `superClass()->methodCount()` 推出,正好和 moc 一致)。
  - 托管侧 `ApiRuntime.AttachMetaObject(this)` 由**每个生成的 QObject 构造函数**调用(只对 `DerivesFromQObject` 的类发):反射 `GetType()` 的静态字段(`QMetaSignalBase`/`QMetaPropertyBase`,含继承),按类型缓存 spec,没有声明的类只是一次字典查询。生成的 root 类新增 `IQtHandle`(让运行期能把绑定类当指针 marshalling,靠反射 `FromHandle`/`FromTransientHandle`)。
  - 支持的成员类型:标量、`string`(`QString`)、枚举(`Qt::ItemDataRole` 这种限定名)、绑定类指针;`MetaType.QtName` 负责 C# 类型 → Qt 类型名(嵌套枚举用 `DeclaringType.Name + "::" + Name`)。已知限制:**boxed 值类型(如 `QModelIndex`)按指针处理**(`QMetaProperty<QModelIndex>` 会得到 `QModelIndex*`);`QMetaSignal` 的声明必须是 **static 字段**(实例字段在基类构造函数跑 `AttachMetaObject` 时还没赋值),所以用法是 `Counter.ValueChanged.Emit(this, 1)` 而不是 `this.ValueChanged.Emit(1)`。
- **新 demo `testapp api-metaobject`**(`testbind/MetaObjectApi.cs`,14 项断言,已进门禁):meta-object 的 `className` 是托管类名、声明的信号/属性在表里、Qt 继承来的信号还在;0/1/2 参数与类指针信号的 Connect+Emit;`setProperty` 打到 C# setter、`property()` 读回;C# 子类继承声明;未声明成员的生成类完全不受影响。
- 验收:model **逐字节可复现且与入库一致**(`A1CFC453…`,445 类 / 448 枚举 / **428 信号**)、codegen 两次一致、native+C# 编译、**13 个 demo + sample + 3 个 PoC 全 PASS**(比上轮多 14 项断言)。
- **已知遗留**:`QMetaSignalBase`/`QMetaProperty` 只有 0–4 个参数的 arity(超过 4 个参数的用户信号暂不支持);动态 meta-object 的 `QMetaObject*` 进程内不释放(每个"成员清单"一个,可接受);属性回调不支持 `QVariant`/容器/boxed 值类型。

**㉓ 本轮完成:C# 里也能 `new QApplication(args)`(PyQt/PySide 那个写法)**
一句话:应用对象可以像 PyQt 一样在 C# 里造,`ApiRuntime.StartApplication()` 仍保留为不关心命令行的捷径。
- **为什么要手写**:`QApplication(int &argc, char **argv, int flags)` 里 `char**` 不在 model 词汇表里、`int&` 又撞上"构造函数不允许 by-ref 参数"的规则,所以生成的类**没有构造函数**(`QApplication`/`QGuiApplication`/`QCoreApplication` 三个都是 `ctors 0`)。等价能力放在**随绑定发布的运行时**里,和 sip/shiboken 为这一条签名手写转换是同一个选择(旧链路其实也一样:`QtSharp.Widgets.cs` 生成的是 `QApplication(ref int argc, sbyte** argv)`,而 `Program.cs` 用的 `new QApplication(string[])` 是手写在 `testbind/QApplication.cs` 的 partial class,里面 `AllocHGlobal` 之后故意不释放)。
  - **新增 `runtime/ApiApplication.cs`**(已加进 `QtSharp.Api.props`):`QCoreApplication`/`QGuiApplication`/`QApplication` 各一个 partial 构造函数 `(string[] args, int flags = 0)`,走 `ApiRuntime.CreateApplication(args, flags, kind)`。因为 `Handle` 的 setter 是 protected,三份 `BindApplication` 只能各写一遍(类注释里写明了原因)。
  - **native `qs_api_app_create_args(argv, argc, flags, kind)`**(kind: 0 Core / 1 Gui / 2 Widgets):把命令行**拷进进程生命期的静态存储**(Qt 会一直持有 argv 指针,而且会改写 argc),`qs_api_app_create()` 保留为无参包装。
  - **单例**:第二次构造返回已存在的那个对象;如果宿主自己已经建了一个(同进程的旧链路、或嵌入我们的 C++ 程序),则**接管它而不是再建第二个** —— Qt 只允许一个,而第二个构造函数会静默把 `QCoreApplication::instance()` 抢过去、第一个对象却还活着(见下面的 0xC0000005)。
  - **没有 `Dispose`**:`QApplication` 在 model 里 `ctors 0` ⇒ 不生成 `Destroy_`/`Dispose`,所以 GC 永远不会删掉应用对象(这正是我们要的)。
- **★ 关掉时的两颗雷(都实测过)**:
  1. 从 **DLL 静态析构**里删应用对象会在 CRT 退出序列的中段跑 Qt 的拆解,此时 `QGuiApplication` 自己的全局已经没了(实测 `~QGuiApplication+0x38` 里 `mov rax,[rcx]`、`rcx=qApp=0`)。修法:新增 `qs_api_app_destroy()`,由托管在 **`ProcessExit` 里、CLR 还活着的时候**调用 —— 这就是 Qt 自己"`QApplication app;` 是 main 里的局部变量、静态析构之前就销毁"的那套顺序。`Instance()`/`OwnsInstance()` 都只是**指针/布尔**的静态,平凡析构 ⇒ 不注册任何 atexit。
  2. **旧链路的 `QApplication` 和新链路的撞在一起**:`api` 这个命令原来在 `Program.cs` 里排在 `using var app = new QApplication(...)`(旧链路)之后,于是进程里**两个 QApplication**;`Main` 结束时旧的那个 `using` 先 `Dispose` → `qApp` 被清空 → 我们再删自己那个就崩。修法是两处:`Program.cs` 把 `api` 移到旧链路 app 之前(文档里那条"新链路命令必须在旧链路 QApplication 之前分发"的规则,`api` 当初漏掉了),**并且** native 侧加了"qApp 不是我们就别删 + 宿主有就接管"的守卫。
- **用法**:`var app = new QApplication(args);`(sample `samples/hello` 已改成这个写法,门禁照跑);`QCoreApplication`/`QGuiApplication` 同理。`api` 冒烟新增 2 项:`new QApplication(args)` 拿到的句柄非空,且与 `QCoreApplication.Instance().Handle` 相同。
- 验收:model 逐字节一致、codegen 两次一致、**13 个 demo + sample + 3 个 PoC 全 PASS**,且 13 个 demo 的**退出码全部为 0**(本轮之前 `api` 是 `0xC0000005`)。

**㉔ 本轮完成:旧 demo 迁移第一批(ShapedClock / Sliders)+ 旧链路的删除勘察**
- **新链路 demo 13 → 15**:`api-shapedclock`(13 项断言;无边框 + 半透明 + `QRegion` 圆掩码 + 鼠标拖拽,`QMouseEvent::buttons()` 已绑所以不用旧代码那个 `dragging` 退化写法)、`api-sliders`(40 项;四组 slider/scrollbar/dial + resize 翻转布局方向)。两者各跑 10 次退出码全 0。
- **`testbind/WindowFlagsApi.cs` 已写好但"停牌"**(115 项断言全 PASS,但**不接进 `Program.cs`/门禁**):进程退出时约 **3/10** 概率 `0xC000041D`。**tracer 栈:崩在 `qwindows.dll` 内,从 `PeekMessageW` → `CallWindowProcW` → `KiUserCallbackDispatcher` 进来** —— 即运行时拆应用对象时 Windows 还在给一个 Qt 侧数据已销毁的窗口投消息。只有这个 demo 触发,因为它把预览窗口在所有 `Qt::WindowType` 上走了一遍(含官方示例没有的 `Desktop`/`SubWindow`/`ForeignWindow`/`CoverWindow`)并反复 show/hide。**一个 1/3 概率挂的 demo 不能进门禁**,文件头写了原因;等拆解那条路查清再接线。
- **旧链路可删性勘察(结论:现在还不能删)**:两个只读子 agent 的清单——
  - **已被新链路完整取代、可直接删**:`LineEdits`/`Calculator`/`SpinBoxes`/`ShortcutEditor`(后者本来就已从 dispatch 摘掉,是死代码)+ `AnalogClock`(新的是精简版)。
  - **还没搬**:`Sliders`(✅ 本轮)、`ShapedClock`(✅ 本轮)、`WindowFlags`(⏸ 停牌)、`CalendarWidget`、`Scribble`、`Menus`、`BasicLayouts`、`SystemTray`、`WidgetsGallery`、`DocumentViewer`。
  - **迁移时确认的模型缺口**:`QSettings`+`QSettings::Scope`(DocumentViewer 的唯一硬缺口;**注意新模型已把 `QAnyStringView` 当 string,大概率只是加 Roots 行**)、`QStyleFactory`(WidgetsGallery 只差它枚举样式名)、`QComboBox.addItem(QString,QVariant)`(Calendar/SystemTray,可用 `AddItem(text)`+`SetItemData` 绕过)、`QLocale::Territory`、`QInputDialog.getInt(...,bool* ok)`、`QPen(QColor,int,...)` ctor(可用 `QBrush`+setter 绕)。**没有新架构问题。**
  - **永久不可行**:运行时不带 `Qt6PrintSupport.dll`/`Qt6Pdf.dll`/`Qt6Svg.dll`(Scribble 打印、DocumentViewer PDF/打印只能空实现);原生插件(官方 documentviewer 的 viewer 插件)无法从托管加载;`Qt::RHI`/Window Embedding 同前。
  - **删除顺序(每步都可编译)**:①先删独立旧 PoC(`testapp/`、`native/qt_sharp_min.cpp`、`build_native.bat`、`run_test.ps1`,零风险);②把 `testbind/Program.cs` 的 legacy 派发/PoC/`AllDemos`/`using QtSharp.QtWidgets` 与 14 个 legacy demo、`QtCompat.cs`/`QApplication.cs`/`Signals.cs`/`Stub.cs`/`QStringMarshaller*s` 一起删,并从 `testbind.csproj` 去掉 `CppSharp.Runtime` 引用与 `CopyLegacyNativeDeps`;③删 legacy native(`build_glue.bat`、`dump_exports.bat`、`native/QtSharp.def`(唯一被 git 跟踪的生成物,要 `git rm`)、`QtSharp_inline.cpp`、`QtSharp_{meta,vdispatch,ownership}.cpp`、`out/native`、`out/qt_min`);④**`gen/ApiModel.cs` 与 `gen/Program.cs` 是共享文件,必须逐块切**:保留 `ApiTypes`、`Roots`/`ActiveRoots`/`RootClasses`/`BindsCtors`、`SupportedTypes`/`BuildVocabulary`、`VirtualAllow`、`ModelPart`/`Serialize`、`WriteSignals`/`SignalParamJson`(去掉 `ModelType == null` 那条 legacy 回退后才能删 `Program.MapSignalType`)、`WriteEnum`、`Json`/`CleanName`/`SignalModelType`,以及 `Program` 的 `QtIncludeRoot`、`SignalInfo`/`PropertyInfo`、`ScanProperties`/`ScanSignals`/`TryParseSignal`;删 `QtLibrary`/`GenerateDef`/`GenerateSignals*`/`FixDtorHooks`/`dumpopts`/`extract`、`gen/*TypeMap.cs`、`gen/sample/`;⑤最后删 `CppSharp/` 与 `tools/rebuild-generator/`;⑥`tools/regen-api.ps1` 去掉 `-Extractor1`。**⚠️ 绝对不能删 `lib/lib/clang/19`**(`gen/ApiModel2.cs` 的 `ResourceDir()` 靠它当 builtin headers,`gen.csproj` 还要拷到输出)。`CrashTracer.cpp`/`build_crashtracer.bat`/`tools/disasm.py`/`tools/clangprobe` 与新旧链路无关,**保留**。

**㉕ 本轮完成:清掉已被取代的旧链路 + 迁移第二批(CalendarWidget / Menus / BasicLayouts)**
- **清理(旧 demo 14 → 6,都是可编译、可运行地删)**:删掉独立的旧 PoC(`testapp/`、`native/qt_sharp_min.cpp`、`build_native.bat`、`run_test.ps1`,零风险)和 **9 个已被新链路完整取代的 legacy demo**:`LineEdits`/`Calculator`/`SpinBoxes`/`ShortcutEditor`(后者本来就是死代码)、`CalendarWidget`/`MainWindowExamples`(Menus)/`LayoutExamples`(BasicLayouts)(本轮新端口验证通过后删)、`Sliders`/`ShapedClock`(上一轮的新端口已覆盖)。同时从 `Program.cs` 的 legacy switch 与 `AllDemos` 摘掉对应条目 —— `testapp` 不带参数的 all 模式**从 14 个窗口变成 6 个**(analogclock / docviewer / gallery / scribble / systray / windowflags),实测 `[OK] 6 demos shown side by side` 无 stderr。
- **迁移第二批(都**不需要**改 model,纯机械活)**:`api-calendarwidget`(59 项,四组 Preview/General Options/Dates/Text Formats;**用上了 legacy 用不了的 `QDateTimeEdit.dateChanged`**,去掉了 `editingFinished` 近似)、`api-menus`(55 项,`QActionGroup` 独占对齐动作 + 状态栏提示 + 上下文菜单,**去掉了 `SetShortcutString` shim**)、`api-basiclayouts`(55 项,layout 里的菜单栏 + 三组 + 滚动区 + 按钮盒)。三者各跑 5 次退出码全 0。
- **新链路 15 → 18 个 demo**;model 仍逐字节一致、codegen 两次一致、门禁全绿。
- **`WindowFlagsApi` 仍停牌**(见"㉔")。**剩余 legacy 只有 6 个**:`analogclock`(新的是精简版,值得补一次完整移植再删)、`docviewer`、`gallery`、`scribble`、`systray`、`windowflags`。下一批要补的小模型项:`QStyleFactory`(gallery)、`QSettings`+`QSettings::Scope`(docviewer)、`QComboBox.addItem(QString,QVariant)`(systray,可先用 `SetItemData` 绕);`systray` 还差把 `QDialog::setVisible` 发布成可重写虚函数;`windowflags` 要先把退出期 `qwindows` 投消息那条路查清。

**㉖ 本轮完成:迁移第三批(Scribble / WidgetsGallery / SystemTray),旧 demo 6 → 3**
- **新链路 18 → 21**:`api-scribble`(66 项;离屏 `QImage` 画笔区 + File/Options/Help 菜单 + Save As;`QMouseEvent::buttons()` 与 `QImageWriter.supportedImageFormats()` 都已绑,legacy 的两个退化写法去掉了)、`api-gallery`(78 项;整棵 widget 面板树 + 三态复选框 + item view + 手动步进的进度条)、`api-systray`(63 项;托盘图标/菜单/动作、两种 `ShowMessage` 重载、close-to-tray 逻辑、`activated` 信号真的 `Emit` 走了一遍)。三者各跑 5 次退出码全 0。
- **清理**:随新端口验证通过删掉 `Scribble.cs`/`WidgetsGallery.cs`/`SystemTray.cs`,legacy all 模式 **6 → 3 窗**(analogclock / docviewer / windowflags)。
- **迁移时确认的模型缺口(都是小项,没有架构问题)**:`QComboBox.addItem(QString,QVariant)`(用 `AddItem`+`SetItemData` 绕,Calendar/SystemTray 两处都是)、`QPen(QColor,int,style,cap,join)` 构造(用 `QPen(QColor)`+setter)、`QColorDialog::getColor(QColor,...)`、`QInputDialog::getInt(...,bool* ok)`、`QStyleFactory`(gallery 的样式名列表改为硬编码)、`QIcon` 没有拷贝构造;**`QDialog::setVisible` 不是可重写虚函数**,所以官方那条"窗口可见性驱动 minimize/maximize/restore 动作启停"只能退化成显式方法(`systray` 文件里写明了)。
- **剩余 legacy 只有 3 个**:`analogclock`(新的是精简版,值得补一次完整移植再删)、`docviewer`(需 `QSettings`+`QSettings::Scope` 入模型;PDF/打印仍不可行)、`windowflags`(停牌,退出期 `qwindows` 投消息那条路未查清)。
- **流程教训**:一个并行子 agent 首次派发时**静默失败**(没有产出文件、没有报告,`task` 返回空)。**派发后必须检查产物文件是否真的存在**(`Get-ChildItem`),不能只看返回的总结。

**㉗ 本轮完成:`QSettings` 入模型 + AnalogClock 保真移植 + DocumentViewer 迁移;旧链路只剩 1 个 demo**
- **`QSettings` 入模型(`Roots` 加一行,模型 446 类 / 451 枚举,抽取器/codegen 一行没改)**:6 个 ctor(含 `(Scope, QString, QString)`)、`value`/`setValue`/`contains`/`remove`/`allKeys`/`childGroups`/`sync`/`status`,以及嵌套的 `QSettings::Scope`/`Format`/`Status`。这印证了㉔的判断("`QAnyStringView` 已按 string 处理,大概率只是加 Roots 行")—— legacy 的 4 个 `QtCompat` QSettings shim 全部作废。
- **`api-analogclock` 从"精简版"改成官方保真移植(10 → 27 项断言)**:官方配色 `(127,0,127)` / `(0,127,127,191)`、官方 3 点指针多边形 `{7,8,-7,8,0,-40}` / `{0,-70}`、12/60 刻度与 `rotate(30)/rotate(6)` 循环、`min(w,h)`+`translate(w/2,h/2)`+`scale(side/200)` 数学;新增 `SetTimeForTest(QTime)` 接缝,让像素断言(时针/分针方向上的像素质心、12 个时针刻度、48 个分针刻度)不依赖真实时钟。
- **新增 `api-docviewer`(100 项)**:菜单/工具栏/状态栏骨架 + `QSplitter` + 西侧 `QTabWidget(Pages/Bookmarks)` + `WidgetResizable` 的 `QScrollArea`;**`QSettings` 持久化工作目录与最近文件**(用临时 scope,不碰用户真实设置);按内容类型内联查看器(text=`QPlainTextEdit`、JSON=**手写 `QAbstractItemModel` 子类**、image=`QLabel` + `QPixmap.FromImage`/`Scaled`)。PDF/打印仍不可行(运行时不带 `Qt6Pdf.dll`/`Qt6PrintSupport.dll`),官方原生 viewer 插件仍无法从托管加载。
- **旧链路只剩 1 个 demo**:删掉 `AnalogClock.cs`、`DocumentViewer.cs`、`QtCompat.cs`(它的最后一个使用者没了)和 `AllDemos.cs`(`testapp` 不带参数的 **all 平铺模式取消**,改成打印用法)。legacy 派发里只剩 `windowflags` —— **现在整个 CppSharp 旧链路只由"1 个停牌 demo + 3 个 PoC"撑着**,㉔ 的 6 步删除计划已经具备前提。
- **一个未解决的偶发崩溃(重要)**:`api-windowflags`(约 3/10)与 `api-systray`(约 1/22)会在断言全过、打印 `[SELFTEST OK]` **之后**偶发以 `0xC000041D` 退出。tracer 栈:崩在 `qwindows.dll` 内,由 `PeekMessageW → SendMessageW → CallWindowProcW → KiUserCallbackDispatcher` 进来,而**调用者是 `combase.dll`(COM/OLE 自己的消息泵)** —— 即运行时在 `ProcessExit` 里拆 QApplication 时,Windows 还在给一个 Qt 侧数据已销毁的窗口投消息。只在"创建了托盘图标 / 把所有 `Qt::WindowType` 走一遍"的这两个 demo 上出现。本轮的缓解是让 `SystemTrayApi` **返回前先 `tray.Visible = false; window.Hide();`**(把气球消息和 tray 从 shell 撤掉);改完 6/6 + 整轮门禁都过了,但**基线本来就只有 ~1/22,这点样本量说明不了什么**。`windowflags` 仍停牌。**两条待验证假设**:(a) 把"销毁应用"从 `ProcessExit` 提前到 demo 返回前(此时 COM 健康);(b) 干脆不销毁(泄漏)——(b) 曾在更早的版本上触发过 "Attempt to execute managed code after the .NET runtime thread state has been destroyed",但那次测量之后运行时改动很大,值得重测。**验证需要几十次重复运行,而每次都会在桌面闪一个窗口**(这台机器**没有 `qoffscreen.dll`**,`runtime/platforms` 只有 `qwindows.dll`),所以要先和用户确认再跑。
- **流程教训(又一条)**:子 agent 给 DocumentViewer 的类起了 legacy 的同名(`DocumentViewerWindow`/`RecentFiles`),而 legacy 文件当时还没删 → `CS0101` 撞名。**派发时必须要求新类型带 `Api` 前缀**(已改成 `ApiDocumentViewerWindow`/`ApiRecentFiles`),否则"新文件单独编译"能过、"合进 testbind"才炸。
- 验收:model 逐字节一致(446 类 / 451 枚举)、codegen 两次一致、**22 个 demo + sample + 3 个 PoC 全 PASS**。

**㉘ 本轮完成:整个旧链路已删除(CppSharp 前端 / legacy native / legacy demo / PoC / 生成器源码全部清掉)**
> **状态:旧链路在本仓库里已不存在。`git log` 上是 `280aa0a`(gen/)之后这一步:`testbind` 只剩 `api-*` demo + 门禁(当轮 22 个,现已 25 个);**CppSharp 源码(`CppSharp/`)、`tools/rebuild-generator/`、`lib/CppSharp*.dll`、`lib/native/CppSharp.CppParser.dll`、`lib/native/Std-symbols.dll` 全部删除**;`native/` 只剩 `QtSharpApiRuntime.{h,cpp}` 与 `CrashTracer.cpp`;**唯一保留的第三方是 `lib/lib/clang/19`(libClangSharp 的 builtin headers,删不得)**。
> 验收:`powershell -File tools\regen-api.ps1` 全绿 —— model 逐字节一致(446 类 / 451 枚举)、codegen 两次一致、native+C# 编译、**demo + sample 全 PASS**(删除当轮是 22 个 demo);`dotnet build tools\clangprobe` 与 `dotnet sln QtSharp.slnx list`(gen / testbind / samples\hello / tools\clangprobe 四个工程)均正常。
> **下面保留本轮删了什么、以及"差点删错"的记录;㉔ 那 6 步删除计划到此收尾。**
- **已完成:`gen/` 里的 CppSharp 前端整块删掉**(`gen/Program.cs` 1239 → 273 行,`gen/ApiModel.cs` 1768 → 933 行)。保留的是新链路真正在用的:`QtIncludeRoot`;`SignalParamInfo`/`SignalInfo`/`PropertyInfo`、`ScanProperties`/`EnclosingClass`、`ScanSignals(dir,header)`/`ScanSignalsInFile`/`TryParseSignal`;`Json`/`CleanName`;`ApiTypes`、`Roots`/`ActiveRoots`/`RootClasses`/`BindsCtors`、`SupportedTypes`/`BuildVocabulary`、`VirtualAllow`、`ModelPart`、`WriteSignals`/`SignalParamJson`/`WriteEnum`;`Program.Main` 现在只有 `extract2` 和 `codegen`。删掉的是:`QtLibrary : ILibrary` 全部(`Setup`/`RootNames`/白名单/`LimitScope`/`CollectRoots`/`PruneMembers`/`GenerateDef`/`AddMethodDef`/`ReportCoverage`…)、`FixDtorHooks`/`FixDuplicateFields`/`DedupeStructs`、`SignalModules`+无参 `ScanSignals()`、`MapSignalType`、`GenerateSignals*`、`DumpParseOptions`、`RunExtract`/`ExtractChunk`/`ParseChunk`/`Extract`、`ModelLibrary`、`ParamJson`/`DefaultLiteral`/`ReturnTypeOf`/`VisibleParams`/`Supported`/`Normalize`/`EnumTypeName`/`CollectEnums`/`BoundBases`/`DerivesFromQObject`/`PublishesOwnership`/`SignalModelType` 等。`gen.csproj` 去掉 6 个 CppSharp `Reference` 与两个 native 拷贝;`gen/QFlagsTypeMap.cs`/`QStringTypeMap.cs`/`QStringListTypeMap.cs`/`gen/sample/Sample.h` 删除。
- **★ 两个"差点删错"的点(下个 session 必读)**:
  1. **`ScopeOfQualified`/`ScopeIsPublishable` 必须留** —— 之前的只读勘察清单没提,但它们被 `ApiModel2.cs`(843/851 行)调用;删了就是 `CS0103`。
  2. **`OpaquePointers` + `BuildVocabulary` 里那两个 `foreach` 必须留** —— 它不只是"legacy 的不透明指针表":`SupportedTypes` 靠它把 `QTimerEvent*`/`QChildEvent*`/`QDynamicPropertyChangeEvent*` 这些**非 Roots** 的拼写放进词汇表,删掉会改变 model(不再逐字节一致)。
  3. 顺带:`ApiModel.cs` 顶部的 `using AstType = CppSharp.AST.Type;` 别名要一起删。
- **剩下 5 步(已全部完成,每步都可编译)**:
  - **Step A(testbind 去 legacy)✅**:删了 `testbind/WindowFlags.cs`(**它没有替代品 —— 新端口 `WindowFlagsApi.cs` 停牌**,㉗)、3 个 PoC(`MetaSignal.cs`/`MetaSignalTest.cs`/`VirtualDispatch.cs`/`Ownership.cs`)、legacy helpers(`Signals.cs`/`Stub.cs`/`QStringMarshaller.cs`/`QStringListMarshaller.cs`/`QApplication.cs`),以及 gitignore 的生成物 `testbind/QtSharp.{Core,Gui,Widgets}.cs`/`Std.cs`/`GeneratedSignals.cs`。`testbind/Program.cs` 从 172 行变成 **一个纯 `switch`**(去掉 `using QtSharp.QtWidgets;`、`using var app = new QApplication(...)`、`metasig`/`vdispatch`/`own`、`windowflags` 与那几行 legacy usage);`testbind.csproj` 去掉 `CppSharp.Runtime` 引用与 `CopyLegacyNativeDeps` target,只剩一个 `<Import Project="..\QtSharp.Api.props" />`。
  - **Step B(legacy native)✅**:`git rm` 了 `build_glue.bat`、`dump_exports.bat`、`native/QtSharp.def`(唯一被 git 跟踪的生成物)、`native/QtSharp_inline.cpp`、`native/QtSharp_{meta,vdispatch,ownership}.cpp`;删掉 gitignored 的 `native/QtSharp_signals.cpp`;清掉 `out/native/` 与 `out/qt_min/`。`native/` 现在只有 `QtSharpApiRuntime.{h,cpp}` + `CrashTracer.cpp`。
  - **Step C(CppSharp)✅**:`git rm -r` 了 `CppSharp/`(253 个跟踪文件)与 `tools/rebuild-generator/`(跟踪文件),并删掉 `lib/CppSharp*.dll`(8 个)、`lib/native/CppSharp.CppParser.dll`(78MB)、`lib/native/Std-symbols.dll`;工作区里 CppSharp 的未跟踪目录(bin/.vs/Backup 等)也一并 `Remove-Item`。**⚠️ `lib/lib/clang/19` 原样保留**(`ApiModel2.ResourceDir()` 的 builtin headers,`gen.csproj` 还要拷到输出;libClangSharp 的 nupkg 不带)—— `.gitignore` 的注释已改成"不要删"。
  - **Step D(脚本/解决方案)✅**:`tools/regen-api.ps1` 去掉 `-Extractor1`/`$extractor`,硬编码 `extract2`;`.gitignore` 删掉全部 CppSharp 子目录忽略与 legacy 生成物条目,现在只剩 `out/` 与 `testbind/QtSharp.Api.cs`;`QtSharp.slnx` 不变(gen / testbind / samples\hello / tools\clangprobe 四个工程,`dotnet sln ... list` 校验过)。
  - **Step E(验收 + 记录)✅**:`powershell -File tools\regen-api.ps1` 全绿(model 逐字节一致、codegen 两次一致、22 个 demo + sample 全 PASS);顺带清掉 `gen/` 里两处指向已删命令的过时注释(`qtgen dumpopts`、`OpaquePointers` 的"legacy extractor"说法——该表还在,但作用只是**词汇表**)。
  - **保留(与新旧链路无关)**:`native/CrashTracer.cpp` + `build_crashtracer.bat`(崩溃 tracer,查㉗那个 flake 要用)、`tools/disasm.py`(capstone RVA 反汇编)、`tools/clangprobe/`。
- **另一件待办(#1,用户已排期)**:㉗ 里那个偶发 `0xC000041D` 退出崩溃(`api-windowflags` 3/10、`api-systray` 约 1/22)。两条待验证假设见㉗。**验证要几十次重复运行,每次会在桌面闪一个窗口**(本机没有 `qoffscreen.dll`),用户说等他下班机器空出来再跑。

**㉚ 本轮完成:painting 组第一批(`basicdrawing` / `gradients` / `painterpaths`,+84 项断言)**
一句话:**QPainter 这一整块(画笔/画刷/渐变/路径/抗锯齿/变换)第一次被官方示例压过**——三个 demo 全部把窗口画进 `QImage` 再**读像素**做断言,不靠人眼看。
- **`api-basicdrawing`(34 项)**:官方形状枚举的 13 个形状逐个渲染并统计墨迹(黑=笔、白=刷、其他=贴图/抗锯齿),`sizeHint`/`minimumSizeHint` 重写、笔宽/笔型(`NoPen` 一点不画、`DashLine` 比 `SolidLine` 少)、画刷(`NoBrush` 不填)、**抗锯齿真的产生混合边**(off=0 / on=86)、**translate/rotate/scale 组合改变渲染**、圆角矩形真的切掉角(角点像素 `0xFFC0C0C0` vs 直角 `0xFFFFFFFF`)、`drawText`/`drawPixmap`,以及控制窗口的 5 个 combo/2 个 checkbox 的范围与**信号→槽**联动(13 个形状项、6 笔型、3 端帽、3 连接、15 画刷)。
- **`api-gradients`(18 项)**:三种渐变的 `type()`、`start/finalStop`、`center/radius`、`angle`、**带焦点的 radial 保留焦点**;线性渐变真的从红到蓝、中点是绿;radial 中心黄、`PadSpread` 在半径外夹到边缘色、`RepeatSpread` 不同、`ReflectSpread` 与 Repeat 渲染出 23636 个不同像素;conical 0°/180° 射线红/蓝、**`SetAngle` 移动色标**;`QGradient::Preset` 经 `QPainter::FillRect` 出多色渐变;combo → 模式 → 重绘。
- **`api-painterpaths`(32 项)**:官方 `Window` 的**九个 `QPainterPath` 逐字复刻**(rect / roundRect 四个 `arcTo` / ellipse / pie / polygon / **两个子路径的 group** / **`addText` + `ForceOutline` 文字轮廓** / cubic bezier / **五角星**),每个都在自己的 RenderArea 里缩放+旋转+描边+**线性渐变填充**后统计墨迹;`BoundingRect()` 与官方坐标一致;`sizeHint`/`minimumSizeHint`;填充规则用 **`QPainterPath::Contains`** 验证(五角星中心被穿过两次 ⇒ `OddEvenFill` 不含、`WindingFill` 含);笔宽 0→9 墨迹增长;旋转 45° 后扫描线不同;渐变填充上红下蓝;控制窗口的九个区域、颜色 combo 由 **`QColor::ColorNames()`** 填充、官方默认色(`mediumslateblue`/`cornsilk`/`darkslateblue`)、笔宽 `0..20`、旋转 `0..359` + **`Wrapping`** + `°` 后缀,以及 spin box/slider→**九个区域一起**联动。
- **本轮为移植做的取舍(都写进了文件头注释)**:
  - `QPainterPath` **没有绑拷贝构造函数** ⇒ 官方 `RenderArea(const QPainterPath &path)` 的"复制"用 `new QPainterPath(); copy.AddPath(src)` 表达(几何与填充规则等价,验证方式是**先 Dispose 源路径再渲染**)。
  - 官方 `basicdrawing` 的 `pixmap` 是 `fill(Qt::transparent)` 且从未画过 ⇒ Pixmap 分支什么都看不见;移植版给它填了色并画了个椭圆,否则这条断言没有意义。`paintEvent` 开头另外填了一层已知浅灰背景,让"背景色"可命名。
  - `QPen(QColor, width, ...)` 与 `QPen(Qt::GlobalColor)` **都没绑**(只有 `QPen(QColor)` 与 `QPen(QBrush, double, style, cap, join)`)⇒ 用 `new QPen(new QBrush(color), width, ...)`。
  - `QComboBox::addItem(QString, QVariant)` 未绑(老问题)⇒ 颜色 combo 用 `AddItem(name)` + 表内 `Dictionary<QComboBox, List<QColor>>` 代替 `itemData`(`QVariant` 也没有 QColor 转换)。
  - `Qt.GlobalColor` 的成员是**小写原名**(`Qt.GlobalColor.black`/`white`/`lightGray`),不是 PascalCase。
  - RenderArea 存的是**颜色副本**(`new QColor(c.Red(), c.Green(), c.Blue(), c.Alpha())`),不是引用 —— 否则调用方 `using` 掉 QColor 后重绘会抛 `ObjectDisposedException`(踩到过)。
- 门禁:model 逐字节一致、codegen 两次一致、**25 个 demo + sample 全 PASS**(993 项断言)。

**㉛ 本轮完成:completer + custom sort/filter,并修掉一个真 bug(私有虚函数被 override 成 `return default`)**
一句话:**新开一个扩展点(`QSortFilterProxyModel`)以搬 `customsortfiltermodel`,顺带发现"私有虚函数被当成可重写点"这个从 ⑮ 就潜伏至今的真 bug**。
- **新 demo `api-completer`(29 项)**:官方 `tools/completer` —— 一个 line edit 背后的四种模型(裸 `QFileSystemModel`、**C# 子类化的 `QFileSystemModel`(重写 `data()` 显示全路径)**、两列 `QStandardItemModel`(国家+代码)、`QStringListModel` 单词表)、三种补全模式、大小写、`maxVisibleItems`、`wrapAround`、`setModelSorting`、自定义 `QTreeView` popup、`QColor`/`QMenuBar`,以及**前缀→`CompletionCount()`/`CurrentCompletion()`** 与 `activated(QString)` 信号的 Connect+Emit。
  - 官方资源(`:/resources/countries.txt`/`wordlist.txt`)**不存在**,改成代码里写死同样数据;**completer 故意不带 parent 并 `Dispose` 上一个**(官方是 `delete completer`;带 parent 的包装器 `Dispose` 是 no-op)。
  - 一个容易踩的移植点:**官方的 `changeModel()` 先给成员赋值再调 `changeMode/changeCase`**;我先调后赋值 → `QCompleter` 的 `caseSensitivity` 默认是 **`CaseSensitive`**(不是平台相关的 Insensitive),于是 `"GRA"` 匹配不到。按官方顺序修好。
- **新 demo `api-customsortfilter`(19 项)**:官方 `itemviews/customsortfiltermodel` —— **托管 `QSortFilterProxyModel` 子类**,Qt 自己回调 `filterAcceptsRow()`(21 次)与 `lessThan()`(21 次);正则/固定串/通配符三种语法(经 `QRegularExpression.Escape`/`WildcardToRegularExpression`)、大小写选项、**自定义日期范围**(`BeginFilterChange()`/`EndFilterChange(Directions.Rows)`)、`mapToSource` 往返、`filterKeyColumn`/`dynamicSortFilter` 往返。
  - 与官方的机械差异(写在文件头):`QVariant` **没有绑日期访问器**(`toDateTime()`/`typeName()` 都没有),所以日期列按 ISO 文本携带、区间比较用字符串比较;`SetFilterMinimumDate(QDate)` 的签名照旧。
- **`gen/` 的两处改动(本轮唯一动 gen 的地方)**:
  1. `ApiModel2.VirtualExtensionClasses` 加一行 `"QSortFilterProxyModel"` —— `filterAcceptsRow`/`filterAcceptsColumn`/`lessThan` 成为可重写点。**副作用(名字闭包的自然结果)**:该类**重声明**的 `mapFromSource`/`mapToSource`/`mapSelectionFromSource`/`mapSelectionToSource`/`setSourceModel` 也一起变成 virtual(仍可调用,只是多了一个可重写点),`QAbstractProxyModel` 因此拿到 `subclassCtor`。
  2. **★ 真 bug:私有虚函数被 override 成 `return default`**。`QAbstractListModel` 把 `columnCount`/`hasChildren`/`parent` 声明在 **`private:`**(NVI),⑮ 给它们加了 `noBase: true`(不生成 `CallBase_*`、托管默认实现"什么都不做")——但**native 包装器照样 override 了它们**,于是 `QAbstractListModel::columnCount` 返回 **0** 而不是 1。而 `QAbstractItemModel::index()` 走 `hasIndex() → columnCount()`,所以**每个 `QStringListModel` 的 `QModelIndex` 都是无效的、`data()` 全空**(completer demo 当场抓到:`index(0,0).isValid()==false`、`data()==''`、`CompletionCount()==0`)。
     - 修法(`ApiCodeGen.cs`):`VirtualSlot` 新增 `Blocked`(= 该槽最近实现的 `noBase`)。**槽仍留在链里**(必须在链里 —— 否则链合并会往上走,挑中更远的 `QAbstractItemModel::columnCount` 那个 public pure 声明),但**不再生成 C# virtual、不再生成 native override、不再生成 dispatch case**;槽号仍是链中位置,三个 emitter 的编号保持一致。Qt 自己的私有实现于是照常运行。
     - model 侧不动(`noBase` 条目保留),所以**这 5 条 `noBase` 在 model 里是"记录",语义是"别碰它"**,而不是"用空实现顶掉它"。
     - 已知残留:`QStringListModel.ColumnCount()` 从 C# 调仍得到 0(解析到 `QAbstractItemModel` 的 pure stub)。C++ 侧没有公开入口,只能如此;Qt 自己的内部逻辑现在是对的。**要读列数就调 `Model().ColumnCount()`(基类型)或直接读 model 的行/列数**。
- 门禁:model 逐字节一致(446 类 / 451 枚举)、codegen 两次一致、native+C# 编译、**27 个 demo + sample 全 PASS**(1041 项断言)。

**㉜ 本轮完成:`stardelegate`(委托的"编辑"半边),并确认一个**待修的能力缺口**(未绑定模型够不着)**
- **新 demo `api-stardelegate`(17 项)**:官方 `itemviews/stardelegate` —— 托管 `QStyledItemDelegate` 画星级评分,并**用托管 `QWidget` 子类(`ApiStarEditor`)当编辑器**:`CreateEditor`/`SetEditorData`/`SetModelData`/`UpdateEditorGeometry` 四个钩子全被 Qt 调用,`commitData`/`closeEditor` 两个信号连接+`Emit`;`ApiStarRating` 是纯 C# 的值类型(`QPolygonF` 星形/菱形 + `QPainter` + `QPalette.Highlight()/WindowText()`),`starAtPosition` 的边界、编辑器离屏渲染的墨迹都做了断言。
  - **机械差异**:官方把 C++ `StarRating` 塞进 `QVariant`(`fromValue<StarRating>`),绑定表达不了 ⇒ 评分以 `int` 存在 item 的 display data 里,委托再解析回来。`QMouseEvent` 没有可构造的构造函数 ⇒ 释放走 `ReleaseEditing()` 接缝(与 `ShapedClockApi` 的 `DragTo` 同一手法)。
- **★ 顺带确认的能力缺口(值得接下来修)**:`view.Model()` 在 **QTableWidget / QTreeWidget / QListWidget / QComboBox** 这类"内部模型是 Qt 私有 C++ 类"的控件上**够不着模型** —— `QAbstractItemModel::index/data/setData/rowCount` 都是 **pure virtual**,而生成的 C# 基方法对 pure virtual 一律 `return default`(null / false),于是 `table.Model().Index(0,0)` 返回 **null**、`RowCount()` 返回 0、`SetData()` 返回 false。**表现是"静默不生效"**(本 demo 编辑回合会写不回去),不是崩溃。
  - 现状:`api-stardelegate` 因此改用 **`QTableView` + `QStandardItemModel`**(已绑的模型类,`Index/Data/SetData` 都走 `__base` 到 C++ 实现),整条委托生命周期是真的;文件头写明了与官方的差异。**已有 demo 没有依赖这个行为**(它们都用已绑模型类),所以门禁不受影响。
  - **推荐的修法**:给生成的 root 类加一个 `__managedSubclass` 标志,不是我们造的对象走 vtable。**已在"㉝"实施**,而且范围比这里写的大(不只 pure virtual —— `setData` 那种"基类实现是个 stub"的非 pure 虚函数同样中招);回归就是**把本 demo 换回 `QTableWidget`**(㉝ 已换回,编辑能写回)。
- 门禁:model 逐字节一致、codegen 两次一致、**28 个 demo + sample 全 PASS**(1058 项断言)。

**㉝ 本轮完成:`__managedSubclass`(修掉"采纳的 C++ 对象够不着自己的 override")+ `composition`**
- **★ 兑现 ㉜ 记下的那条缺口,并且发现它比 ㉜ 描述的更广**。原判断是"pure virtual 的 C# 基方法一律 `return default`,所以 `view.Model()` 够不着私有模型"。真动手才发现两件事:
  1. **不只是 pure virtual**:`QAbstractItemModel::setData` **不是** pure(基类实现就是 `return false;`),而生成的 stub 走的是 `__base`(**限定的非虚调用**)——所以对一个**采纳来的**对象调 `view.Model().SetData(...)` 永远走基类那份"返回 false",写不进去。只要"对象的实际实现比声明它的类更靠下",限定的基类调用就是错的。**正确规则是:不是我们造的包装器,一律走 vtable(`self->method(...)`);只有我们自己造的(装了 dispatch 钩子)才需要 `__base` 链或 `return default`** ——否则 native override 会回派到同一个方法。
  2. **`__managedSubclass` 必须在两条构造路径都置位**:普通 ctor 和**抽象类的 `subclassCtor`**。漏掉后者 ⇒ 托管子类(`QAbstractTableModel` 等)被当成"采纳对象" ⇒ `Index()` 走 `_vt` → native override → dispatch → `Index()` … **栈溢出**(`api-custommodel` 当场 0xC00000FD;`api-widgets`/`api-ownership` 也是同一个根因,表现为 0xC000041D / 0xC0000374)。
- **落地(`gen/ApiCodeGen.cs`)**:
  - 生成的 root 类新增 `internal bool __managedSubclass;`,**两条构造路径**都置 true(`FromHandle`/`FromTransientHandle`/`FromOwnedHandle` 造出来的包装器保持 false)。
  - 每个已发布的非私有虚函数,C# stub 变成:
    ```csharp
    public virtual X M(...) {
        if (!__managedSubclass) { /* 走 vtable:ApiNative.X_M_vt(...) */ }
        else { /* pure → return default;否则 __base 链 */ }
    }
    ```
    两半各自在 `{ }` 里,避免容器的 marshal 临时变量重名(踩到过 CS0136)。
  - 新增 `qs_api_X_M_vt` 转发器(**连 pure 一起**,不再只给 pure;返回/参数走与 `__base` 相同的扁平 C ABI 转换)。
  - **protected/private 的虚函数没法从自由函数调**(`QAbstractButton::paintEvent`、`QGraphicsLayoutItem::sizeHint`):生成一个**无 override 的小派生类** `QsApiAccess_X : X`,里面 `static Call_M(X* self, ...)` 用 `static_cast<QsApiAccess_X*>(self)->M(...)`(**虚调用**,落到对象真正的实现上);`_vt` 导出只是转发它。这与 `CallBase_*` 依赖的"声明基类在偏移 0、转换是恒等"是同一个既有前提。
  - 于是 `_vt`/`__base` 两份 DllImport 都发,且都**只在该类可子类化时发**(否则 `QsApiAccess_X` 不存在);不可子类化的类退回 `return default`。
- **验证**:`api-stardelegate` **改回官方的 `QTableWidget`**(㉜ 里为了绕过缺口才改成 QTableView),新增断言 `table.Model().Index(2,3)` 有效、`RowCount()==4`、`[2,3]=='3'`,并且**编辑回合真的写回**(`setModelData()` 后 row 1 的 rating 从 4 变 2)。
- **新 demo `api-composition`(16 项)**:官方 `painting/composition` —— 24 种 `QPainter::CompositionMode`、基缓冲 + 源圆的两次 blit(`memcpy` 改成 `Copy()`),**从带 alpha 的缓冲里读像素**:`Clear` 让圆的像素变透明、`Destination` 忽略源(与基缓冲逐字节相同)、8 种模式给出 8 个不同签名、alpha=0 与基缓冲相同、hue 改变圆的颜色;窗口侧 24 项 combo + hue/alpha 两个 slider 联动。官方的 `flower.jpg` 资源不存在 ⇒ 程序化生成一张彩色"照片"(写在文件头)。
- 门禁:model 逐字节一致、codegen 两次一致、**29 个 demo + sample 全 PASS**(1074 项断言)。

### 解析配置清单(历史上 CppSharp 传给 clang 的东西 —— 迁移时的等价基准;`qtgen dumpopts` 已随旧链路删除)
来源曾是 `gen/ApiModel.cs` 的 `ModelLibrary.Setup` + `QtLibrary.QtIncludeRoot` + `CppSharp.Parser.ParserOptions.SetupMSVC`;`ApiModel2.cs` 复刻的就是这套配置。
```
ClangVersion = 19.1.7                       # CppSharp 自带 clang;新前端用 libClangSharp 21 亦可
TargetTriple = ''                           # 空 ⇒ clang 宿主默认 = x86_64-pc-windows-msvc
LanguageVersion = CPP17, SkipFunctionBodies = True
MicrosoftMode = True, ToolSetToUse = 193000000   # ⇒ -fms-compatibility-version=19.30
Arguments   = -xc++ -std=c++17 -fno-rtti
              -fms-extensions -fms-compatibility -fdelayed-template-parsing
              -D_ALLOW_COMPILER_AND_STL_VERSION_MISMATCH
IncludeDirs = <include> <include>\QtCore <include>\QtGui <include>\QtWidgets
Defines     = QT_NO_DEBUG QT_CORE_LIB QT_GUI_LIB QT_WIDGETS_LIB
SystemIncludeDirs = (空 —— MSVC STL/Windows SDK 由 clang 自己探测)
BuiltinsDir = <assemblies>\lib\clang\19\include
```
被解析的头文件 = `Roots` 表里的 `(Module, Header)` 去重后的集合;解析成**一条 TU**(probe 是:第一个头当源文件、其余用 `-include` 预包含),**一次解析即确定**。

### 新前端要产出的"事实契约"(就是现在 ApiModel 从 CppSharp 读的那些)
- **类**:原始名、是完整定义还是前向声明(必须能区分!前向声明不能覆盖真实类)、`isAbstract`、**所有**基类(按声明顺序,基类是否是"绑定类"由 `Roots` 名字集合判定 —— 用名字判定而不是 AST 对象身份)
- **构造**:public、非拷贝、非 deleted;参数类型 + 默认值
- **方法**:public、非 virtual、非 operator、非析构、非构造;`static` 标志;参数列表
- **虚函数**:在 `VirtualAllow` 白名单里的虚函数(只要**本类声明的**;链由 codegen 合并)
- **枚举**:限定名(`Qt::X` / `QColor::Spec`)、项+值、`QFlags` typedef(要用 typedef 名,并带底层枚举的 items)
- **信号**:**哪些声明是信号**由正则扫头文件决定(`Program.ScanSignals`;`Q_SIGNALS` 在 clang 里只剩 `public`,没有可识别痕迹),**参数类型从 AST 取**(`ApiModel2.SignalFactsOf`,复用 `FnFact`/`ParamFact`),见"㉒"
- **类型词汇表/规范化**:保持现在 `Normalize`/`Supported` 的语义(标量、`QString`、`int*` 只收 const、`Foo*`、按值 `Foo` → boxed、`X::Y` 枚举、`QFlags` typedef);新前端的优势是可以**直接用 clang 的类型拼写字符串**,不用再玩 `Desugar()`/身份比较

### 新前端**不该**继承的东西(这些正是脆弱源)
`Driver`/`Module`/passes/`IsGenerated`、`Type.Desugar()`、`CompleteDeclaration`、跨 TU 身份、`DeclConverter`、以及"按头文件分块 + 每块两次解析一致"这套自校验(新前端的目标是**一次解析即确定**,不需要它)。

### 迁移路径(5 步,风险最小)—— **5 步全部完成**(见"㉘")
1. 保留 `model/*.json` 作为接口;写 `extract2`(ClangSharp),**先只对当前 44 个类**产出同样的 model;✅
2. 用现成的 **model diff 门禁**对拍 `extract` vs `extract2` —— 一致即复刻成功;✅(对拍结果:原 44 类 model 哈希相同 `CA7567…`)
3. 按 `Roots` 逐类迁(自然会暴露漏掉的类型规则),直到全覆盖;✅(44 → 446 类)
4. 切默认前端;删掉分块/两次校验那套机器;把 extract 时间打进 CI;✅(`gates`:`extract2` ~2.5 秒、单 TU 一次解析)
5. 最后才动 vendored CppSharp。✅(㉘:14 个 legacy demo 全部搬完 → 整个旧链路连同 `QtSharp.def`/`build_glue.bat`/CppSharp 源码一起删了)

### 诚实的风险
CppSharp 替我们解决了一个不小的坑:**Qt 头的 MSVC 兼容解析**(`-fms-compatibility`、`__cplusplus`、`_MSC_VER` 等)和 libclang 分发。换前端必须精确复刻这套配置(约 20 行,但要**验证**)。另外**模板/默认参数的提取会比 CppSharp 的"结构化 AST"更手工** —— 这正是路线 B 作为备选的原因(自己吐 JSON 可以只吐我们要的那几个事实)。

## 下一 agent 的第一件事:四条轴的落地路线(2026-09-17,勘察已完成)
> 用户已决定**四条轴全做**。已经用 4 个并行子 agent 做完**只读勘察**,下面是可以直接照做的方案摘要;完整数字见各自的勘察结论(本节的数字都是实测)。
> **进度:四条轴(A1/A2/B/C/D)+ QtCore batch 1/2/3 + 输入事件整族 + 富文本整族 + 托管树模型(见"⑬…㉑")。剩下的低收益项:`QDebug`(入口全是自由函数/`operator<<`)、`QLatin1String`(9,需要 Latin-1 转换)、`QFont::Tag`(8,嵌套类)、`QList<std::pair<...>>`(6,pair 元素类别)、`QMetaObject::invokeMethod`(`QGenericArgument`,11)与 `QMetaObject::Connection`(5)。**
> **按收益排序的下一步(㉒ 之后的建议)**:①~~信号的"收与发"~~ **✅ 已完成(见"㉒",信号 266→428、可 `Emit`、C# 可自声明 Signal/Property)**;②**继续批量入库**剩余 QtCore/QtGui 类(`QAbstractAnimation` 家族/`QTimeLine`、`QPageSize`/`QPageLayout`/`QPdfWriter`(真 PDF 输出!)、`QIconEngine`/`QPaintEngine`、`QSessionManager`、`QAccessible*`、`QOffscreenSurface`/`QOpenGLContext`);③**剩余 demo 搬迁**(Menus / Basic Layouts / Sliders / WindowFlags / Scribble / Calendar / SystemTray —— 依赖已基本齐);④`QDrag` + 拖放 demo(`QMimeData`/`QDropEvent` 已绑)。
> **注意:剩下的 2/3/4 都要改 `gen/ApiCodeGen.cs`,必须串行落地,不要并发改同一批文件。**

**A. 所有权 —— ✅ A1+A2 全部完成(见"⑭/⑰")。下面是勘察记录:**
**A1 + A2 均已完成**(A1 见"⑭",A2 见"⑰")。下面是最初的勘察记录:
`DerivesFromQObject` 按 chunk 判断是**真 bug**:`QMainWindow`/`QCheckBox`/`QPushButton` 现在是 `owned:true`,而它们被 reparent 后归 Qt → **finalizer double free**(即文档里的 QCommandLinkButton 崩溃)。修法(按风险从低到高的顺序):
1. ~~**最便宜的正确修复**:生成的 `qs_api_<X>_dtor` 里对 QObject 派生类加 `if (obj->parent()) return;`~~ **已做(见"⑭")**。
2. 完整方案(见 `native/QtSharp_ownership.cpp` 的 PoC #3):`ApiRuntime` 把 `Dictionary<IntPtr, List<WeakReference>>` 换成 entry(`Destroyed/ManagedOwned/IsQObject/Owner`);native 加**一个** `QObject::destroyed` 槽(用一个静态回调用函数指针,`userData` 直接放对象地址,**不要 per-object GCHandle/delegate**,PoC 里那样会泄漏);`Handle` getter 在已销毁时抛 `ObjectDisposedException`(生成的每个方法都走 `Handle`,所以只改一处就全局生效);`AssociateOwner(owner, item)` 覆盖**非 QObject** 的 item 类(`addItem/addRect/setLayout/...` 白名单,`scene.Dispose()` 后 item 访问抛异常)。
3. `owned:false` 的语义就是"永不 delete + destroyed 后失效",这也是 item 类不再泄漏的正解。

**B. 虚函数白名单 —— ✅ 已完成(见"⑮")。如果之后要「全部虚函数可重写」(不止 model/delegate 子树):**
`PublishAsVirtual` 只需把 `IsInExtensionTree(className)` 去掉即可(代价:native override 槽 3308 → ~6800,+45% native / +80% C#,MSVC 单 TU `/O2` 未验证,兜底是 `/bigobj` 或拆 TU)。**下面是最初的勘察记录(仍有参考价值):**
实测:绑定类**自己声明**的虚函数 2102 个(去掉 moc 脚手架 438 → **1664**),白名单外 **1026 个 / 149 个类**;今天的 override 槽 **2877** 个,native 2.5MB/71k 行。**全开 ≈6828 槽(+3951),native +45%、C# +80%**(MSVC 单 TU `/O2` 可行,兜底是 `/bigobj` 或拆 TU)。**必须用"名字闭包"而不是"按类开"**:只开基类的名字而不开所有 overrider,`QStandardItemModel::data` 会退回**纯虚**实现 → 托管默认实现 nothing → 静默返回空数据。落地顺序(每步都要门禁绿):
1. 先补 dormant 设施(**不改 model**):`DispatchStore` 加 `QString`/`QStringList`/`QList`/`QMap`/裸 `IntPtr` 分支;`DispatchRead` 的 **boxed 参数改成 clone**(现在用 `FromHandle` 会进身份缓存,栈地址复用 → 拿到上一个参数的包装;`owned` 类型还会被 finalizer **delete 掉调用方拥有的对象**!)并给容器加 `ReadTransient`(`Read` 会 **free 掉 Qt 自己的容器**);`DispatchArgStore` 对数组/`uchar*` 传 `name` 而不是 `&name`;`CppVirtualParamType` 保留 pointee `const` 并支持 `const QList<T>&`。
2. **`qs_api_assign_<T>` 现在会解引用空源指针**(`*dst = *src`):`QAbstractItemModel::data` 返回 `QVariant`,托管不重写时 `return default` → 传 0 → **崩**。必须先加 null 判断。
3. `VirtualBoxedReturns` 目前只发 `QSize/QSizeF`,新增的 boxed 返回(`QVariant/QModelIndex/QItemSelection/QPixmap/QRegion/QStyleOption*`)要一起生成,否则 DLL 链接失败。
4. 然后 model 侧把 `VirtualAllow.Contains(name)` 换成一个 `PublishAsVirtual(fn)` 谓词(保留 moc 脚手架 denylist),按 `QAbstractItemModel → 委托类 → 视图类 → 全开` 分四步 `-Adopt`。
5. 验证要新增 `testbind/CustomModelApi.cs`(`testapp api-custommodel`):托管 `QAbstractTableModel` + `QStyledItemDelegate` 子类,断言 `RowCount`/`Data`/`Paint` 真的被 Qt 回调、`view.Grab()` 后 `PaintCount > 0`、boxed 参数是 clone 不是缓存别名、`GC.Collect()` 后仍可用。

**C. 非 const `QString&` —— ✅ 已完成(见"⑯")。下面是勘察记录:**
全部是 **in-out**(Qt 的实现既读又写)→ C# `ref string`。难点是一个 model 参数要变成**两个 native 参数**(`const char16_t* in` + `void** out`)且 native 侧要**语句体**(局部 `QString` → 调用 → `*out = new QString(local)`)。做法:`MarshalParams` 增加 `Postlude`(调用后、return 前执行,`EmitGuarded` 的 finally 之外)、`NativeRetExpr` 旁边加 `NativeBody(m, call)` 返回语句列表(`QString qs_in_x = ...; auto qs_result = call; *x_out = new QString(qs_in_x); return ...;`)、`NativeParamTypeFor`/`CppParamTypeFor` 改成 `...ParamsFor(p)` 返回 `List<string>` 并改 6 个调用点(3 个 DllImport 声明、3 个 native 前向声明)。顺带:标量 `ref`(如 `int &pos`)的生成路径已经写好了但目前 model 里 0 个,`QValidator::validate` 会是第一个真正跑它的地方。

**D. Q_PROPERTY → C# 属性 —— ✅ 已完成(见"⑯")。下面是勘察记录:**
扫描器照抄信号那条路(`gen/Program.cs` 的 `SignalInfo`/`ScanSignals`,`^class` 跟踪 + 括号平衡取多行 `Q_PROPERTY(type name READ getter WRITE setter NOTIFY sig ...)`,`extract2` 里与信号一起 scan/filter/排序,model 新增 `properties` 数组,**放在 `signals` 之前**以免破坏 `WriteSignals` 的"最后一个 key"约定)。C# 侧**只做语法糖**:`public string Text { get => text(); set => SetText(value); }` —— 调用已经生成的访问器方法,**不重复生成 native glue**。名字冲突复用现有的 `reservedByClass`/`MemberName` 机制(把属性名加进保留集,于是 `QLineEdit.text()` 保持小写,属性叫 `Text`);被保留集/类型词汇表挡住的属性(如 `echoMode` vs `EchoMode` 枚举、`QAction.icon` 的 `QIcon`)直接**跳过属性、保留方法**。NOTIFY 暂不生成 C# `event`(信号层还没有 disconnect,`+=` 会泄漏)。

## 早期路线图(历史记录,保留)
**目标已升级为"做到 PyQt/PySide 同等能力"**(见"⑫"的量化表)。类型词汇表这一轴**已经做完**(`QList`/`QStringList`/`QMap`/`QHash`/`QBindable`/`QByteArrayView`/out 参数全在,见"⑧/⑩/⑪"),剩下的类型只值 5–10 个方法。**下一步按收益排序**:

1. **QtCore 类批量入库** —— 收益最大且是纯体力活:**batch 1 已做**(22 个值类,见"⑬")。batch 2 是 **QObject 派生的那批**,必须等上面的所有权修复:`QFileDevice`/`QFile`/`QSaveFile`/`QTemporaryFile`/`QProcess`/`QThread`/`QLibrary`/`QPluginLoader`/`QMutex`(要连 `QBasicMutex` 一起,否则 `lock()` 在未绑定的基类上消失)/`QReadWriteLock`(+`QBasicReadWriteLock`)/`QSemaphore`/`QWaitCondition`/`QThreadPool`(要 `QRunnable`)/`QGuiApplication`/`QWindow`/`QDrag`/`QDebug`(入口全是自由函数/`operator<<`,收益低)/`QXmlStreamReader`/`QXmlStreamWriter`/`QTextDocumentWriter`/`QAbstractTextDocumentLayout`(抽象)/`QLockFile`。已知会新暴露的词汇表缺口:**`std::chrono::milliseconds/nanoseconds`(≈9→ 建议映射成 C# `TimeSpan`)**、函数指针 typedef(`QFunctionPointer`/`EasingFunction`,6)、`QRunnable`(4)、`QRawFont`(2)、`QStaticPlugin`(1)。注意 PySide 也不绑的模板/内部类(`QAtomicInt`/`QFlags`/`QIterable`/`QForeachContainer`/`*Private`/`QExplicitlySharedDataPointer`/`QMutexLocker`/`QFuture<T>`)直接跳过。**一批 20–30 个类跑一次门禁**。
2. **解开虚函数白名单**(自定义 model/delegate 的关键):`VirtualAllow` 从"31 个名字"改成"全部虚函数可重写"。需要先在 `Dispatch` 侧补齐:容器/boxed **参数**用瞬态包装(`FromTransientHandle`,现在 `DispatchRead` 用 `FromHandle` 会进身份缓存,对短命参数不安全)、`QList`/`QMap` 返回值的写回(现在 `DispatchStore` 只认标量/枚举/boxed/指针/字符串)、以及 native wrapper 体积膨胀的代价(现在 638 个槽,全开可能 3–5k)。
3. **非 const `QString&`**(11 条:`QValidator::validate/fixup`、`QClipboard::text`)→ C# `ref string`/`out string`。难点:`NativeRetExpr` 假设"单个表达式",而 in-out 字符串需要 native 侧**语句体**(局部 `QString` + `*out = new QString(local)`)。做法:给 codegen 引入"native 函数体 = 语句列表"(现在只有 `return <expr>;`),同时 `NativeParamTypeFor`/`CppParamTypeFor` 要求 1:1 参数映射 → 改成"一个 model 参数 → 1..n 个 native 参数"。
4. **Q_PROPERTY → C# 属性**、**所有权服务(item 类泄漏)**、**`QMetaObject::invokeMethod`/`Connection`**——见"⑫"表。

覆盖率报告在 `extract2` 结尾会打印,`QTSHARP_MODEL_DEBUG=1` 会把被挡的**完整拼写**打出来(报告里按容器名归并),并且会打印 `skipped <方法名>(<参数类型>) blocked by <类型>` —— 加类型前先用它看清"这一改能解锁哪几个方法"。
做法:改 `ApiModel2` 的词汇表/`ParamModeOf` + `ApiTypes.Managed` + `ApiCodeGen` 的 `ManagedType`/`NativeParamTypeFor`/`CppParamTypeFor`/`CppArgFor`/`NativeRetCpp`(+ 需要临时对象的参数走 `MarshalParams`/`EmitGuarded`),每加一类就跑 `tools/regen-api.ps1` 看 `blocked by` 榜往下走。

## 其它可继续的方向
1. ~~`extract2` 已通过验收,可以**删掉 CppSharp 的 extract 路径**……但旧链路 15 个 demo 仍在用 CppSharp 的 pass/codegen。~~ ✅ **已完成**(㉘:整个旧链路删净)。
2. ~~把旧 demo 逐个搬进新链路。~~ ✅ **已完成**(14 个 legacy demo 全部搬成 `api-*`;`WindowFlagsApi` 停牌)。
3. ~~item 类的所有权(item 由视图/布局持有)→ 落地 PoC #3 的所有权服务。~~ ✅ **已完成**(见"⑰")。
4. ~~派生类虚函数用 `override`(见"已知瑕疵 4")。~~ ✅ **已完成**(见"⑮"阶段 3 与"㉑")。

## 关键构建知识
- **旧链路(CppSharp / 它的 pass 与 codegen / `build_glue.bat` / `rebuild-generator`)已整块删除**(见"㉘"),上面那些"改 `FunctionExtensions.cs`/`CSharpSources.cs` 要重建哪个 dll"的知识**只对历史版本有效**。现在只有一条链路:libclang(ClangSharp)→ model → codegen → 生成的 C# + C++ wrapper。
- 生成产物 `out/`、`testbind/QtSharp.Api.cs` 被 .gitignore,不入库。

## 构建/重新生成命令序列
### A. (已删除)旧链路
一条命令跑完全部(含两道门禁与冒烟):
```
powershell -File D:\QtSharp\tools\regen-api.ps1
```
内部顺序:`dotnet build gen` → `qtgen extract2 out\qtsharp.api.json.new`(**一条 TU,~2.5 秒**)→ 与 `model\qtsharp.api.json` 比对(**model diff 门禁**,不一致要显式 `-Adopt` 才采纳)→ `qtgen codegen <model> out\api`(跑两次,**两次一致门禁**)→ 拷贝 `out\api\QtSharp.Api.cs` 到 testbind → `build_api.bat`(→ `out\api\QtSharp.Api.dll`)→ `dotnet build testbind` → 4 个冒烟 demo(`api` / `api-analogclock` / `api-lineedits` / `api-widgets`)。
手动分段:
```
D:\QtSharp\gen\bin\Release\net10.0\win-x64\qtgen.exe extract2 D:\QtSharp\model\qtsharp.api.json
D:\QtSharp\gen\bin\Release\net10.0\win-x64\qtgen.exe codegen D:\QtSharp\model\qtsharp.api.json D:\QtSharp\out\api
cmd /c D:\QtSharp\build_api.bat
```
`extract2` 支持 `QTSHARP_ROOTS=<逗号分隔类名>` 只提取子集(排查用;注意类之间的继承关系要一起给)。

### C. 自测命令
```
testapp api             # QObject 冒烟(方法/信号/虚函数)
testapp api-analogclock # 完整 AnalogClock demo(真实事件循环 + 渲染 + 事件回调)
testapp api-lineedits   # 完整 LineEdits demo(布局 + 控件 + 校验器 + 信号)
testapp api-widgets     # QtWidgets 巡礼(菜单/工具栏/标签页/分割器/三个 item view/model-view-proxy/mapper/日历/undo)
testapp api-docviewer   # 29 个 demo 见 testbind/Program.cs 的 switch 与 tools/regen-api.ps1
```
`testapp` 不带参数只打印用法;`QTSHARP_DEMO_MS=8000` 可让 `api-analogclock` 在屏幕上多留一会儿(默认 1.5 秒)。

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
- **(已删除)** `CppSharp/` 里的 fork(`src/AST/FunctionExtensions.cs` 的 `realAbiOrder`、`src/AST/Type.cs` 的 USR 比较、`src/Generator/Passes/DelegatesPass.cs` 的 sret 返回 `void*`、`src/Generator/Generators/CSharp/CSharpSources.cs` 的 vtable hook + `return @return`、`SymbolsCodeGenerator.cs`、`GenerateSymbolsPass.cs`、`src/AST/CppSharp.AST.csproj`)与 `tools/rebuild-generator/` 已随旧链路删除;那些 ABI 修复现在由生成的 native wrapper 从根上规避(见"架构 PoC #2")。
- `gen/Program.cs`(`QtIncludeRoot` + 头文件扫描器 `ScanSignals`/`ScanProperties` + `extract2`/`codegen` 两个命令)
- `tools/clangprobe/`(**路线 A 的可行性验证工具**,不入产品链路):`ClangProbe.csproj`(ClangSharp 21.1.8.2 + libClangSharp.runtime.win-x64 21.1.8.2)、`Program.cs`(取事实的完整实现:`--class`/`--all`/`--raw`/`--tree`/`--dump`/`--index`)。**`extract2` 的逻辑从这里搬**。
- `testbind/Program.cs`(命令分发;不带参数只打印用法)
- `native/CrashTracer.cpp`、`build_crashtracer.bat`、`tools/disasm.py`(ABI 调试工具)
- **新链路(产品骨架)**:
  - `model/qtsharp.api.json`(**入库**,规范化 API model;**446 类 / 451 枚举 / 9523 方法 / 858 虚函数 / 476 字段 / 1004 属性 / 428 信号**,含构造/继承/可选参数/boxed 值类型/`ref`·`out` 参数/`cpp` 拼写)
  - `gen/ApiModel2.cs`(`extract2`,`ApiModel` 的 partial:一条 TU + libclang 索引 → model;类型词汇表/`QFlags`/枚举限定/默认参数切片/非 const 引用判定/信号参数从 AST 取都在这里)
  - `gen/ApiModel.cs`(模型形状 + `Roots` 表 = 类名+头文件+模块+是否绑 ctor+`owned` + 词汇表/`VirtualAllow`/`ExtraEnums`/`OpaquePointers` + 头文件扫描器 `ScanSignals`/`ScanProperties`/`WriteSignals`/`WriteEnum` + 序列化;`extract` 老命令已随 CppSharp 删除)
  - `gen/ApiCodeGen.cs`(`codegen`:model → C# + C++ wrapper + P/Invoke;纯函数。继承/次要基类视图/枚举/flags/ctor 重载/boxed 值类型/整链虚函数 `__base` 转发/静态方法/`int[]` 数组参数)
  - `native/QtSharpApiRuntime.{h,cpp}`(手写运行时:moc 信号转发器 + QString 桥 + 应用启动 `qs_api_app_*` + `qs_api_event_type` + 事件探针)
  - `build_api.bat`(→ `out/api/QtSharp.Api.dll`)、`tools/regen-api.ps1`(两道门禁 + 12 个 demo + 独立 sample;`-Adopt` 才采纳新 model)
  - **`QtSharp.Api.props`(用户侧唯一入口:一个 `<Import>` 就拿到生成的绑定 + 运行时 + native/Qt DLL 拷贝)**,配套 `samples/hello/`(一个真界面工程,门禁会 build+run)
  - `runtime/ApiRuntime.cs`(C# 侧运行时,随绑定发布,不在 testbind 里:`QSignal<T>`/`Wrap`(按 handle 存多视图)/信号解包/`SendUserEvent`/`SendMousePress`/`SendWheel`/`SendKeyPress`/**`IQtObjectView` + `destroyed` 注册表 + `Forget`/`Watch`/`AssociateOwner`**)
  - 29 个 demo(都在 `tools/regen-api.ps1` 门禁里,共 1074 项断言):`testbind/ApiSmoke.cs`(`testapp api`)、`AnalogClockApi.cs`(`api-analogclock`)、`LineEditsApi.cs`(`api-lineedits`)、`WidgetsApi.cs`(`api-widgets`)、`GraphicsApi.cs`(`api-graphics`)、`CoreApi.cs`(`api-core`)、`EventsApi.cs`(`api-events`)、`OwnershipApi.cs`(`api-ownership`)、`CustomModelApi.cs`(`api-custommodel`)、`CalculatorApi.cs`(`api-calculator`)、`SpinBoxesApi.cs`(`api-spinboxes`)、`ShortcutEditorApi.cs`(`api-shortcut`)、`MetaObjectApi.cs`(`api-metaobject`)、`ShapedClockApi.cs`(`api-shapedclock`)、`SlidersApi.cs`(`api-sliders`)、`CalendarWidgetApi.cs`(`api-calendarwidget`)、`MenusApi.cs`(`api-menus`)、`BasicLayoutsApi.cs`(`api-basiclayouts`)、`ScribbleApi.cs`(`api-scribble`)、`WidgetsGalleryApi.cs`(`api-gallery`)、`SystemTrayApi.cs`(`api-systray`)、`DocumentViewerApi.cs`(`api-docviewer`)、**`BasicDrawingApi.cs`(`api-basicdrawing`)/`GradientsApi.cs`(`api-gradients`)/`PainterPathsApi.cs`(`api-painterpaths`)**、**`CompleterApi.cs`(`api-completer`)/`CustomSortFilterApi.cs`(`api-customsortfilter`)**、**`StarDelegateApi.cs`(`api-stardelegate`)**、**`CompositionApi.cs`(`api-composition`)**;`WindowFlagsApi.cs` 存在但**停牌**(见"㉔/㉗")。
  - **三个 PoC(源码已删)**:`native/QtSharp_meta.cpp`/`QtSharp_vdispatch.cpp`/`QtSharp_ownership.cpp` 与对应的 `testbind/MetaSignal*.cs`/`VirtualDispatch.cs`/`Ownership.cs` 不在了;机制分别进了 `native/QtSharpApiRuntime.cpp`、`ApiCodeGen`、`ApiRuntime`。

## 提交状态
- 已提交(master,按时间顺序):
  1. `Generator: fix sret/vtable ABI, cross-TU type identity, QFlags returns, abstract ctors`
  2. `Port official QtWidgets examples and align them with upstream`(ShortcutEditor/DocumentViewer/QtCompat/AllDemos/WidgetsGallery)
  3. `Add native crash tracer and disassembly tooling for ABI debugging`
  4. `Expand bindings: item-view helpers, wizards, animation, undo, syntax highlighting, image IO`
  5. `Bind event types for context menus, drag and drop and QMimeData`
  6. `Port Menus and Basic Layouts examples from the official Qt sources`
  7. `PoC: generic moc-driven signal dispatcher (one forwarder for every signal)`
  8. `PoC: virtual dispatch into managed code via a compiled C++ wrapper (UnmanagedCallersOnly)`
  9. `PoC: ownership/lifetime model for Qt objects under the .NET GC`
  10. `Step 1: normalized API model + deterministic codegen pipeline`
  11. 各步的 CONTEXT.md 记录
- 生成产物 `out/`、`testbind/QtSharp.Api.cs` 被 .gitignore;`model/qtsharp.api.json` 被跟踪。(`testbind/QtSharp.{Core,Gui,Widgets}.cs`、`Std.cs`、`GeneratedSignals.cs`、`native/QtSharp_signals.cpp`、`native/QtSharp.def` 随旧链路一起消失了。)

### ✅ 已提交
六轮工作(⑬–⑱)落在 `3f8445e` 之后的 4 个 commit 里;⑲ 那一轮又落 3 个。每次提交前都跑过完整门禁:**model 逐字节可复现、codegen 两次一致、native+C# 编译、全部 demo 全 PASS**。`git diff` 的 hunk 在各轴之间互相交错(同一批函数被反复改),**按轴拆 commit 需要重建无法编译的中间态**,所以改成按区域拆:

**`3f8445e` → 4 个 commit(⑬–⑱)**
1. `Runtime: a Qt-destroyed object invalidates its wrapper, and an item dies with its owner` —— `testbind/ApiRuntime.cs` + `native/QtSharpApiRuntime.cpp`
2. `Expand the model to 352 classes: fields, Q_PROPERTY, out/ref parameters, containers, durations` —— `gen/*` + `model/qtsharp.api.json`
3. `Demos: QtCore tour, ownership and invalidation, and a managed model + delegate` —— `testbind/*` + `tools/regen-api.ps1` + `QtSharp.slnx`
4. 记录 CONTEXT.md

**⑲(本轮)3 个 commit**
5. `Bind the input events, QtCore batch 3 and the rich text types (445 classes)` —— `gen/*` + `model/qtsharp.api.json`
6. `Demos: a wheel turn, a key press and the rich text types reach C#` —— `native/QtSharpApiRuntime.cpp`(两个探针)+ `testbind/ApiRuntime.cs` + `testbind/CoreApi.cs` + `testbind/EventsApi.cs` + `Program.cs` + `tools/regen-api.ps1`
7. `Port the Calculator and Spin Boxes examples onto the new pipeline` —— `testbind/CalculatorApi.cs` + `testbind/SpinBoxesApi.cs` + `Program.cs` + `tools/regen-api.ps1`(+ `gen/ApiModel.cs` 删掉一个 legacy 里没人用的 `classCount`,消掉 build warning)
8. 记录 CONTEXT.md(本条)

**⑳(本轮)2 个 commit**
9. `Make the binding usable outside the test app: a props file and a sample project` —— `QtSharp.Api.props` + `samples/hello/` + `runtime/ApiRuntime.cs`(从 `testbind/` 移出)+ `testbind/testbind.csproj` + `QtSharp.slnx` + `tools/regen-api.ps1`(去掉拷贝步骤,顺手修好 `api-spinboxes` 的 `exit` 把 `api-custommodel` 挡掉的 bug)
10. `Let the Roots table decide who may be released, not a per-header guess` —— `gen/ApiModel.cs` + `gen/ApiModel2.cs` + `model/qtsharp.api.json` + `testbind/OwnershipApi.cs`

**注意**:`testbind/QtSharp.Api.cs`(生成物,被 gitignore)必须由 `tools/regen-api.ps1` 产出。

**㉑(本轮)3 个 commit**
11. `Publish the protected members a managed model needs, and stop hiding object.ToString` —— `gen/ApiModel2.cs` + `gen/ApiCodeGen.cs` + `model/qtsharp.api.json`
12. `Port the Shortcut Editor onto the new pipeline; drop the legacy one that crashed on a tree node` —— `testbind/ShortcutEditorApi.cs` + `testbind/Program.cs` + `testbind/AllDemos.cs` + `testbind/AnalogClockApi.cs`(鼠标事件改在 `Exec()` 前发)+ `testbind/CoreApi.cs`(剪贴板 SKIP)+ `tools/regen-api.ps1`
13. 记录 CONTEXT.md(本条)

**㉒–㉘(⑫–㉘ 各轮的后续)**:信号收发 + C# 自定义 Signal/Property、`new QApplication(args)`、各批 demo 迁移、`QSettings`、`gen/` 的 CppSharp 前端删除(`280aa0a`)、旧链路剩余的 5 步删除(本条)。

**㉙(本轮)**:`gen/` 之后那 5 步全部完成 —— **整个旧链路已删除**。改动集中在 `testbind/`(`Program.cs` 变成纯 switch、删 10 个 legacy 文件 + 5 个生成物、csproj 去掉 `CppSharp.Runtime` 与 `CopyLegacyNativeDeps`)、`native/`(删 6 个 legacy 文件)、`lib/`(删 CppSharp dll,保留 `lib/lib/clang/19`)、`.gitignore`、`tools/regen-api.ps1`(`-Extractor1` 移除)、`gen/ApiModel.cs`(清掉两处指向已删命令的过时注释),以及 `CppSharp/`(253 跟踪文件)与 `tools/rebuild-generator/`。

**㉚**:painting 组第一批 —— **不需要改 `gen/`,纯 `testbind/` 新增 3 个 demo + 门禁**。`BasicDrawingApi.cs`(34 项)/`GradientsApi.cs`(18 项)/`PainterPathsApi.cs`(32 项),`Program.cs` 与 `tools/regen-api.ps1` 各加 3 行;若干 API 缺口记在"㉚"的取舍清单里(最有价值的一条:`QPainterPath` 没有拷贝构造函数)。

**㉜**:`stardelegate` —— 纯 `testbind/` 新增 1 个 demo + 门禁(`StarDelegateApi.cs`,17 项);顺带确认"未绑定模型(`QTableWidget` 私有模型)够不着"这个能力缺口(见"㉜",含修法)。

**㉝(本轮)**:`gen/ApiCodeGen.cs` 的 **`__managedSubclass` + `_vt` 路由**(㉜ 那条缺口的修法,范围扩到**所有已发布虚函数**;踩到 `subclassCtor` 漏置位导致栈溢出)+ `QsApiAccess_X` 访问类;`StarDelegateApi.cs` 回归改回 `QTableWidget`(`+1` 项断言);新增 `CompositionApi.cs`(`api-composition`,16 项)+ `Program.cs`/`tools/regen-api.ps1` 各加 1 行。

### ✅ 最近 5 个 commit(旧链路删除 + 搬示例这一批)
| commit | 内容 |
|---|---|
| `fda5614` | `Delete the rest of the legacy pipeline` —— testbind/native/CppSharp/rebuild-generator/lib 全部清掉(`lib/lib/clang/19` 保留) |
| `8f31aca` | `Port the first painting examples` —— `api-basicdrawing`(34)/`api-gradients`(18)/`api-painterpaths`(32),全部读像素断言 |
| `aa64435` | `Port the completer and custom-sort/filter examples...` —— `api-completer`(29)/`api-customsortfilter`(19);`QSortFilterProxyModel` 进扩展类表;修"私有虚函数被 override 成 `return default`"(`VirtualSlot.Blocked`) |
| `b7ee921` | `Port the star delegate example, and pin down a real gap it exposed` —— `api-stardelegate`(17);记下"`view.Model()` 够不着私有 C++ 模型"的缺口 |
| `3cb8a53` | `Let an adopted C++ object reach its own override, and port the composition example` —— `__managedSubclass` + `_vt` + `QsApiAccess_X`;`api-stardelegate` 换回 `QTableWidget`;`api-composition`(16) |

**当前门禁**:**29 个 demo + sample、1074 项断言全 PASS**;model 逐字节一致(446 类 / 451 枚举)、codegen 两次一致。工作区干净。

**㉛(上一批)**:completer + custom sort/filter。`gen/` 两处改动 —— `VirtualExtensionClasses` 加 `QSortFilterProxyModel`(开扩展点),以及 `ApiCodeGen` 的 `VirtualSlot.Blocked`(**修掉"私有虚函数被 override 成 `return default`"** ⇒ `QStringListModel` 的索引/`data()` 恢复正常);`model/qtsharp.api.json` 随重抽更新(5 条 `noBase` 恢复 + `QSortFilterProxyModel` 的 virtuals);`testbind/CompleterApi.cs`(29 项)+ `CustomSortFilterApi.cs`(19 项)+ `Program.cs`/`tools/regen-api.ps1` 各加 2 行。

## 扩展示例覆盖(进行中)
目标:把 Qt 官方 **所有** examples/widgets 都改写成 C# 并跑通(不止 examples-widgets.html 那 16 个)。

### 已完成的官方示例(全部在新链路上,共 29 个 `api-*`)
widgets/lineedits、analogclock、calculator、spinboxes、sliders、shapedclock、calendarwidget、menus、basiclayouts、scribble、widgetsgallery(Widgets Gallery)、systray、shortcut editor、widgets(tour)、graphics view、documentviewer、**painting/basicdrawing、painting/gradients、painting/painterpaths**(㉚)、**tools/completer、itemviews/customsortfiltermodel**(㉛)、**itemviews/stardelegate**(㉜)、**painting/composition**(㉝)。另有 5 个不是示例的 demo(`api` 冒烟 / `api-core` QtCore 巡礼 / `api-events` / `api-ownership` / `api-metaobject`)。

### painting 组剩余(㉚/㉝ 已证明 **不需要新的绑定**)
`affine` / `deform` / `imagecomposition` / `pathstroke` / `transformations`:所需 API 全在(`QPainter` 含 `setCompositionMode`/`setOpacity`、`QPainterPathStroker`、`QTransform` 含 `QuadToQuad`/`Map`、三种渐变)。**唯一障碍是资源与示例私有类**:`affine`/`deform`/`imagecomposition` 依赖 `:/res/...jpg`(仓库里没有 qrc;`composition` 见㉝,已用程序化贴图绕开),`affine` 还依赖 `ArthurFrame`/`HoverPoints`/`ArthurStyle`;`pathstroke`/`transformations` 也用 `ArthurFrame`(照㉝ 的做法写一个 `ApiArthurFrame` 等价物即可,不需要新绑定)。

### 其它尚可移植的组(依赖已基本齐)
- **itemviews**(addressbook / simpletreemodel / editabletreemodel / spreadsheet):`QSortFilterProxyModel`(㉛ 起可子类化)/`QStandardItem`/`QStyledItemDelegate` 已绑。(`customsortfiltermodel` 见㉛、`stardelegate` 见㉜,均已搬完。)
- **richtext**(orderform / textobject / syntaxhighlighter):`QSyntaxHighlighter` 与 `QTextObjectInterface` 已入库(㉒/⑲)。
- **tools**(completer / regexp / settingseditor):`QCompleter`/`QRegularExpression`/`QSettings` 已绑。
- **animation/easing、graphicsview 整组、draganddrop、touch、gestures**:依赖类都已入库。
- **不可行**:RHI×2(需 QRhi + `.qsb`)、Window Embedding、plugandpaint(需原生插件)、quartz/other 平台示例、QtStateMachine(模块未装)、QtSvg/PrintSupport/QtPdf(运行时不带 DLL)。

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

## 架构 PoC #3:所有权/生命周期(已验证成功)
**动机**:这是唯一可能推翻架构的未知——.NET 的 GC 是**非确定**的,而 Qt 的所有权是 parent-child + 引用计数;最危险的失效模式是"Qt 还在用这个对象,托管 wrapper 已被终结并释放"。

**PoC**(`native/QtSharp_ownership.cpp` + `testbind/Ownership.cs`,`testapp own`)验证三条规则:
1. **Qt 拥有的对象**:托管侧只存 `IntPtr`(**不用 SafeHandle、不加终结器**),native 对象由 Qt 的 parent-child 持有;
2. **托管拥有**的对象:用 `SafeHandle`(**恰好释放一次**);
3. 两者都用 `QObject::destroyed` 回调把托管 wrapper 标记失效,之后访问**抛 `ObjectDisposedException` 而不是崩溃**(等价 PySide 对已删除 C++ 对象抛 `RuntimeError`)。

**验证结果(11 项全 PASS)**:删除父对象会连带销毁子对象且其 wrapper 失效、访问抛异常;`GC.Collect()+WaitForPendingFinalizers()` 后 Qt 拥有的对象**仍然存活**且能从 native 侧正常使用;托管拥有的对象 `Dispose` **恰好一次**、二次 `Dispose` 是 no-op(不 double free)、被遗弃对象的**终结器恰好释放一次**。

**产品注意点(已写进代码注释)**:`SafeHandle` 的 `ReleaseHandle` 在**终结器线程**执行,真实 QObject 应改为 `deleteLater()`/在所属线程删除,否则违反 Qt 的线程亲和。

### 三个 PoC 合起来的结论(产品骨架已全部验证)
| 层 | 机制 | 状态 |
|---|---|---|
| 信号 | moc 元数据 + 单一 `QSlotObjectBase` 转发器 | ✅ |
| 虚函数 | 生成的 C++ wrapper + 单一托管入口(`[UnmanagedCallersOnly]`) | ✅ |
| 所有权 | Qt-owned 弱引用 + managed-owned `SafeHandle` + `destroyed` 失效 | ✅ |
| ABI | 由 C++ 编译器负责(不手写 sret/thunk) | ✅(PoC #2 间接证明) |

也就是说:**"生成 C++ wrapper + 一个薄 .NET 运行时"这套架构的三根支柱都已实测可行**,剩下的都是工程量(模型提取器、codegen、模块覆盖、打包),而不是未知风险。

## 产品化第 1 步:规范化 model + 确定性 codegen(已跑通)
**目标**:让 codegen 不再是"读原始头文件 + 一堆后处理补丁",而是**某个入库产物的纯函数**,这样"生成器不稳定"从根上消失,并且能加 CI 门禁。

**产出**:
- `model/qtsharp.api.json`(**入库**,第一版只有 `QObject`,现为 **221 类**):字段 `formatVersion/qtVersion/namespace/classes[]`,每个类有 `subclassable/isAbstract/methods[]/virtuals[]/signals[]`;方法含 `name(原始 C++ 名)/returnType/parameters[]`。排序固定 → **字节可复现**。
- `gen/ApiModel2.cs`(**现在的默认前端**,`extract2` 模式,libclang/ClangSharp;见"extract2 上线"一节)。
- `gen/ApiModel.cs`(旧前端,`extract` 模式;保留作交叉验证,**下一次可以删**):从 clang/CppSharp AST 提取并**规范化**:
  - 用 `OriginalName`(C++ 名)而不是 CppSharp 改名后的 `Name`;
  - **不依赖 `IsGenerated`**(旧管线会按自己的理由裁掉方法),改用我们自己的类型白名单判定;
  - 处理**按值返回**(AST 里表现为隐藏的 `IndirectReturnType` 参数,真实类型在 `OriginalReturnType`);
  - 处理**引用**(`PointerType` + `LVReference` 修饰);
  - property 的 getter/setter 要从 `c.Properties` 取回(旧管线把 properties 全部 ignore);
  - 忽略 `QPrivateSignal` 参数;剔除 signal 名以免混进 method 列表;按 `(name,paramTypes)` 去重。
- `gen/ApiCodeGen.cs`(`codegen` 模式):model → **C# 绑定 + C++ wrapper + P/Invoke**,并转义 C# 关键字参数名(如 `event` → `@event`)。
- `native/QtSharpApiRuntime.{h,cpp}`(**手写运行时**,随绑定发布):通用 moc 信号转发器(PoC #1)+ QString 桥。
- `build_api.bat` → `out/api/QtSharp.Api.dll`;`tools/regen-api.ps1` = 提取 → **model diff 门禁** → codegen → **两次生成一致性门禁** → 编译原生 → 编译 C# → 跑 `testapp api`。
- 验证:`testapp api` 全 PASS(生成的访问器、生成的信号属性经 moc 派发投递、**native 调用生成的虚函数命中宿主重写**);15 个旧 demo 与三个 PoC 均无回归。

**意义**:这条链路就是产品骨架的缩影 —— "model(入库) → 纯函数 codegen → 生成的 C# + C++ wrapper → 薄运行时"。下一步是扩大 model 覆盖面(更多类/类型/模块)并让旧 demo 逐步切到新链路。

## 之后四轮:从 1 个类到 34 个类
### ① extract 拆成独立 parse-only 提取器(2 分钟 → 2 秒,后来因自校验变 ~190 秒)
`ApiModel.RunExtract` 取代旧的 `ExtractModelPath` 钩子:`ModelLibrary.Setup` → `driver.Setup()` → `ParseCode()` → `Extract()`。**不跑 generator / type maps / pass 链 / 代码生成 / 后处理**。`Roots` 表(类名+头文件+模块)只解析需要的头文件 —— 这才是提速的关键(全量 Qt 头解析 125 秒,只解析 root 头 ~2 秒)。信号扫描也改为按 `Roots` 逐头扫描(`Program.ScanSignals(moduleDir, header)`)。顺手统一了 Qt include 根常量。

### ② 用 AnalogClock 验证新链路(model v2 + codegen v2)
- **model v2**:`Roots` 表 + 类型词汇表(类名**封闭**、枚举**开放**)、`bases[]`(所有绑定基类)、`constructors[]`、方法级 `"static"`、参数级 `"optional"`/`"default"`、`"owned"`。类型形态:`int`/`QString`/`int*`(只收 const)/`Foo*`/**`Foo`(按值 → boxed 值类型)**/`Qt::PenStyle`。
- **重载折叠按 C# 映射后签名**(否则 `setObjectName(QString)` 与 `(QAnyStringView)` 会撞 `CS0111`);新增 `ApiTypes.Managed` 作为模型类型→托管类型的唯一映射。
- **codegen v2**:继承、**次要基类视图**(`AsQPaintDevice()`,因多继承子对象有偏移)、枚举(命名空间级进 `Qt` 静态类、类作用域嵌套)、ctor 重载(真实 C++ 参数类型 + C ABI 导出)、静态方法、`int[]` 数组参数、boxed 值类型的 `Dispose`/终结器(仅对我们拥有的类型)。
- **虚函数默认实现必须链回 C++ 基类**(`CallBase_*` 转发器,经派生静态成员绕过 protected)—— 否则 `QWidget::event()` 被空实现顶掉,Qt 自己的事件分发全废。
- **两个真 bug**:① `argv[i]` 约定错了(native 放的是参数的**地址**而不是**值**)→ 托管侧拿到栈地址 → `QWidget::event()` 认不出事件 → **paintEvent 永不触发**;② 多继承指针调整。
- 验证:真实事件循环跑 1.5 秒、5 次 paintEvent;截屏统计到 **991 个时针像素 + 467 个分针像素**,证明绘制真的生效。

### ③ 整链虚函数 + QFlags
- **祖先类的虚函数现在能被重写**:model 沿主基类链自底向上合并虚函数表(同签名只占一个槽、保留基类分配的 id,由派生类重声明时替换实现),每槽记录 `declaring`/`inherited`。native wrapper override **整条链**,`Dispatch` 逐个派发(继承来的名字要用**声明类的命名规则**查),`__base` 只给声明的槽生成。验证:重写 `QObject::customEvent` 收得到。
- **QFlags**:用 typedef 名(`Qt::WindowFlags`)发布为 `[Flags]` 枚举,但带底层枚举的 items;保留 `Qt::WindowType`(单值)与 `Qt::WindowFlags`(掩码)两个类型,合并会撞 `CS0111`;native 用 `fromInt`/`toInt`。验证:`0x40801` 往返。
- **只 override `VirtualAllow` 里的白名单名字**(当时 4 个)。

### ④ 覆盖率报告 + 几何值类型
`extract` 结束时打印**被哪种类型卡住的方法数**,于是"下一步补什么"从猜变成数据。榜首是几何值类型(`QRect/QPoint/QRectF/QPointF/QSize` ≈ 120 个方法),`Roots` 加 5 行即可(`QPoint/QPointF/QSize/QRect/QRectF`)—— **codegen 一行没改**(它们和 `QColor` 一样是"非 QObject 派生"→ 自动走 boxed 值类型路径)。类从 8 → 13,方法 ~350 → 616。

### ⑤ 事件层 + 再补一批类型 + ★ 提取可复现性修复(本轮)
**A. 事件层(新链路第一次能写"交互式"应用)**
- 14 个事件类入 model(`QEvent`、`QPointerEvent→QSinglePointEvent→QMouseEvent`、`QPaintEvent/QResizeEvent/QMoveEvent/QCloseEvent/QShowEvent/QHideEvent/QFocusEvent/QContextMenuEvent`),全部 `bindCtors:false, owned:false` —— 事件是 Qt 在栈上构造后交给我们的,**绝不能**被 GC 释放,也不提供构造。
- 虚函数白名单 4 → **27 个名字**(`mousePressEvent`/`resizeEvent`/`keyPressEvent`/`wheelEvent`/`closeEvent`/`changeEvent`/`dragEnterEvent`…);`QWidget` 现在有 **28 个可重写槽**。
- **`FromTransientHandle`**:虚拟参数用**不进身份缓存**的包装 —— 事件寿命极短且**栈地址会被下一个事件复用**,用缓存会返回上一个事件的包装。
- 运行时探针 `qs_api_probe_user_event`/`qs_api_probe_mouse_press` + `ApiRuntime.SendUserEvent/SendMousePress`。
- 验证:`MousePressEvent(QMouseEvent e)` 读到 `e.Position() == (42,24)`、`e.Button() == LeftButton`;`CustomEvent(QEvent e)` 里 `e.type() == QEvent.Type.User` ✓。

**B. 又加一批类型(13 → 34 类 / 1088 方法 / 1202 导出)**
`QBrush/QPen/QRegion/QTransform/QFont/QPainterPath/QImage/QPixmap/QAction` + 上面 14 个事件类。`QPainter` 57→**166** 方法,`QWidget` 190→**205**;生成 C# **11.9k 行** + native **6.9k 行**。
- 验证:离屏 `QImage(32×32, ARGB32)` + `QPen/QBrush/QPainterPath` 画椭圆,**回读像素是绿的** `rgb 0,255,0`;`QAction("Open").Text()` 往返。
- **真 bug ①**:`QBrush(const QColor&, Qt::BrushStyle = Qt::SolidPattern)` 的默认值被写成 C# `default`(=0),而 `Qt::BrushStyle` 的 0 是 **`NoBrush`** → 画出来是空的。现在 `DefaultLiteral` 把具名枚举量翻成对应 C# 枚举成员(`Qt.BrushStyle.SolidPattern`)。
- **真 bug ②**:中间位置的"可选但不可 marshal"参数被丢弃会**位置错位**(`QPixmap::load(QString, const char* = nullptr, Flags = …)` 把 flags 传进了 format 槽)。现在 `VisibleParams` 只保留**最长可 marshal 前缀**;不可 marshal 且无默认值的参数直接否决整条声明。

**C. ★ 核心修复:CppSharp 的 AST 转换在大型 parse 下不可复现**
- **症状**:37 类的 model **连续 5 次运行 5 个不同哈希**。表现是某些参数的类型变成毫不相干声明的类型(`QPainter::drawPoints(QPoint*, int)` 的第二参变成 `QUrl::ComponentFormattingOption`),每次坏的地方不同。
- **排查**:候选声明的分数/文件两次一致 → 类的原始 `c.Methods` 文本两次一致 → 直接 dump AST 就能看到两次不同 ⇒ **CppSharp 的 parse/转换本身不确定**;`GC.TryStartNoGCRegion(2GB)` 挂起 GC **无效**(不是终结器时序);按类二分:**`QInputEvent`/`QKeyEvent`/`QWaveEvent` 一进 parse 就永不可复现**,而 22 个"简单事件"类完全稳定。
- **做法**:`RunExtract` 按 `Root.Group` **分块**,每块**连解析两次**、两次序列化必须**逐字节相同**,不一致就**对半切开重试**,切到单类还不行就**报错退出**(宁可不产 model,也不产被污染的 model)。`QObject` 簇独自成组(`QObject/QTimer/QAction/QWidget`)—— **一个类绝不能和"声明了它继承的虚函数"的祖先分开**。`BoundBases`/`DerivesFromQObject` 改用**完整词汇表**判断基类(跨块的 `QImage : QPaintDevice` 照样发得出)。`Extract` 按**本块类名集合**过滤 —— 块里会看到别的块类的前向声明(qwindowdefs.h 前向声明 QWidget),那些只是类型名,**不能覆盖真实类**。
- **结果**:34 类 / 58 枚举,**两次独立完整运行产出逐字节相同的 model** ✓(~190 秒)。
- **取舍**:`QInputEvent/QKeyEvent/QWheelEvent` 因为会让 parse 不可复现,暂时放进 `OpaquePointers`(能重写 `keyPressEvent` 但拿不到 key code),理由写在代码注释里。
- **门禁加强**:`regen-api.ps1` 的 model diff 门禁**不再自动采纳**新 model,不一致就打印 diff 并 **exit 1**,要采纳必须显式 `-Adopt`(提取已自校验,不一致只可能是真实 API 变更)。

## 本轮完成:布局/控件/校验器 + LineEdits 跑在新链路 + 提取可复现性(真正的修复)★★★
### A. 第二个 demo:LineEdits(`testapp api-lineedits`)
`Roots` 加了 `QLayout/QGridLayout/QFrame/QLabel/QGroupBox/QComboBox/QLineEdit/QValidator/QIntValidator/QDoubleValidator`(10 个类 = 9 行,因为 `QFrame` 是 `QLabel`/`QGroupBox` 的基类,不绑定它这两个类在 C# 侧就不是 QWidget,不能交给 layout)。
`testbind/LineEditsApi.cs` 与官方 `examples/widgets/widgets/lineedits` 对齐(5 个 QGroupBox + QGridLayout + QLabel/QComboBox/QLineEdit);两处**被迫的差异**(写在文件头注释里):① combo box 用 `AddItem()` 循环填充,因为 `QStringList`(容器)还没建模;② 用 `currentIndexChanged` 而不是 `activated`(后者只对真实用户交互发射,测试造不出来)—— handler 本身与官方一致。
**9 项全 PASS**:combo 信号 → handler → `SetEchoMode` 读回 `Password`;`QIntValidator` 拒绝 `"abc"` 接受 `"123"`(`HasAcceptableInput()`);`Qt.Alignment` flags 到达(值 2 = AlignRight);输入掩码生效且 `HasAcceptableInput()`;只读开关;`textChanged` 经 moc 转发器投递;窗口渲染 4 次 paint。

### B. 抽象类不再生成构造函数
`QLayout`(纯虚 `itemAt`/`takeAt`)、`QValidator`(纯虚 `validate`)这类抽象类,生成的 native 子类**没有实现纯虚函数**,给它 ctor 只会在链接/运行期炸。现在 model 里 `c.IsAbstract` 为真就不发 ctor(仍然可以作为类型和基类使用,`QGridLayout : QLayout` ✓)。**已知限制**:托管侧因此**无法实例化抽象类的子类**(需要一个会实现纯虚函数的 native 子类,现在没有)。

### C. ★ 虚函数链从 AST 搬到 codegen(真正的可复现性修复)
上一轮把虚函数链(祖先槽位合并)放在**AST 遍历**里做,于是它要求"一个类必须和声明其虚函数的祖先在同一个 chunk"。这带来两个后果:①chunk 划分必须照顾祖先关系;②自适应切分(因为某块不可复现而切)会让**划分依赖运行结果** → 模型前后不一致(实测:同一份 44 类模型两次运行**哈希不同** ✗)。

**修法**:model 只存**本类声明的虚函数**(`virtuals` 只含 own),**链在 codegen 里按 `bases[0]` 递归合并**(`ChainOf()`,记忆化):同签名只占一个槽、保留基类分配的 id、由派生类重声明时替换,并记录 `declaring`/`inherited`。于是 chunk 之间**完全独立** → 划分可以固定:
- **按头文件分块**(`ActiveRoots.GroupBy(r => (r.Module, r.Header))`):同一头文件里的类本来就要一起解析,这是最自然的粒度,也**更快**(165 秒 vs 上一版 370 秒,因为不用反复试大块)。
- 每块**连解析两次、逐字节一致**才算过,3 次不过 → **硬失败并打印元凶类**,提示从头文件/`Roots` 里去掉(不再自适应切分)。
- `Root.Group` 字段**删除**(不再需要)。

**结果**:**两次独立完整运行逐字节相同的 model** ✓,而且链是**完整的** —— 实测 `QsApi_QLineEdit` 覆盖 **28 个槽**,包含继承来的 `customEvent`/`eventFilter`/`timerEvent`/`childEvent` ✓(之前这类会因为跨 chunk 而丢失)。

**顺带**:CppSharp 自己也会崩(`DeclConverter` 在某个模板别名上抛 `InvalidOperationException: Only dependent classes have template parameters`),是否崩取决于一次转换多少东西 → `ParseChunk` 现在 try/catch,把崩溃当作"这块没过",交由硬失败处理。

### D. 其它真 bug
- **信号重复**:`Roots` 里多行共用一个头文件(`QValidator/QIntValidator/QDoubleValidator` 都在 `qvalidator.h`)会让头文件被扫描多次 → 每个信号出现 N 次 → C# 里同名属性撞 `CS0102`。现在按 `(Module,Header)` 去重后再扫描,并在 model 里按 `名称(参数)` 再折叠一次。
- **头文件写错难排查**:CppSharp 把"某个 header 不存在"报成 `Files not found: 'a.h,b.h,...'`(列出整块),现在 extract 启动时先逐个校验 `Roots` 的 `(Module,Header)` 是否存在并点名。
- `QValidator` 在 Qt6 属于 **QtGui** 而不是 QtWidgets(最初按 QtWidgets 写导致整块解析失败)。

### E. 已知形式化瑕疵(已写进代码注释)
`CallBase_*` / `__base` 转发器把 `self` 静态转换成**生成它的那个子类**(例如 `QsApi_QObject*`),而对象实际可能是更深的子类(`QsApi_QLineEdit`)。**数值上是对的**——本模型里所有继承链都把声明基类放在偏移 0,所以转换是恒等;但按标准是 UB。完全合规的写法需要"派生辅助类 + friend 声明"的技巧。改动前请先看 `ApiCodeGen.WriteNativeClass` 里的 NOTE。

### F. 数字
| | 上一轮 | 本轮 |
|---|---|---|
| 类 / 枚举 | 34 / 58 | **44 / 75** |
| 方法总数 | 1088 | **1309** |
| 生成 C# / native | 11.9k / 6.9k 行 | **15.8k / 9.8k 行** |
| native 导出 | 1202 | **1496** |
| extract | ~190 秒(自适应分块) | **~165 秒(按头文件固定分块)** |

## 下一 agent 可继续的方向(按优先级)
> **注:前端迁移(ClangSharp)已完成(㉘)。** 下面 1–7 是当前的优先级(1–3 已完成,保留作历史)。
1. ~~按覆盖率榜继续加类型~~ ✅(词汇表这一轴做完,只余低收益项)。
2. ~~`FromHandle` 元对象定型 / 所有权运行时服务~~ ✅(见"⑰"、"⑳")。
3. ~~把旧 demo 逐个切到新链路~~ ✅(14 个全部搬完,㉘)。
4. **容器类型** ✅(`QStringList`/`QList<T>`/`QMap`/`QHash`/`QBindable` 都已入,见"⑧/⑩/⑪");剩 `QList<std::pair<...>>`(6)、`QFont::Tag`(8)、`QLatin1String`。
5. 复现/验证任何 native 崩溃:用 `native/CrashTracer.cpp`(VEH+StackWalk64)+ `tools/disasm.py`(capstone)。
6. 已知不可行:RHI×2(需 QRhi + `.qsb` shader blob)、Window Embedding、QtStateMachine(模块未安装)、QtSvg/PrintSupport(运行时不带 DLL)、QtPdf。
7. **继续搬示例**:painting 组还剩 `pathstroke` / `transformations` / `affine` / `deform` / `imagecomposition`(都要写一个 `ApiArthurFrame` 等价物;`affine`/`deform`/`imagecomposition` 还要程序化贴图),之后是 itemviews(addressbook / simpletreemodel / editabletreemodel / spreadsheet)、richtext、tools(regexp / settingseditor)、animation 各组。
8. **㉗ 那个偶发 `0xC000041D` 退出崩溃**(`api-windowflags` 3/10、`api-systray` 约 1/22)仍未查清,两条待验证假设见"㉗";要几十次重复运行,需与用户确认机器空闲时再跑。

## 给新 session 的提醒(踩坑清单)
- **手写 UI 从 `samples/hello/` 抄**:一个 `<Import Project="..\..\QtSharp.Api.props" />` 就够(生成绑定 + 运行时 + native/Qt DLL 拷贝)。门禁会 build+run 它,所以这条路不会烂。
- **改 native(`build_api.bat`)之后必须重建托管侧再跑**:中间态(native 是新的、生成绑定仍是旧的)会让 `ApiNative.Destroy_<X>`/`<X>_get_...` 落到错误的导出函数上,表现为**偶发**的 `0xC0000005`(⑳ 实测过一次 `QAbstractButton_text`;干净重建后 10/10 无事)。`regen-api.ps1` 的顺序是对的,手敲命令时别漏 `dotnet build`。
- **`owned` 语义**(⑳):模型里的 `owned` = "**托管侧是否可以释放它**";运行时由 `__ownsNative` + 生成的 `if (object->parent()) return;` 把关。加新类时:凡"Qt 通过 **非 parent 途径**收养"的类(交给 scene/view/clipboard/`setGraphicsEffect`/`setTransformations`)必须写 `owned: false`,否则 GC 会把 Qt 还在用的对象删掉。`QObject` 自己永远 `owned:false`(释放路径是继承的,否则子类的 `owned:false` 形同虚设)。
- **只有一条链路了**(㉘):`gen/ApiModel.cs`(模型 + `Roots` + 词汇表)+ `gen/ApiModel2.cs`(libclang 前端,`extract2`)+ `gen/ApiCodeGen.cs`(codegen)→ `out/api/QtSharp.Api.cs` + `out/api/QtSharp.Api.native.cpp` → `build_api.bat` → `out/api/QtSharp.Api.dll`。
- **不要用 PowerShell 的文本 cmdlet 改非 ASCII 文件**(`Get-Content` 默认按 ANSI/GBK 解码,`Set-Content -Encoding utf8` 再写回 → 中文永久损坏)。改 `.md`/含中文的源码请用编辑工具。CONTEXT.md 因此被毁过一次,靠 `git checkout` 恢复。
- ~~旧链路生成很不稳定必须 retry~~(已随旧链路删除);现在 `extract2` 一条 TU **一次解析即确定**,model 门禁直接对哈希。
- `QPainter::setPen(QPen)`/`setBrush(QBrush)` 实际是 **`QPainter.Pen`/`QPainter.Brush` 属性**;`QModelIndex` 没有 `internalPointer` 导出(用 `InternalId`)。
- `QSettings(string,string)` ctor 时有时无;用 `QSettings(Scope, org, app)` 更稳。
- **`extract2` 只有 ~2.5 秒**,`tools/regen-api.ps1` 全程(含 native 编译与 29 个 demo + sample)约 5–7 分钟;**别并发跑**(会争 `testbind/bin/testapp.exe`,报 `MSB3021 文件被另一个进程占用`)。`QTSHARP_ROOTS` 可以只跑子集排查。
- **僵尸 `testapp` 会锁住 exe**:跑 demo 中断/超时后会留进程,下次构建直接 `MSB3021`。`Get-Process testapp | Stop-Process -Force` 清掉即可。
- **改生成代码的调用点时注意 `Q_PROPERTY` 改名**:属性占 Pascal 名后访问器退回 C++ 原名(`window.Children()`→`children()`、`QTimer.Interval()`→`interval()`、`QWidget.Width()`→`width()`);编译错误 `CS1955/CS1501`("X 不是函数" / "该方法没有采用 N 个参数的重载")就是这个信号,按报错行把 `.Prop(` 改成 `.prop(` 即可(`CS1501` 的报文里没有类名前缀)。
- **PowerShell 5.1 的字符串比较是大小写不敏感的**:`$a -ne $b` 对只有大小写差异的字符串返回"相等" → 用 `-cne`;写"改大小写"的批量脚本时曾因此静默失败两次。
- 生成的类型必须在**同一个程序集**里能找到:`IQtObjectView` 放在 `runtime/ApiRuntime.cs` 的**命名空间级**(不能嵌在 `ApiRuntime` 里),生成代码是按裸名实现它的。
- 虚函数白名单是**名字闭包 + 继承子树**(`VirtualExtensionClasses` 里的类自己声明的虚函数名,只在它们的子树里发布):想开一个新扩展点(如 `QRunnable::run`、`QSortFilterProxyModel::filterAcceptsRow`),把它加进那张表即可;但**别用"全局名字闭包"**——`QPicture::setData(const char*, uint)` 这种同名无关方法会一起被打开(实测编译失败)。**副作用要有预期**:加一个类 = 它**自己重声明**的那些虚函数名也一起打开(`QSortFilterProxyModel` 一带进来,`mapToSource`/`setSourceModel` 也变成可重写点)。
- **`__managedSubclass` 决定虚函数的调用方式**(`ApiCodeGen`,㉝):**我们自己造**的对象(装了 native dispatch 钩子)⇒ pure virtual 返回 `default`、非 pure 链 `__base`;**采纳来的**对象(任何 `FromHandle`/`FromTransientHandle`/`FromOwnedHandle` 造出来的包装器)⇒ 一律走 `_vt`(`self->method(...)`,**虚调用**)。忘了给某条构造路径置位 ⇒ 托管子类被当成采纳对象 ⇒ native override ↔ C# stub 互相回派 ⇒ **栈溢出**(`api-custommodel` 踩过:`subclassCtor` 漏置位)。改动这里之后**必须**跑 `api-custommodel` / `api-widgets` / `api-ownership` / `api-stardelegate`。
- **私有虚函数(`private:`)会被 model 记录但绝不 override**(`ApiCodeGen` 的 `VirtualSlot.Blocked`):Qt 用它们做 NVI(`QAbstractListModel::columnCount`/`hasChildren`/`parent`),顶掉它们(哪怕只是 `return default`)会让 Qt 自己的逻辑出错 —— 曾经让 **每个 `QStringListModel` 的索引都无效**。槽必须留在链里(否则合并会往上挑中 `QAbstractItemModel` 的 public pure 声明),但**不生成 C# virtual / native override / dispatch case**。
- 字段/属性重名时:字段优先拿属性名(重名则退化成 `GetX()/SetX()`),`Q_PROPERTY` 撞嵌套枚举/虚函数/字段时**跳过属性保留方法**(`echoMode`/`sizeHint` 是例子)。
- 自定义的 `QRunnable` 子类交 `QThreadPool::start` 前要 `SetAutoDelete(false)`(pool 默认会 `delete` 它,而托管侧也 owns 它 → double free)。
