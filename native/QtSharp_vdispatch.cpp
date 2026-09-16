// PoC #2: virtual dispatch from C++ into managed code using a stable function pointer.
//
// The point is that codegen emits a *real C++ subclass* (like shiboken does) whose
// overrides forward to a single managed entry point. The compiler owns the ABI, so
// return-by-value (QSize, QVariant) needs no sret/RAX knowledge on the managed side --
// the managed code only ever sees plain pointers. No per-object vtable patching and no
// Marshal.GetFunctionPointerForDelegate / delegate keep-alive either.

#include <qwidget.h>
#include <qabstractitemmodel.h>
#include <qvariant.h>
#include <qsize.h>
#include <qpixmap.h>
#include <qstring.h>

typedef void (*QsVirtualDispatch)(void* userData, int methodId, void** argv);

// Method ids (a product would generate these from the API model).
enum
{
    Method_SizeHint = 1,
    Method_PaintEvent = 2,
    Method_RowCount = 11,
    Method_ColumnCount = 12,
    Method_Data = 13,
};

// ---------------------------------------------------------------- widget host
class QsHostWidget : public QWidget
{
public:
    QsHostWidget(void* userData, QsVirtualDispatch dispatch)
        : m_userData(userData), m_dispatch(dispatch) {}

protected:
    QSize sizeHint() const override          // by-value return
    {
        QSize result;
        void* argv[] = { &result };
        m_dispatch(m_userData, Method_SizeHint, argv);
        return result;
    }

    void paintEvent(QPaintEvent* event) override   // void + pointer argument
    {
        void* argv[] = { nullptr, event };
        m_dispatch(m_userData, Method_PaintEvent, argv);
    }

private:
    void* m_userData;
    QsVirtualDispatch m_dispatch;
};

// ---------------------------------------------------------------- model host
class QsHostModel : public QAbstractItemModel
{
public:
    QsHostModel(void* userData, QsVirtualDispatch dispatch)
        : m_userData(userData), m_dispatch(dispatch) {}

    QModelIndex index(int row, int column, const QModelIndex& parent = QModelIndex()) const override
    {
        if (parent.isValid() || !hasIndex(row, column, parent))
            return QModelIndex();
        return createIndex(row, column);
    }

    QModelIndex parent(const QModelIndex&) const override { return QModelIndex(); }

    int rowCount(const QModelIndex& parent = QModelIndex()) const override
    {
        if (parent.isValid())
            return 0;
        int result = 0;
        void* argv[] = { &result };
        m_dispatch(m_userData, Method_RowCount, argv);
        return result;
    }

    int columnCount(const QModelIndex& parent = QModelIndex()) const override
    {
        if (parent.isValid())
            return 0;
        int result = 0;
        void* argv[] = { &result };
        m_dispatch(m_userData, Method_ColumnCount, argv);
        return result;
    }

    QVariant data(const QModelIndex& index, int role = Qt::DisplayRole) const override
    {
        QVariant result;                    // non-trivial by-value return
        int roleValue = role;
        void* argv[] = { &result, const_cast<QModelIndex*>(&index), &roleValue };
        m_dispatch(m_userData, Method_Data, argv);
        return result;
    }

private:
    void* m_userData;
    QsVirtualDispatch m_dispatch;
};

// ---------------------------------------------------------------- factories
extern "C" __declspec(dllexport) void* qs_vd_widget_create(void* userData, QsVirtualDispatch dispatch)
{
    return new QsHostWidget(userData, dispatch);
}

extern "C" __declspec(dllexport) void* qs_vd_model_create(void* userData, QsVirtualDispatch dispatch)
{
    return new QsHostModel(userData, dispatch);
}

// ------------------------------------------------- probes that cross the virtual
extern "C" __declspec(dllexport) void qs_vd_widget_size_hint(void* widget, int* width, int* height)
{
    const QSize size = static_cast<QWidget*>(widget)->sizeHint();
    *width = size.width();
    *height = size.height();
}

// QWidget::grab() renders the widget, which invokes paintEvent through the vtable.
extern "C" __declspec(dllexport) int qs_vd_widget_grab(void* widget)
{
    const QPixmap pixmap = static_cast<QWidget*>(widget)->grab();
    return pixmap.isNull() ? 0 : 1;
}

extern "C" __declspec(dllexport) int qs_vd_model_row_count(void* model)
{
    return static_cast<QAbstractItemModel*>(model)->rowCount();
}

// QAbstractItemModel::match() lives in the Qt library and walks indexes calling the
// virtual data() itself - i.e. a real cross-module virtual call with a QVariant return.
extern "C" __declspec(dllexport) int qs_vd_model_match_display(void* model, const char* value)
{
    auto* abstractModel = static_cast<QAbstractItemModel*>(model);
    const QModelIndex start = abstractModel->index(0, 0);
    const QModelIndexList hits = abstractModel->match(
        start, Qt::DisplayRole, QString::fromUtf8(value), 1, Qt::MatchExactly);
    return int(hits.size());
}

// ------------------------------------------------- value writers (PoC plumbing)
extern "C" __declspec(dllexport) void qs_vd_qsize_set(void* size, int width, int height)
{
    auto* value = static_cast<QSize*>(size);
    value->setWidth(width);
    value->setHeight(height);
}

extern "C" __declspec(dllexport) void qs_vd_qvariant_assign(void* destination, const void* source)
{
    *static_cast<QVariant*>(destination) = *static_cast<const QVariant*>(source);
}
