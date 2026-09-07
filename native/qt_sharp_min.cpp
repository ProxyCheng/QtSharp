#include <QApplication>
#include <QWidget>
#include <QTimer>

extern "C" __declspec(dllexport) void* qtsharp_create_application()
{
    static int argc = 1;
    static char arg0[] = "qtsharp";
    static char* argv[] = { arg0, nullptr };
    return new QApplication(argc, argv);
}

extern "C" __declspec(dllexport) void* qtsharp_create_widget()
{
    return new QWidget();
}

extern "C" __declspec(dllexport) void qtsharp_widget_show(void* w)
{
    static_cast<QWidget*>(w)->show();
}

extern "C" __declspec(dllexport) void qtsharp_widget_set_title(void* w, const char* title)
{
    static_cast<QWidget*>(w)->setWindowTitle(QString::fromUtf8(title));
}

extern "C" __declspec(dllexport) void qtsharp_widget_resize(void* w, int width, int height)
{
    static_cast<QWidget*>(w)->resize(width, height);
}

extern "C" __declspec(dllexport) int qtsharp_application_exec(void* app)
{
    QTimer::singleShot(1500, []() { QCoreApplication::quit(); });
    return static_cast<QApplication*>(app)->exec();
}

extern "C" __declspec(dllexport) void qtsharp_delete_qobject(void* o)
{
    delete static_cast<QObject*>(o);
}
