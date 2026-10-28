using System.Text;
using QtSharp.Runtime;

namespace QtSharp.QtCore
{
    // The template member the generator cannot publish: FromType<T> is the typed spelling of
    // FromName, and the C# -> Qt type name comes from the one canonical mapper (MetaType) so a
    // QMetaType asks for exactly the name a [QtSignal]/[QtProperty] declares.
    public partial struct QMetaType
    {
        /// <summary>
        /// The metatype Qt registered for <typeparamref name="T"/>, looked up by the name
        /// <see cref="MetaType.QtName"/> gives it. An unsupported C# type throws there instead of
        /// resolving to an unrelated metatype; a type Qt itself does not know (a QObject subclass
        /// declared in C#, say) gives an invalid QMetaType.
        /// </summary>
        public static QMetaType FromType<T>() => FromName(Encoding.UTF8.GetBytes(MetaType.QtName(typeof(T))));
    }
}
