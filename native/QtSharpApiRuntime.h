#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <type_traits>
#include <utility>
#include <QtCore/quuid.h>

// Shared contract between the generated native bindings and the hand-written runtime: the dispatch
// signature every generated subclass calls, and nothing else.
//
// The plain-old-data mirrors of Qt's value types (QsApiPointF, QsApiSize, QsApiRect, ...) are
// *generated* into each binding's own translation unit instead of living here: the table in
// gen/ApiModel.cs is their single source of truth, so every binding - the core and each extension
// module - carries exactly the ones its model has, and growing that table cannot leave a module
// compiling against a stale copy of this header.
typedef void (*QsApiDispatch)(void* userData, int methodId, void** argv);

// QUuid is not one of those model structs: it is erased to System.Guid, so the ABI mirror has to
// match the managed type instead of Qt's spelling. System.Guid is laid out as
// (int, short, short, byte[8]), little-endian fields - which is exactly QUuid's own layout on the
// target (data1, data2, data3, data4[8]) and the same fields the Win32 GUID uses. A Guid therefore
// crosses the boundary by value and is rebuilt as a QUuid here, with no allocation either way.
typedef struct QsApiGuid
{
    uint32_t data1;
    uint16_t data2;
    uint16_t data3;
    uint8_t  data4[8];
} QsApiGuid;

static inline QUuid QsApiGuidToQuuid(const QsApiGuid& guid)
{
    return QUuid(guid.data1, guid.data2, guid.data3,
                 guid.data4[0], guid.data4[1], guid.data4[2], guid.data4[3],
                 guid.data4[4], guid.data4[5], guid.data4[6], guid.data4[7]);
}

static inline QsApiGuid QsApiQuuidToGuid(const QUuid& uuid)
{
    return QsApiGuid{ uuid.data1, uuid.data2, uuid.data3,
                      { uuid.data4[0], uuid.data4[1], uuid.data4[2], uuid.data4[3],
                        uuid.data4[4], uuid.data4[5], uuid.data4[6], uuid.data4[7] } };
}

// A value type whose layout the POD mirrors but whose fields are private or not accessible as
// plain members (QModelIndex, QColor, QOperatingSystemVersion, ...): the two conversions are a bit
// copy. Valid exactly when the Qt type is standard-layout and trivially copyable (the pod probe
// checks this, and the POD's size is asserted next to its typedef in the binding's translation
// unit). The value is aliased rather than default-constructed because several of these types have
// a private default constructor; the copy out uses the public copy constructor.
template <class T, class Pod>
static inline T QsApiRawFromPod(const Pod& pod)
{
    static_assert(sizeof(T) == sizeof(Pod), "the POD must mirror the Qt value");
    static_assert(std::is_trivially_copyable<T>::value, "the raw conversion needs a trivially copyable value");
    return *reinterpret_cast<const T*>(&pod);
}

template <class Pod, class T>
static inline Pod QsApiRawToPod(const T& value)
{
    static_assert(sizeof(T) == sizeof(Pod), "the POD must mirror the Qt value");
    static_assert(std::is_trivially_copyable<T>::value, "the raw conversion needs a trivially copyable value");
    Pod pod;
    std::memcpy(&pod, &value, sizeof(Pod));
    return pod;
}

// Whether Qt provides an equality/hash for a value type. Both are asked in unevaluated contexts,
// so a deleted overload reads as "no" - qHash(QPointF) is deliberately deleted.
template <class T, class = void> struct QsApiHasEqual : std::false_type {};
template <class T>
struct QsApiHasEqual<T, std::void_t<decltype(std::declval<const T&>() == std::declval<const T&>())>>
    : std::true_type {};

template <class T, class = void> struct QsApiHasHash : std::false_type {};
template <class T>
struct QsApiHasHash<T, std::void_t<decltype(qHash(std::declval<const T&>(), std::declval<size_t>()))>>
    : std::true_type {};

// Equality is Qt's operator== when the type has one, and a comparison of the POD bytes when it
// does not (the generator normalizes the padding of the types that have any). This is the same
// rule for every value type: no hand-written comparison can disagree with Qt's.
template <class T, class Pod>
static inline bool QsApiValueEqual(const Pod& a, const Pod& b)
{
    if constexpr (QsApiHasEqual<T>::value)
        return QsApiRawFromPod<T>(a) == QsApiRawFromPod<T>(b);
    else
        return std::memcmp(&a, &b, sizeof(Pod)) == 0;
}

// Hashing is Qt's qHash when the type declares one. Otherwise the generator's fallback hashes the
// fields the type does have (a field-wise qHashMulti, or qHashBits over opaque words).
template <class T, class Pod, class Fallback>
static inline size_t QsApiValueHash(const Pod& pod, Fallback fallback)
{
    T value = QsApiRawFromPod<T>(pod);
    if constexpr (QsApiHasHash<T>::value)
        return qHash(value);
    else
        return fallback(value);
}
