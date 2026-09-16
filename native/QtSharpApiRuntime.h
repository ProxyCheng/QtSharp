#pragma once

// Shared contract between the generated native bindings and the hand-written runtime.
typedef void (*QsApiDispatch)(void* userData, int methodId, void** argv);
