using System;
using System.Runtime.InteropServices;
using System.Security;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

// A few public wrappers used by the official examples were pruned from the generated
// bindings. The underlying P/Invoke declarations still exist inside the same assembly,
// so they are re-exposed here instead of regenerating the bindings.
public static class QtCompat
{
    public static void SetForeground(this QTextCharFormat format, QColor color)
    {
        using var brush = new QBrush(color);
        QTextFormat.__Internal.SetForeground(format.__Instance, brush.__Instance);
    }

    // --- Shortcut editor support -------------------------------------------------
    // The generator cannot emit signals whose payload contains QModelIndex/QList<int>,
    // nor the QString based QKeySequence conversions, so these small helpers bridge them.

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QAbstractItemModel_emit_dataChanged", CallingConvention = CallingConvention.Cdecl)]
    private static extern void EmitDataChangedNative(IntPtr model, IntPtr topLeft, IntPtr bottomRight);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QAction_setShortcut_string", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetActionShortcutNative(IntPtr action, IntPtr utf16);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QAction_shortcut_string", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetActionShortcutNative(IntPtr action);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QKeySequenceEdit_setKeySequence_string", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetKeySequenceStringNative(IntPtr editor, IntPtr utf16);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QKeySequenceEdit_keySequence_string", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetKeySequenceStringNative(IntPtr editor);

    public static void EmitDataChanged(this QAbstractItemModel model, QModelIndex topLeft, QModelIndex bottomRight)
    {
        EmitDataChangedNative(model.__Instance, topLeft.__Instance, bottomRight.__Instance);
    }

    public static void SetShortcutString(this QAction action, string shortcut)
    {
        IntPtr utf16 = Marshal.StringToCoTaskMemUni(shortcut ?? string.Empty);
        try { SetActionShortcutNative(action.__Instance, utf16); }
        finally { Marshal.FreeCoTaskMem(utf16); }
    }

    public static string ShortcutString(this QAction action)
    {
        IntPtr qs = GetActionShortcutNative(action.__Instance);
        try { return QtSharp.QStringMarshaller.ToManaged(qs); }
        finally { QtSharp.QStringMarshaller.Free(qs); }
    }

    public static void SetKeySequenceString(this QKeySequenceEdit editor, string keySequence)
    {
        IntPtr utf16 = Marshal.StringToCoTaskMemUni(keySequence ?? string.Empty);
        try { SetKeySequenceStringNative(editor.__Instance, utf16); }
        finally { Marshal.FreeCoTaskMem(utf16); }
    }

    public static string KeySequenceString(this QKeySequenceEdit editor)
    {
        IntPtr qs = GetKeySequenceStringNative(editor.__Instance);
        try { return QtSharp.QStringMarshaller.ToManaged(qs); }
        finally { QtSharp.QStringMarshaller.Free(qs); }
    }

    // --- QSettings (keys are QAnyStringView in Qt 6) -----------------------------

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QSettings_beginGroup_string", CallingConvention = CallingConvention.Cdecl)]
    private static extern void QSettingsBeginGroup(IntPtr settings, IntPtr utf16);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QSettings_setValue_string", CallingConvention = CallingConvention.Cdecl)]
    private static extern void QSettingsSetValue(IntPtr settings, IntPtr utf16, IntPtr variant);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QSettings_value_string", CallingConvention = CallingConvention.Cdecl)]
    private static extern void QSettingsValue(IntPtr settings, IntPtr utf16, IntPtr outVariant);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QSettings_contains_string", CallingConvention = CallingConvention.Cdecl)]
    private static extern int QSettingsContains(IntPtr settings, IntPtr utf16);

    public static void SettingsBeginGroup(this QSettings settings, string key)
    {
        IntPtr utf16 = Marshal.StringToCoTaskMemUni(key ?? string.Empty);
        try { QSettingsBeginGroup(settings.__Instance, utf16); }
        finally { Marshal.FreeCoTaskMem(utf16); }
    }

    public static void SettingsSetValue(this QSettings settings, string key, QVariant value)
    {
        IntPtr utf16 = Marshal.StringToCoTaskMemUni(key ?? string.Empty);
        try { QSettingsSetValue(settings.__Instance, utf16, value.__Instance); }
        finally { Marshal.FreeCoTaskMem(utf16); }
    }

    public static QVariant SettingsValue(this QSettings settings, string key)
    {
        var result = new QVariant();
        IntPtr utf16 = Marshal.StringToCoTaskMemUni(key ?? string.Empty);
        try { QSettingsValue(settings.__Instance, utf16, result.__Instance); }
        finally { Marshal.FreeCoTaskMem(utf16); }
        return result;
    }

    public static bool SettingsContains(this QSettings settings, string key)
    {
        IntPtr utf16 = Marshal.StringToCoTaskMemUni(key ?? string.Empty);
        try { return QSettingsContains(settings.__Instance, utf16) != 0; }
        finally { Marshal.FreeCoTaskMem(utf16); }
    }

    // --- QPixmap value factories (generated sret wrappers corrupt shared data) -----

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QPixmap_fromImage", CallingConvention = CallingConvention.Cdecl)]
    private static extern void PixmapFromImageNative(IntPtr outPixmap, IntPtr image);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QPixmap_scaled", CallingConvention = CallingConvention.Cdecl)]
    private static extern void PixmapScaledNative(IntPtr outPixmap, IntPtr source, int width, int height, int aspectMode, int transformMode);

    public static QPixmap PixmapFromImage(QImage image)
    {
        var result = new QPixmap();
        PixmapFromImageNative(result.__Instance, image.__Instance);
        return result;
    }

    public static QPixmap PixmapScaled(QPixmap source, int width, int height,
        Qt.AspectRatioMode aspectMode, Qt.TransformationMode transformMode)
    {
        var result = new QPixmap();
        PixmapScaledNative(result.__Instance, source.__Instance, width, height, (int)aspectMode, (int)transformMode);
        return result;
    }
}
