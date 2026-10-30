using System;

// The QIODevice bridge: a C# Stream behind Qt's QIODevice.
//
// Qt's IO stack takes a QIODevice*, so a managed implementation has to be a QIODevice subclass
// whose readData/writeData hooks are overridable from C#. Two things live here:
//
//   * readData/writeData/readLineData hand the implementation a raw byte buffer. The model keeps
//     the C++ spelling (the virtual override must repeat Qt's declaration exactly), but the
//     parameter's model type becomes void* so managed code receives an IntPtr it can copy into or
//     read from. Without this the buffer has no model type and the virtual would be inexpressible;
//   * the override points must not propagate to QIODevice's subclasses (see
//     PropagatesToSubclasses).
[VirtualBridge]
internal sealed class QioDeviceBridge : IVirtualBridge
{
    public bool AppliesTo(string className) => className == "QIODevice";

    // The hooks are declared again by QBuffer, QFileDevice, QAbstractSocket, QProcess, ... with
    // the same `const char *` + length shape. Those declarations must cross as byte buffers too:
    // a string there round-trips through UTF-8 and silently corrupts any non-UTF-8 byte (0xFA
    // became 0xEF in a QBuffer write). They still do not get a native override of their own -
    // PropagatesToSubclasses below stays false.
    public bool MapsAllDeclaringClasses => true;

    // Subclasses (QFile, QBuffer, QLocalSocket, ...) call their own open()/readData()-style
    // members straight into Qt; a native override on them would dispatch back into the managed
    // call that is already running - infinite recursion, found by test-network.
    public bool PropagatesToSubclasses => false;

    public void MapParameters(ApiModel.FnFact fn, string declaringClass)
    {
        if (!fn.IsVirtual)
            return;
        if (fn.Name != "readData" && fn.Name != "writeData" && fn.Name != "readLineData")
            return;
        foreach (var p in fn.Params)
        {
            // A writable `char *` has no ModelName (it is not the NUL-terminated string), so the
            // C++ spelling is what identifies the buffer.
            if (p.Type == "char*" || IsCharPointerSpelling(p.CppType))
            {
                p.Type = "void*";
                p.Mode = ApiModel.ParamMode.In;
                p.Buffer = true;
            }
        }
    }

    static bool IsCharPointerSpelling(string cpp)
    {
        if (cpp == null)
            return false;
        string compact = cpp.Replace(" ", string.Empty);
        return compact == "char*" || compact == "constchar*";
    }
}
