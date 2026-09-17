# QtSharp 任务上下文 (session 续接用)

## 总体目标(已明确)
**做一套长期可用的 Qt for .NET 绑定**;"把 Qt 官方示例改写成 C# 并跑通"只是 **conformance suite / 验证手段**。
当前正处于"从 CppSharp 打补丁的 PoC → 产品骨架"的过渡期:**三个架构 PoC + model/codegen 都已实测通过**,并且**两个官方 demo 已经跑在新链路上**。
`testapp.exe` 不带参数 = 平铺 15 个旧链路 demo;自测命令:`metasig` / `vdispatch` / `own` / `api` / `api-analogclock` / `api-lineedits`。
**下一个大动作:把前端从 CppSharp 换成 ClangSharp**(见下"已决定"一节),新 session 的第一件事就是它的可行性验证。

## 当前状态速览(给新 session)
| 项 | 状态 |
|---|---|
| 旧链路(13+2 个 demo) | ✅ 全部能跑(`testapp all`,15 窗口) |
| examples-widgets.html 16 个 | 14 个完成;剩 RHI×2、Window Embedding(不可行)、DocumentViewer(PDF/打印缺模块) |
| 架构 PoC #1 信号(moc metacall) | ✅ `testapp metasig` 全 PASS |
| 架构 PoC #2 虚函数(生成的 C++ wrapper + `[UnmanagedCallersOnly]`) | ✅ `testapp vdispatch` 全 PASS |
| 架构 PoC #3 所有权(Qt-owned 弱引用 + SafeHandle + destroyed 失效) | ✅ `testapp own` 全 PASS |
| 产品化第 1 步:model(入库)+ codegen | ✅ 44 类 / 1309 方法;**model 可复现**(按头文件分块自校验)、codegen 纯函数 |
| **两个 demo 跑在新链路上** | ✅ `testapp api-analogclock`(事件循环 + 渲染 + 鼠标/自定义事件)✅ `testapp api-lineedits`(布局 + 控件 + 校验器 + 信号) |
| **前端迁移(已决策)** | ✅ **已完成**:`qtgen extract2`(ClangSharp)取代 CppSharp 成为默认前端,~2.5 秒产出**逐字节一致**的 model;原 44 类 model 复刻 **1472/1472** 条事实(见"路线 A 可行性验证"一节) |
| **QtWidgets 覆盖面** | 🚧 **44 → 296 类**(QtWidgets 184 + QtGui 70 + QtCore 42)/ **7209 方法** / 233 信号 / 309 枚举;5 个新链路 demo 共 87 项断言全 PASS(`api` 6 / `api-analogclock` 10 / `api-lineedits` 9 / `api-widgets` 53 / `api-graphics` 9)。**QGraphics\* 整组已入**(scene/view/15 个 item 类/9 个 scene event/effect/transform/layout)。**类型类别已补**:`const char*`、**泛型 `QList<T>`**(参数+返回)、`QStringList`、`QStringView`/`QAnyStringView`、`qsizetype`/`qint64`/`uint`/`QRgb`、`short`/`ushort`/`long`/`ulong`/`char`/`uchar`/`char16_t`/`char32_t`;类补了 `QMetaObject`/`QCalendar`/`QMimeData`/`QBitArray`/`QIODevice`/`QDataStream`/`QMetaType`/`QRegularExpression`/`QTextStream`/`QColorSpace`/`QTimeZone`。仍缺:`QByteArrayView`、`QBindable<T>`、`QMap<...>`、`const uchar*`、`QGraphics*` 整组 |

## 关键背景
- 仓库:`D:\QtSharp`(QtSharp + vendored CppSharp)
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
- **`ApiTypes.Managed` 的坑**:`char*` 也以 `*` 结尾,必须先判字符串再判指针,否则 `char*`→`IntPtr` 而 codegen 渲染成 `string` → **同一 C# 签名出现两个重载(CS0111)**。
- 顺带:生成的 ctor 局部变量 `context` 撞了 `QShortcut(... Qt.ShortcutContext context)`,已改名 `__context`。
- **QGraphics\* 整组入库**(scene/view/15 个 item 类/9 个 scene event/effect/transform/layout/anchor):item **不是 QObject**且由 scene 拥有,所以一律 `owned: false`(GC 不删,暂时会泄漏,等所有权服务);scene event 由 Qt 在栈上构造 → `bindCtors: false`。**新增 demo `testapp api-graphics`**(9 项断言,含 `scene.items()` 的 `QList<QGraphicsItem*>` 返回、`itemAt()` 的身份一致、离屏 `view.Grab()` 真的渲染出 240x120)。
- 又补 29 个类(其余 QtWidgets):`QSplashScreen`/`QToolTip`/`QWidgetAction`/`QWizardPage`/`QUndoGroup`/`QSplitterHandle`/`QStylePainter`/`QTableWidgetSelectionRange`/`QTreeWidgetItemIterator`/`QPlainTextDocumentLayout`/`QItemEditorFactory`/`QStyleOption*` 4 个/`QStyleHintReturn*` 3 个/`QScroller`/`QScrollerProperties`/手势家族 5 个/`QGestureEvent`/`QGestureRecognizer`。

**⑨ 已知瑕疵 / 下一步(按收益排序)**
1. **`QMap<...>`**(x15)和 **`QByteArrayView`**(x27)、**`QBindable<T>`**(x18)是现在最大的三块:`QMap` 可以照抄 `QList` 的写法(需要 count/key/value 三个访问器);`QByteArrayView` 是字节视图(定成 `byte[]` 最自然);`QBindable<T>` 需要 `bindable*()` 那族。
2. `const uchar*`(x10,图像的 scanline 缓冲)+ `void*`/`const void*`(x27)是剩余的裸指针类别。
3. **item 类暂时 `owned: false`**(视图/布局接管所有权,GC 释放会 double free)→ 会泄漏;正解是 PoC #3 的所有权服务。
4. 派生类重声明的虚函数生成的是 **C# `virtual` 隐藏**(CS0114)而非 `override`:native 派发路径是对的(槽位按声明类解析),但从基类类型调用会走基类实现。要彻底修需要 codegen 记录"基类是否已声明同名虚函数"。
5. `QErrorMessage.FromHandle` 等静态成员隐藏了继承的同名静态(CS0108 警告),同理可加 `new`。
6. `QMetaObject` 已入库(动态派发能力到手:`metaObject().className()`/`indexOfSignal()` 都可用),但它的 `QMetaMethod`/`QMetaProperty` 还没绑,`method()`/`property()` 那族仍被挡。

### 解析配置清单(CppSharp 实际传给 clang 的东西 —— 已 dump,`qtgen dumpopts`)
来源:`gen/ApiModel.cs` 的 `ModelLibrary.Setup` + `QtLibrary.QtIncludeRoot` + `CppSharp.Parser.ParserOptions.SetupMSVC`。
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
`qtgen dumpopts`(本轮新增)可随时重新打印这份配置。

### 新前端要产出的"事实契约"(就是现在 ApiModel 从 CppSharp 读的那些)
- **类**:原始名、是完整定义还是前向声明(必须能区分!前向声明不能覆盖真实类)、`isAbstract`、**所有**基类(按声明顺序,基类是否是"绑定类"由 `Roots` 名字集合判定 —— 用名字判定而不是 AST 对象身份)
- **构造**:public、非拷贝、非 deleted;参数类型 + 默认值
- **方法**:public、非 virtual、非 operator、非析构、非构造;`static` 标志;参数列表
- **虚函数**:在 `VirtualAllow` 白名单里的虚函数(只要**本类声明的**;链由 codegen 合并)
- **枚举**:限定名(`Qt::X` / `QColor::Spec`)、项+值、`QFlags` typedef(要用 typedef 名,并带底层枚举的 items)
- **信号**:仍然是**正则扫头文件**(`Program.ScanSignals`),与 clang 无关,原样保留
- **类型词汇表/规范化**:保持现在 `Normalize`/`Supported` 的语义(标量、`QString`、`int*` 只收 const、`Foo*`、按值 `Foo` → boxed、`X::Y` 枚举、`QFlags` typedef);新前端的优势是可以**直接用 clang 的类型拼写字符串**,不用再玩 `Desugar()`/身份比较

### 新前端**不该**继承的东西(这些正是脆弱源)
`Driver`/`Module`/passes/`IsGenerated`、`Type.Desugar()`、`CompleteDeclaration`、跨 TU 身份、`DeclConverter`、以及"按头文件分块 + 每块两次解析一致"这套自校验(新前端的目标是**一次解析即确定**,不需要它)。

### 迁移路径(5 步,风险最小)
1. 保留 `model/*.json` 作为接口;写 `extract2`(ClangSharp),**先只对当前 44 个类**产出同样的 model;
2. 用现成的 **model diff 门禁**对拍 `extract` vs `extract2` —— 一致即复刻成功(`tools/regen-api.ps1` 的门禁可直接复用,或加一个 `-Extractor2` 开关);
3. 按 `Roots` 逐类迁(自然会暴露漏掉的类型规则),直到全覆盖;
4. 切默认前端;删掉分块/两次校验那套机器;把 extract 时间打进 CI;
5. 最后才动 vendored CppSharp —— **注意旧链路 15 个 demo 仍在用 `QtSharp.def` + `build_glue.bat` + CppSharp 的 pass/codegen,删不得**,要等旧 demo 也搬完。

### 诚实的风险
CppSharp 替我们解决了一个不小的坑:**Qt 头的 MSVC 兼容解析**(`-fms-compatibility`、`__cplusplus`、`_MSC_VER` 等)和 libclang 分发。换前端必须精确复刻这套配置(约 20 行,但要**验证**)。另外**模板/默认参数的提取会比 CppSharp 的"结构化 AST"更手工** —— 这正是路线 B 作为备选的原因(自己吐 JSON 可以只吐我们要的那几个事实)。

## 下一 agent 的第一件事
**继续啃类型类别**(`const char*`/`QStringList`/**`QList<T>`**/整数 typedef/字符串视图/小标量已补完,见"⑧";QGraphics\* 整组与其余 QtWidgets 类已入库)。
剩下的按收益:①**`QMap<K,V>`**(15 处,照抄 `QList<T>` 的写法 + key/value 访问器);②`QByteArrayView`(27,字节视图 → `byte[]`)、`QBindable<T>`(18);③`const uchar*`(10,scanline 缓冲)与 `void*`/`const void*`(27);④`QMetaMethod`/`QMetaProperty` 那族(动态派发/属性读写)。
做法:改 `ApiModel2.ModelName` 的词汇表 + `ApiTypes.Managed` + `ApiCodeGen` 的 `ManagedType`/`NativeParamType`/`CppParamType`/`CppArg`/`NativeRetCpp`(+ 需要临时对象的参数走 `MarshalParams`/`EmitGuarded`),每加一类就跑 `tools/regen-api.ps1` 看 `blocked by` 榜往下走。

## 其它可继续的方向
1. `extract2` 已通过验收,可以**删掉 CppSharp 的 extract 路径**(`ApiModel.RunExtract`/`ExtractChunk`/`ParseChunk`/`ModelLibrary`)以及 `tools/regen-api.ps1` 的 `-Extractor1`——但**旧链路 15 个 demo 仍在用 CppSharp 的 pass/codegen**(`QtSharp.def`+`build_glue.bat`),那部分要等旧 demo 搬完。
2. 把旧 demo 逐个搬进新链路:现在 `QWidget`/`QLayout`/`QAbstractItemView`/`QStandardItemModel` 都齐了,**SpinBoxes / Sliders / WindowFlags / Calculator / Menus / Basic Layouts** 看起来可以直接重写(它们的头文件/类都在 model 里了)。
3. item 类的所有权(item 由视图/布局持有)→ 落地 PoC #3 的所有权服务。
4. 派生类虚函数用 `override`(见"已知瑕疵 4")。

## 关键构建知识
- `FunctionExtensions.cs` 在 `CppSharp/src/AST/` 中,编译进 **CppSharp.AST.dll**。改它必须重建 `CppSharp.AST.csproj`(带 `-o D:\QtSharp\lib`)。
- `CSharpSources.cs`、`SymbolsCodeGenerator.cs` 在 `CppSharp/src/Generator/` 中,编译进 **CppSharp.Generator.dll**(`tools/rebuild-generator` 项目,引用预编译 lib)。
- gen 运行**易 flaky**(类型退化 f1/f2、QColorDialog 错位、mask 未定义等随机出现),必须用 retry 循环直到 `dotnet build testbind` 0 错误。
- 生成产物 `out/`、`testbind/QtSharp.*.cs` 被 .gitignore,不入库。

## 构建/重新生成命令序列
### A. 旧链路(15 个 demo 用的绑定)
1. `dotnet build D:\QtSharp\CppSharp\src\AST\CppSharp.AST.csproj -c Release -o D:\QtSharp\lib`
2. `dotnet build D:\QtSharp\tools\rebuild-generator\CppSharp.Generator.csproj -c Release` 然后拷贝 dll 到 `lib\CppSharp.Generator.dll` 和 `gen\bin\Release\net10.0\win-x64\`
3. `dotnet build D:\QtSharp\gen\gen.csproj -c Release`
4. 删除 `out\qt_min\*.cs,*.cpp`,运行 `qtgen.dll`(retry 直到干净)
5. 拷贝 .cs 到 testbind + `build_glue.bat` 重建 native dll + `dotnet build testbind`

### B. 新链路(model → codegen,产品骨架)
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
`-Extractor1` 走旧的 CppSharp extract(分块+双解析自校验,~190 秒),只在需要交叉验证时用。
`extract`/`extract2` 都支持 `QTSHARP_ROOTS=<逗号分隔类名>` 只提取子集(排查用;注意类之间的继承链要一起给)。

### C. 三个 PoC 与自测命令
```
testapp metasig         # PoC #1 通用 moc 信号派发
testapp vdispatch       # PoC #2 [UnmanagedCallersOnly] 虚函数派发
testapp own             # PoC #3 所有权/生命周期
testapp api             # 新链路:QObject 冒烟(方法/信号/虚函数)
testapp api-analogclock # 新链路:完整 AnalogClock demo(真实事件循环 + 渲染 + 事件回调)
testapp api-lineedits   # 新链路:完整 LineEdits demo(布局 + 控件 + 校验器 + 信号)
testapp api-widgets     # 新链路:QtWidgets 巡礼(菜单/工具栏/标签页/分割器/三个 item view/model-view-proxy/mapper/日历/undo)
testapp all             # 15 个旧链路 demo 平铺
```
PoC 只依赖 `QtSharp.dll`(旧 native dll),`api`/`api-analogclock` 只依赖 `QtSharp.Api.dll`(新 native dll),二者互不干扰。
新链路命令自己通过 `ApiRuntime.StartApplication()` 建 `QApplication`,所以必须在旧链路的 `QApplication` **之前**分发(见 `testbind/Program.cs`)。`QTSHARP_DEMO_MS=8000` 可让 `api-analogclock` 在屏幕上多留一会儿(默认 1.5 秒)。

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
- `gen/Program.cs`(scope 裁剪/CollectRoots + AddMethodDef wrapped 优先 + QFlagsReturnWhitelist + itemviews 头文件 + QSettings RootName;新增 `dumpopts` 打印 CppSharp 传给 clang 的全部配置)
- `tools/clangprobe/`(**路线 A 的可行性验证工具**,不入产品链路):`ClangProbe.csproj`(ClangSharp 21.1.8.2 + libClangSharp.runtime.win-x64 21.1.8.2)、`Program.cs`(取事实的完整实现:`--class`/`--all`/`--raw`/`--tree`/`--dump`)。**`extract2` 的逻辑从这里搬**。
- `testbind/Program.cs`(命令分发;未匹配 = all)
- `testbind/AllDemos.cs`(`CreateAll()` + `ShowTiled()`,全部 demo 顶层窗口平铺)
- `testbind/QtCompat.cs`(`QTextCharFormat.SetForeground` + Shortcut Editor marshal 辅助 + QSettings/QPixmap shim 包装)
- `testbind/WidgetsGallery.cs`、`testbind/DocumentViewer.cs`、`testbind/ShortcutEditor.cs`、`testbind/Calculator.cs`、`testbind/Scribble.cs`、`testbind/CalendarWidget.cs`、`testbind/ShapedClock.cs`、`testbind/SpinBoxes.cs`、`testbind/SystemTray.cs`
- `native/QtSharp_inline.cpp`(手写 shim:QString/QKeySequence/QAction/QSettings/dataChanged/QPixmap)
- `native/CrashTracer.cpp`、`build_crashtracer.bat`、`tools/disasm.py`(ABI 调试工具)
- **新链路(产品骨架)**:
  - `model/qtsharp.api.json`(**入库**,规范化 API model;v2:**34 个类 + 58 个枚举(10 个 flags)**,含构造/继承/可选参数/boxed 值类型)
  - `gen/ApiModel2.cs`(`extract2`,`ApiModel` 的 partial:一条 TU + libclang 索引 → model;类型词汇表/`QFlags`/枚举限定/默认参数切片/非 const 引用判定都在这里)
  - `gen/ApiModel.cs`(`extract`:parse-only AST → model;**分块 + 每块两次解析自校验**(CppSharp 大 parse 不可复现,见"⑤ C");`Roots` 表 = 类名+头文件+模块+是否绑 ctor+`owned`;按自有类型白名单判定;处理间接返回/引用/property/QPrivateSignal;按 **C# 映射后签名** 折叠重载)
  - `gen/ApiCodeGen.cs`(`codegen`:model → C# + C++ wrapper + P/Invoke;纯函数。继承/次要基类视图/枚举/flags/ctor 重载/boxed 值类型/整链虚函数 `__base` 转发/静态方法/`int[]` 数组参数)
  - `native/QtSharpApiRuntime.{h,cpp}`(手写运行时:moc 信号转发器 + QString 桥 + 应用启动 `qs_api_app_*` + `qs_api_event_type` + 事件探针)
  - `build_api.bat`(→ `out/api/QtSharp.Api.dll`)、`tools/regen-api.ps1`(两道门禁 + 三个冒烟 demo;`-Adopt` 才采纳新 model)
  - `testbind/ApiRuntime.cs`(C# 侧运行时:`QSignal<T>`/`Wrap`(按 handle 存多视图)/信号解包(`Decode`:bool/int/QString/**QStringList**)/`SendUserEvent`/`SendMousePress`)、`testbind/ApiSmoke.cs`(`testapp api`)、`testbind/AnalogClockApi.cs`(`testapp api-analogclock`)、`testbind/LineEditsApi.cs`、`testbind/WidgetsApi.cs`(`testapp api-widgets`,44 项断言)
- **三个 PoC**:
  - `native/QtSharp_meta.cpp` + `testbind/MetaSignal.cs`/`MetaSignalTest.cs`(PoC #1 信号)
  - `native/QtSharp_vdispatch.cpp` + `testbind/VirtualDispatch.cs`(PoC #2 虚函数)
  - `native/QtSharp_ownership.cpp` + `testbind/Ownership.cs`(PoC #3 所有权)

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
- 生成产物 `out/`、`testbind/QtSharp.*.cs`、`testbind/QtSharp.Api.cs`、`testbind/Std.cs`、`testbind/GeneratedSignals.cs`、`native/QtSharp_signals.cpp` 被 .gitignore;`native/QtSharp.def` 与 `model/qtsharp.api.json` 被跟踪。
- 工作区干净(commit `67d9fd2`)。

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
> **注:前端迁移(ClangSharp)已决策并排在第一位**,见上面"已决定"一节。下面的 1–6 是迁移期间/之后的优先级。
1. **按覆盖率榜继续加类型**。当前榜:`Char* x11`(C 字符串参数)、`QIcon& x9`、`QList<...> x9`、`TemplateParameterType x9`、`Connection x6`(`QMetaObject::Connection` → disconnect!)、`QKeySequence& x5`。加类 = `Roots` 加一行(注意 `Group` 别把类和它的虚函数祖先分开)。
2. **补限制 1/2**:`FromHandle` 走 metaObject 动态定型;把 PoC #3 的所有权规则做成运行时服务(替换创建时那个永不释放的 `GCHandle`)。
3. **把旧 demo 逐个切到新链路**:LineEdits / SpinBoxes 的依赖基本齐了(还需 `QLineEdit`/`QComboBox`/`QGroupBox`/`QLabel`/`QValidator`/`QLayout` 家族的 `Roots` 行);Scribble 需要 `QMouseEvent` 的 move/button 状态(已具备)+ `QImage`(已具备)。
4. **容器类型是下一个"类型类别"**:`QStringList`/`QList<T>`/`QVector<T>` 挡掉 9+9 个方法(信号和槽里到处都是)。需要设计:模型里怎么表达容器、运行时怎么 marshal(参考 `QString` 的桥接方式)。
5. **旧链路绑定 + 移植示例**(conformance suite):`QPainter` gradients 一次解锁 9 个 painting 示例;`QGraphicsView` 家族解锁 graphicsview 整组。旧链路只做 conformance,不再投入。
6. 复现/验证任何 native 崩溃:用 `native/CrashTracer.cpp`(VEH+StackWalk64)+ `tools/disasm.py`(capstone)。
7. 已知不可行:RHI×2(需 QRhi + `.qsb` shader blob)、Window Embedding、QtStateMachine(模块未安装)、QtSvg/PrintSupport(运行时不带 DLL)、QtPdf。

## 给新 session 的提醒(踩坑清单)
- **新链路**(`gen/ApiModel.cs` + `gen/ApiCodeGen.cs`)是产品方向,**旧链路**(`gen/Program.cs` 的 `QtLibrary` + `QtSharp.def` + `build_glue.bat`)只作 conformance suite;两者产物互不干扰(`QtSharp.dll` vs `QtSharp.Api.dll`)。
- **不要用 PowerShell 的文本 cmdlet 改非 ASCII 文件**(`Get-Content` 默认按 ANSI/GBK 解码,`Set-Content -Encoding utf8` 再写回 → 中文永久损坏)。改 `.md`/含中文的源码请用编辑工具。CONTEXT.md 因此被毁过一次,靠 `git checkout` 恢复。
- 旧链路生成**很不稳定**,必须用 retry 直到 `dotnet build testbind` + `build_glue.bat` 都干净(见"踩过的坑")。
- 加 `qmdiarea.h`/`qmdisubwindow.h` 会触发 CppSharp 的 `event`→`QFlagsStorage` 重命名 bug,**已放弃 MDI**。
- `QPainter::setPen(QPen)`/`setBrush(QBrush)` 实际是 **`QPainter.Pen`/`QPainter.Brush` 属性**;`QModelIndex` 没有 `internalPointer` 导出(用 `InternalId`)。
- `QSettings(string,string)` ctor 时有时无;用 `QSettings(Scope, org, app)` 更稳。
- 生成器不确定性的根因是 **pass 按 `HashSet<Declaration>` 哈希序迭代 + `DeclarationsList` 按 `DefinitionOrder` 二分(并列退化)**,新链路的 model 通过"全排序"已规避;而**新链路自己的不确定性根因是 CppSharp 的 AST 转换**(见"⑤ C. 核心修复"),已用"分块 + 两次一致"挡住。改 `Roots` 后**必须**跑完整 `extract` 看它是否报 `FAILED`。
- `extract` 现在 ~190 秒(分块自校验),不是慢得离谱但也别在循环里跑;`QTSHARP_ROOTS` 可以只跑子集排查。
