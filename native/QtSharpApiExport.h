#pragma once

// The visibility of the C ABI the generator emits and the hand-written runtime implements.
//
// Windows exports with __declspec(dllexport); GCC/Clang builds the shared object with
// -fvisibility=hidden so that only these functions - not the tens of thousands of C++ symbols the
// Qt headers and the generated subclasses bring in - end up in the dynamic symbol table.
#if defined(_WIN32) || defined(__CYGWIN__)
#  define QS_API_EXPORT __declspec(dllexport)
#else
#  define QS_API_EXPORT __attribute__((visibility("default")))
#endif
