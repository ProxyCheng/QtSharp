using System;
using QtSharp.Runtime;

// QCoreApplication::postEvent takes ownership of the event: Qt delivers it and deletes it. The
// generated three-argument overload passes the pointer and leaves the wrapper owning a native
// object that Qt will free - a double delete waiting for the finalizer. This two-argument overload
// is the safe spelling: it posts and marks the wrapper dead, so neither Dispose nor the finalizer
// touches the object. The priority overload stays available for code that needs it.
namespace QtSharp.QtCore
{
    public partial class QCoreApplication
    {
        /// <summary>
        /// Posts <paramref name="event"/> and transfers it to Qt, which delivers it and then deletes
        /// it. The wrapper is invalid afterwards, so do not use it again.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="event"/> is null.</exception>
        public static void PostEvent(QObject receiver, QEvent @event)
        {
            if (@event is null)
                throw new ArgumentNullException(nameof(@event));

            PostEvent(receiver, @event, 0);
            ((IQtObjectView)@event).MarkDestroyed();
        }
    }
}
