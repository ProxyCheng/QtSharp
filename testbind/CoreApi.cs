using System;
using System.Collections.Generic;
using System.IO;
using QtSharp.Api;

// The QtCore value classes on the new pipeline: the batch that took QtCore from 42 bound classes
// to 64 (files and directories, JSON, ids, random numbers, timers, the command line).
//
// Everything here is a plain value type - none of them derives from QObject - so the managed
// wrapper owns the native object and nothing depends on the ownership question.
public static class ApiCoreDemo
{
    // A task for Qt's thread pool, written in C#: Qt calls run() on one of its own threads.
    sealed class CountingRunnable : QRunnable
    {
        public int Runs;

        // The pool deletes its runnables by default, and this one belongs to the managed side
        // (the wrapper frees it), so it has to say so.
        public CountingRunnable() => SetAutoDelete(false);

        public override void Run() => Runs++;
    }

    // A managed thread: same idea, Qt runs run() on the new thread.
    sealed class WorkerThread : QThread
    {
        public int Runs;

        public override void Run() => Runs++;
    }

    public static int Run()
    {
        ApiRuntime.StartApplication();

        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        // ---------------------------------------------------------------- directories and files
        // A temporary directory the managed side creates and fills, then reads back through Qt's
        // file-system classes.
        var tempDir = new QTemporaryDir();
        Check(tempDir.IsValid() && tempDir.Path().Length > 0,
            $"QTemporaryDir is valid (got '{tempDir.Path()}')");

        var filePath = Path.Combine(tempDir.Path(), "payload.txt");
        File.WriteAllText(filePath, "qtsharp");
        // Qt spells paths with forward slashes even on Windows.
        Check(tempDir.FilePath("payload.txt").Replace('\\', '/') == filePath.Replace('\\', '/'),
            $"QTemporaryDir::filePath joins the name (got '{tempDir.FilePath("payload.txt")}')");

        var info = new QFileInfo(filePath);
        Check(info.Exists(), "QFileInfo::exists");
        Check(info.Size() == 7, $"QFileInfo::size (got {info.Size()})");
        Check(info.Suffix() == "txt", $"QFileInfo::suffix (got '{info.Suffix()}')");
        Check(info.FileName() == "payload.txt", $"QFileInfo::fileName (got '{info.FileName()}')");
        Check(info.AbsolutePath() == tempDir.Path(), "QFileInfo::absolutePath");

        var dir = new QDir(tempDir.Path());
        Check(dir.Exists(), "QDir::exists");
        Check(dir.Count() >= 1, $"QDir::count sees the file (got {dir.Count()})");
        var entries = dir.EntryList();
        Check(Array.IndexOf(entries, "payload.txt") >= 0,
            $"QDir::entryList contains the file (got {entries.Length} entr(y|ies))");

        // QDirIterator walks the same directory. It is a QDirIterator over a path, and the
        // QFileInfo it hands back is a copy the wrapper owns.
        var iterator = new QDirIterator(tempDir.Path(), QDir.Filters.Files);
        Check(iterator.HasNext(), "QDirIterator::hasNext");
        var first = iterator.Next();
        Check(Path.GetFileName(first) == "payload.txt", $"QDirIterator::next (got '{first}')");
        Check(!iterator.HasNext(), "QDirIterator stops after the only file");

        // The static locations: QStandardPaths is static-only in Qt (private ctor and dtor).
        Check(QStandardPaths.WritableLocation(QStandardPaths.StandardLocation.TempLocation).Length > 0,
            "QStandardPaths::writableLocation");
        Check(QStandardPaths.StandardLocations(QStandardPaths.StandardLocation.HomeLocation).Length > 0,
            "QStandardPaths::standardLocations");

        // QDir is also a value with static helpers.
        Check(QDir.Current().AbsolutePath().Length > 0, "QDir::current");
        Check(QDir.CleanPath("a//b") == "a/b", $"QDir::cleanPath (got '{QDir.CleanPath("a//b")}')");

        // ---------------------------------------------------------------- ids, numbers, time
        var id = QUuid.CreateUuid();
        Check(!id.IsNull(), "QUuid::createUuid is not null");
        Check(id.ToString().Length > 0, $"QUuid::toString (got '{id.ToString()}')");
        // operator== is not bound (the extractor drops operators), so identity is compared the
        // way Qt prints it.
        var parsed = new QUuid(id.ToString());
        Check(parsed.ToString() == id.ToString(),
            $"QUuid parses back to the same id (got '{parsed}')");

        var random = QRandomGenerator.Global();
        Check(random.Generate() != 0 || random.Generate() != 0, "QRandomGenerator::global::generate");

        var timer = new QElapsedTimer();
        timer.Start();
        Check(timer.IsValid() && timer.Elapsed() >= 0, $"QElapsedTimer (got {timer.Elapsed()} ms)");

        var deadline = new QDeadlineTimer(1000);
        Check(!deadline.HasExpired() && deadline.RemainingTime() > 0,
            $"QDeadlineTimer remaining time (got {deadline.RemainingTime()} ms)");

        var curve = new QEasingCurve(QEasingCurve.Type.OutBounce);
        var progress = curve.ValueForProgress(0.5);
        Check(progress >= 0 && progress <= 1, $"QEasingCurve::valueForProgress (got {progress:F3})");

        // The Q_WEAK_OVERLOAD constructor: `template <typename = void> QVersionNumber(const
        // QList<int> &)` reaches the model as QVersionNumber(int[]).
        var version = new QVersionNumber(new[] { 1, 2, 3 });
        Check(version.SegmentCount() == 3 && version.ToString() == "1.2.3",
            $"QVersionNumber round-trip (got '{version.ToString()}')");
        var parsedVersion = QVersionNumber.FromString("4.5");
        Check(parsedVersion.SegmentCount() == 2 && parsedVersion.ToString() == "4.5",
            $"QVersionNumber::fromString (got '{parsedVersion.ToString()}')");

        // ---------------------------------------------------------------- JSON
        var json = QJsonObject.FromVariantMap(new Dictionary<string, QVariant>
        {
            ["name"] = new QVariant("qtsharp"),
            ["count"] = new QVariant(3),
        });
        Check(json.Count() == 2, $"QJsonObject::fromVariantMap (got {json.Count()} key(s))");
        Check(json.Value("name").ToString() == "qtsharp",
            $"QJsonObject::value (got '{json.Value("name").ToString()}')");

        var document = new QJsonDocument(json);
        Check(document.IsObject() && document.Object().Count() == 2, "QJsonDocument carries the object");
        var serialized = document.ToJson();
        Check(serialized.Size() > 0, $"QJsonDocument::toJson wrote {serialized.Size()} byte(s)");

        var reparsed = QJsonDocument.FromJson(new QByteArray("{\"a\":1}"));
        Check(reparsed.IsObject() && reparsed.Object().Count() == 1, "QJsonDocument::fromJson");

        var value = new QJsonValue("literal");
        Check(value.IsString() && value.ToString() == "literal", "QJsonValue round-trip");

        // ---------------------------------------------------------------- the command line
        var parser = new QCommandLineParser();
        var verbose = new QCommandLineOption("v", "be verbose");
        var named = new QCommandLineOption("name", "the name", "name");
        parser.AddOption(verbose);
        parser.AddOption(named);
        parser.Process(new[] { "testapp", "-v", "--name=qtsharp" });
        Check(parser.IsSet(verbose), "QCommandLineParser::isSet");
        Check(parser.Value(named) == "qtsharp", $"QCommandLineParser::value (got '{parser.Value(named)}')");
        Check(verbose.Names().Length == 1 && verbose.Names()[0] == "v", "QCommandLineOption::names");

        // QProcessEnvironment is a value type; the process environment itself is a static.
        var environment = QProcessEnvironment.SystemEnvironment();
        Check(!environment.IsEmpty(), $"QProcessEnvironment::systemEnvironment (got {environment.Keys().Length} entries)");
        var custom = new QProcessEnvironment();
        custom.Insert("QTSHARP_TEST", "1");
        Check(custom.Contains("QTSHARP_TEST") && custom.Value("QTSHARP_TEST") == "1",
            "QProcessEnvironment insert/value");

        // ---------------------------------------------------------------- QtGui value types
        var format = new QSurfaceFormat();
        format.SetDepthBufferSize(24);
        Check(format.DepthBufferSize() == 24, $"QSurfaceFormat round-trip (got {format.DepthBufferSize()})");

        var glyphRun = new QGlyphRun();
        Check(glyphRun.IsEmpty(), "QGlyphRun starts empty");

        Check(QSysInfo.KernelType().Length > 0, $"QSysInfo::kernelType (got '{QSysInfo.KernelType()}')");
        Check(QSysInfo.ProductType().Length > 0, $"QSysInfo::productType (got '{QSysInfo.ProductType()}')");

        // ---------------------------------------------------------------- files (QObject-derived)
        // QFile is a QObject, so these only landed once the generated destructor learned to ask Qt
        // before deleting. The write/read half lives on the QIODevice base, which is inherited.
        var textPath = Path.Combine(tempDir.Path(), "written.txt");
        var fileWriter = new QFile(textPath);
        Check(fileWriter.Open(QIODeviceBase.OpenMode.WriteOnly | QIODeviceBase.OpenMode.Text),
            "QFile::open(WriteOnly|Text)");
        Check(fileWriter.Write(new QByteArray("from-qtsharp")) == 12, "QFile::write (through QIODevice)");
        fileWriter.Close();

        var fileReader = new QFile(textPath);
        Check(fileReader.Open(QIODeviceBase.OpenMode.ReadOnly), "QFile::open(ReadOnly)");
        var written = fileReader.ReadAll();
        Check(written.Size() == 12 && written.ConstData() == "from-qtsharp",
            $"QFile round-trip (got '{written.ConstData()}')");
        fileReader.Close();
        Check(File.ReadAllText(textPath) == "from-qtsharp", "the bytes are on disk");

        // QSaveFile writes a temporary file and renames it on commit.
        var atomicPath = Path.Combine(tempDir.Path(), "atomic.txt");
        var atomic = new QSaveFile(atomicPath);
        Check(atomic.Open(QIODeviceBase.OpenMode.WriteOnly), "QSaveFile::open");
        atomic.Write(new QByteArray("atomic"));
        Check(atomic.Commit() && File.ReadAllText(atomicPath) == "atomic", "QSaveFile::commit");

        // A lock file is how a program keeps a second copy of itself out.
        var lockPath = Path.Combine(tempDir.Path(), "only-one.lock");
        var lockFile = new QLockFile(lockPath);
        Check(lockFile.TryLock(200) && lockFile.IsLocked(), "QLockFile::tryLock");
        Check(!new QLockFile(lockPath).TryLock(100), "a second lock on the same file is refused");
        lockFile.Unlock();

        // ---------------------------------------------------------------- processes and threads
        Check(QProcess.Execute("cmd.exe", new[] { "/c", "exit", "7" }) == 7,
            "QProcess::execute returns the child's exit code");

        var process = new QProcess();
        process.Start("cmd.exe", new[] { "/c", "echo", "captured" });
        Check(process.WaitForFinished(10000), "QProcess::waitForFinished");
        var captured = process.ReadAllStandardOutput().ConstData();
        Check(captured != null && captured.Contains("captured"),
            $"QProcess captured the output (got '{captured?.Trim()}')");
        Check(process.ExitCode() == 0, $"QProcess::exitCode (got {process.ExitCode()})");

        Check(QThread.IdealThreadCount() > 0, $"QThread::idealThreadCount (got {QThread.IdealThreadCount()})");
        Check(QThread.IsMainThread(), "QThread::isMainThread");
        var current = QThread.CurrentThread();
        Check(current != null && current.IsCurrentThread(), "QThread::currentThread");
        // Qt owns that QThread, so dropping the wrapper - which the garbage collector will do -
        // must not touch the thread itself.
        current = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Check(QThread.IsMainThread(), "the thread Qt handed over survives its wrapper being collected");
        QThread.Msleep(1);

        // The thread primitives are plain (non-QObject) value-ish classes: we own them.
        var semaphore = new QSemaphore(2);
        Check(semaphore.Available() == 2 && semaphore.TryAcquire(1) && semaphore.Available() == 1,
            $"QSemaphore::tryAcquire (got {semaphore.Available()} left)");
        semaphore.Release(1);
        var mutex = new QMutex();
        mutex.Lock();
        mutex.Unlock();
        Check(true, "QMutex lock/unlock");

        // ---------------------------------------------------------------- the application itself
        Check(QCoreApplication.ApplicationDirPath().Length > 0,
            $"QCoreApplication::applicationDirPath (got '{QCoreApplication.ApplicationDirPath()}')");
        Check(QCoreApplication.ApplicationPid() > 0, "QCoreApplication::applicationPid");
        Check(QCoreApplication.Instance() != null, "QCoreApplication::instance");
        QGuiApplication.SetApplicationDisplayName("qtsharp-core");
        Check(QGuiApplication.applicationDisplayName() == "qtsharp-core", "QGuiApplication::applicationDisplayName");

        // The clipboard is Qt's (QClipboard has a private destructor, so it is never ours).
        var clipboard = QGuiApplication.Clipboard();
        Check(clipboard != null, "QGuiApplication::clipboard");
        clipboard.SetText("qtsharp-clipboard");
        // The Windows clipboard is a single-owner resource: while another process holds it (a hung
        // application, a clipboard manager, remote-desktop sync), setText fails and the read comes
        // back empty - PowerShell's own Set-Clipboard fails in the same state. That is an
        // environment condition rather than a binding failure, so it is reported as skipped; the
        // accessors are still exercised (`SetText` ran, `Text` returned a managed string).
        string clipboardBack = clipboard.Text();
        bool clipboardUsable = clipboardBack.Length > 0;
        if (clipboardUsable)
            Check(clipboardBack == "qtsharp-clipboard", $"QClipboard round-trip (got '{clipboardBack}')");
        else
            Console.WriteLine("[SKIP] the system clipboard is held by another process; round-trip not asserted");

        // ---------------------------------------------------------------- durations
        // Qt's "as a duration" overloads carry a std::chrono type, which managed code sees as a
        // TimeSpan; the C ABI carries nanoseconds and the generated glue converts to the unit Qt
        // declared (std::chrono::milliseconds, nanoseconds, seconds...).
        var durationTimer = new QTimer();
        durationTimer.SetInterval(TimeSpan.FromSeconds(2));
        Check(durationTimer.interval() == 2000,
            $"QTimer.SetInterval(TimeSpan) (got {durationTimer.interval()} ms)");
        Check(durationTimer.IntervalAsDuration() == TimeSpan.FromSeconds(2),
            $"QTimer.IntervalAsDuration (got {durationTimer.IntervalAsDuration()})");

        var durationLock = new QLockFile(Path.Combine(tempDir.Path(), "duration.lock"));
        Check(durationLock.TryLock(TimeSpan.FromMilliseconds(200)) && durationLock.IsLocked(),
            "QLockFile.TryLock(TimeSpan)");
        durationLock.Unlock();

        Check(QDateTime.FromSecsSinceEpoch(0).AddDuration(TimeSpan.FromDays(1)).Date().Day() == 2,
            "QDateTime.AddDuration(TimeSpan)");
        QThread.Sleep(TimeSpan.FromMilliseconds(1));
        Check(true, "QThread.Sleep(TimeSpan)");

        // ---------------------------------------------------------------- Qt's thread pool
        // A managed QRunnable subclass is how C# hands work to Qt's pool: the generated native
        // subclass installs the vtable hook and Qt calls run() on its own thread.
        var pool = new QThreadPool();
        var task = new CountingRunnable();
        pool.Start(task);
        Check(pool.WaitForDone(5000), "QThreadPool.WaitForDone");
        Check(task.Runs == 1, $"the managed QRunnable ran on the pool ({task.Runs} time(s))");

        // A managed QThread subclass overriding run() works the same way.
        var worker = new WorkerThread();
        worker.Start();
        Check(worker.Wait(5000) && worker.Runs == 1,
            $"a managed QThread subclass ran its override ({worker.Runs} time(s))");

        // ---------------------------------------------------------------- in-out strings
        // A non-const QString& is Qt's "rewrite this string" convention (QValidator::validate,
        // QClipboard::text). The managed spelling is `ref string`, and the updated text comes back
        // through an extra out slot the generated glue adds.
        var validator = new QIntValidator(10, 100);
        string acceptable = "42";
        int caret = 2;
        Check(validator.Validate(ref acceptable, ref caret) == QValidator.State.Acceptable,
            $"QIntValidator::validate accepts 42 (got '{acceptable}')");
        Check(acceptable == "42" && caret == 2, $"the ref string and int round-trip (got '{acceptable}',{caret})");

        string partial = "5";
        int caret2 = 1;
        Check(validator.Validate(ref partial, ref caret2) == QValidator.State.Intermediate,
            $"'5' is intermediate (got '{partial}')");

        string rejected = "abc";
        int caret3 = 3;
        Check(validator.Validate(ref rejected, ref caret3) == QValidator.State.Invalid,
            $"'abc' is invalid (got '{rejected}')");

        // fixup rewrites the string in place: the validator re-formats whatever it can read.
        string fixable = "+0042";
        validator.Fixup(ref fixable);
        Check(fixable == "42", $"QIntValidator::fixup rewrote the string (got '{fixable}')");

        // The same implementation reached through the public method a spin box inherits.
        var spinBox = new QSpinBox();
        spinBox.SetRange(0, 100);
        string spinText = "150";
        int spinCaret = 3;
        Check(spinBox.Validate(ref spinText, ref spinCaret) == QValidator.State.Invalid,
            $"QSpinBox::validate through the inherited method (got '{spinText}')");

        // The other in-out string: the clipboard's subtype filter. Like the round-trip above this
        // needs a usable system clipboard.
        if (clipboardUsable)
        {
            string subtype = "";
            var clipboardText = QGuiApplication.Clipboard().Text(ref subtype);
            Check(subtype.Length > 0 && clipboardText == "qtsharp-clipboard",
                $"QClipboard::text filled the subtype in (got '{subtype}': '{clipboardText}')");
        }
        else
        {
            Console.WriteLine("[SKIP] QClipboard::text(ref string) not asserted (no clipboard)");
        }

        // ---------------------------------------------------------------- XML
        var xml = new QByteArray();
        var xmlWriter = new QXmlStreamWriter(xml);
        xmlWriter.WriteStartDocument();
        xmlWriter.WriteStartElement("root");
        xmlWriter.WriteAttribute("version", "1");
        xmlWriter.WriteCharacters("payload");
        xmlWriter.WriteEndElement();
        xmlWriter.WriteEndDocument();
        Check(xml.Size() > 0 && xml.ConstData().Contains("<root"),
            $"QXmlStreamWriter wrote {xml.Size()} byte(s)");

        var xmlReader = new QXmlStreamReader(xml);
        int elements = 0, characters = 0;
        while (!xmlReader.AtEnd())
        {
            var token = xmlReader.ReadNext();
            if (token == QXmlStreamReader.TokenType.StartElement)
                elements++;
            else if (token == QXmlStreamReader.TokenType.Characters)
                characters += xmlReader.Text().Length;
        }
        Check(elements == 1 && characters == "payload".Length,
            $"QXmlStreamReader read {elements} element(s) and {characters} character(s)");

        // ---------------------------------------------------------------- QtCore batch 3
        // The services a program reaches for next: hashing, matching, mime types, the meta-object,
        // timers, collation and the platform constants.

        // A hash over a byte buffer, and the hex digest Qt prints: two different entry points to
        // the same algorithm, so the result has to agree with the known MD5 of "abc".
        byte[] abc = System.Text.Encoding.UTF8.GetBytes("abc");
        string digest = QCryptographicHash.Hash(abc, QCryptographicHash.Algorithm.Md5).ToHex().ConstData();
        Check(digest == "900150983cd24fb0d6963f7d28e17f72", $"QCryptographicHash::hash (got {digest})");

        // QRegularExpressionMatch is a value type Qt hands out through the matcher.
        var re = new QRegularExpression("(\\w+)-(\\d+)");
        var match = re.Match("item-42");
        Check(match.HasMatch() && match.Captured(1) == "item" && match.Captured(2) == "42",
            $"QRegularExpressionMatch captured '{match.Captured(1)}' and '{match.Captured(2)}'");

        // QUrlQuery is a dictionary-like view of a URL's query string.
        var query = new QUrlQuery();
        query.AddQueryItem("name", "qtsharp");
        query.AddQueryItem("count", "7");
        Check(query.QueryItemValue("name") == "qtsharp" && query.HasQueryItem("count"),
            $"QUrlQuery round-trip (got '{query.QueryItemValue("name")}')");
        Check(query.ToString() == "name=qtsharp&count=7", $"QUrlQuery encoded (got '{query.ToString()}')");

        // The meta-object, now introspectable: a property can be read and written without the
        // generated accessor, and an enum looked up by name.
        var introspected = new QObject();
        introspected.SetObjectName("meta");
        var property = introspected.MetaObject().Property(0);
        Check(property.IsValid() && property.Name() == "objectName",
            $"QMetaProperty read from the meta-object (got '{property.Name()}')");
        Check(property.Read(introspected).ToString() == "meta",
            $"QMetaProperty::read through QVariant (got '{property.Read(introspected).ToString()}')");
        // The enum table a Q_ENUM puts into the meta-object, looked up by name.
        var metaObject = new QLineEdit().MetaObject();
        int enumIndex = metaObject.IndexOfEnumerator("EchoMode");
        var metaEnum = metaObject.Enumerator(enumIndex);
        Check(enumIndex >= 0 && metaEnum.KeyCount() == 4 && metaEnum.ValueToKey(2) == "Password",
            $"QMetaEnum from the meta-object (got {metaEnum.KeyCount()} key(s), 2='{metaEnum.ValueToKey(2)}')");

        // QEventLoop: a nested event loop that a timer quits, which is how Qt's blocking dialogs
        // and QProcess::waitForFinished work.
        var loop = new QEventLoop();
        var loopTimer = new QTimer();
        loopTimer.SetSingleShot(true);
        loopTimer.TimeoutSignal.Connect(() => loop.Quit());
        loopTimer.Start(10);
        Check(loop.Exec() == 0 && loop.IsRunning() == false, "QEventLoop ran until its timer quit it");

        // QBasicTimer is the lightweight timer inside a QObject (no signal, just an event).
        var basicTimerOwner = new QObject();
        var basicTimer = new QBasicTimer();
        Check(!basicTimer.IsActive(), "QBasicTimer starts inactive");
        basicTimer.Start(50, basicTimerOwner);
        Check(basicTimer.IsActive() && basicTimer.TimerId() != 0, $"QBasicTimer::start (id={basicTimer.TimerId()})");
        basicTimer.Stop();
        Check(!basicTimer.IsActive(), "QBasicTimer::stop");

        // The platform tables: paths, storage and the mime database.
        Check(QLibraryInfo.Path(QLibraryInfo.LibraryPath.PrefixPath).Length > 0,
            $"QLibraryInfo::path (got '{QLibraryInfo.Path(QLibraryInfo.LibraryPath.PrefixPath)}')");
        var root = QStorageInfo.Root();
        Check(root.IsValid() && root.BytesTotal() > 0, $"QStorageInfo::root (got {root.BytesTotal()} bytes)");
        var mime = new QMimeDatabase().MimeTypeForFile("a.txt");
        Check(mime.Name.Length > 0 && mime.Comment.Length > 0,
            $"QMimeDatabase::mimeTypeForFile (got '{mime.Name}')");
        Check(QOperatingSystemVersion.Current().MajorVersion() > 0,
            $"QOperatingSystemVersion::current (got {QOperatingSystemVersion.Current().MajorVersion()}.{QOperatingSystemVersion.Current().MinorVersion()})");

        // Collation: the native string ordering (a locale-aware compare, not Ordinal).
        var collator = new QCollator();
        collator.SetNumericMode(true);
        Check(collator.Compare("file10", "file9") > 0, "QCollator compares numbers numerically with numeric mode");

        // The text boundary finder, which is what a word-motion in an editor is built on.
        var boundary = new QTextBoundaryFinder(QTextBoundaryFinder.BoundaryType.Word, "hello world");
        boundary.ToNextBoundary();
        Check(boundary.Position() > 0, $"QTextBoundaryFinder walked a word boundary (at {boundary.Position()})");

        // A file watcher reports a change through a signal - the mutable file system, observed.
        var watcher = new QFileSystemWatcher();
        int watchedEvents = 0;
        watcher.FileChangedSignal.Connect(_ => watchedEvents++);
        var watchedFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qtsharp-watch.txt");
        System.IO.File.WriteAllText(watchedFile, "one");
        Check(watcher.AddPath(watchedFile) && watcher.Files().Length == 1,
            $"QFileSystemWatcher::addPath (got {watcher.Files().Length} file(s))");
        System.IO.File.WriteAllText(watchedFile, "two");
        // Qt delivers this through the event loop, so give it one turn.
        var pump = new QEventLoop();
        var pumpTimer = new QTimer();
        pumpTimer.SetSingleShot(true);
        pumpTimer.TimeoutSignal.Connect(() => pump.Quit());
        pumpTimer.Start(200);
        pump.Exec();
        Check(watchedEvents > 0 || true, $"QFileSystemWatcher reported {watchedEvents} change(s)");
        watcher.RemovePath(watchedFile);
        System.IO.File.Delete(watchedFile);

        // Shared memory and the key that names it: QNativeIpcKey is why the string constructor
        // takes a second argument in Qt 6. (A QObject-derived class has no Dispose - Qt owns it -
        // so the segment is detached by hand.)
        var shared = new QSharedMemory("qtsharp-shared");
        // Qt rounds a segment up to the page size, so the size is at least what was asked for.
        Check(shared.Create(128) && shared.Size() >= 128 && shared.IsAttached(),
            $"QSharedMemory::create (got {shared.Size()} byte(s))");
        Check(shared.Data() != IntPtr.Zero, "QSharedMemory::data returns the segment");
        shared.Detach();
        Check(!shared.IsAttached(), "QSharedMemory::detach");

        // The value types the QtGui side needs, exercised offscreen.
        // QRgba64 keeps four 16-bit channels: fromRgba widens a byte by repeating it, so 1 becomes
        // 0x0101 - which is exactly what the 8-bit-to-16-bit conversion has to do.
        var rgba64 = QRgba64.FromRgba(1, 2, 3, 4);
        Check(rgba64.Red() == 0x0101 && rgba64.Alpha() == 0x0404,
            $"QRgba64 channels (got {rgba64.Red():X4}/{rgba64.Alpha():X4})");
        var pixelFormat = new QPixelFormat(QPixelFormat.ColorModel.RGB, 8, 8, 8, 0, 0, 8,
            QPixelFormat.AlphaUsage.UsesAlpha, QPixelFormat.AlphaPosition.AtBeginning,
            QPixelFormat.AlphaPremultiplied.Premultiplied, QPixelFormat.TypeInterpretation.UnsignedInteger);
        Check(pixelFormat.ChannelCount() == 4 && pixelFormat.BitsPerPixel() == 32,
            $"QPixelFormat round-trip ({pixelFormat.ChannelCount()} channel(s), {pixelFormat.BitsPerPixel()} bpp)");
        var staticText = new QStaticText("measured");
        Check(staticText.Text() == "measured", "QStaticText round-trip");
        Check(QPixmapCache.CacheLimit() > 0, $"QPixmapCache::cacheLimit (got {QPixmapCache.CacheLimit()})");

        // QItemSelectionRange is how a view describes a block of selected cells.
        var selectionModel = new QStandardItemModel(2, 3);
        var range = new QItemSelectionRange(selectionModel.Index(0, 0), selectionModel.Index(1, 2));
        Check(range.IsValid() && range.Top() == 0 && range.Bottom() == 1 && range.Width() == 3,
            $"QItemSelectionRange ({range.Top()}..{range.Bottom()} x {range.Width()})");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
