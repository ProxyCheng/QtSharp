#include <windows.h>
#include <dbghelp.h>
#include <psapi.h>
#include <stdio.h>

static LONG CALLBACK CrashHandler(PEXCEPTION_POINTERS ep);
static volatile LONG g_reported = 0;

// The module list, taken once at install time. A frame is then arithmetic — which module, and how
// far into it — with no symbol handler and no loader lock, which is the whole point: dbghelp takes
// the loader lock, and the thread that faulted may be the one holding it, in which case a handler
// that resolves symbols hangs and prints nothing below the fault.
struct ModuleEntry { DWORD64 Base, End; char Name[64]; };
static ModuleEntry g_modules[256];
static int g_moduleCount = 0;

static void CaptureModules()
{
    HMODULE mods[256];
    DWORD needed = 0;
    if (!K32EnumProcessModules(GetCurrentProcess(), mods, sizeof(mods), &needed)) return;
    int count = (int)(needed / sizeof(HMODULE));
    if (count > 256) count = 256;
    for (int i = 0; i < count; i++)
    {
        MODULEINFO info;
        char path[MAX_PATH];
        if (!K32GetModuleInformation(GetCurrentProcess(), mods[i], &info, sizeof(info))) continue;
        if (!GetModuleFileNameA(mods[i], path, MAX_PATH)) continue;
        const char* base = strrchr(path, '\\');
        base = base ? base + 1 : path;
        strncpy_s(g_modules[g_moduleCount].Name, base, _TRUNCATE);
        g_modules[g_moduleCount].Base = (DWORD64)info.lpBaseOfDll;
        g_modules[g_moduleCount].End = (DWORD64)info.lpBaseOfDll + info.SizeOfImage;
        g_moduleCount++;
    }
}

static const ModuleEntry* ModuleOf(DWORD64 addr)
{
    for (int i = 0; i < g_moduleCount; i++)
        if (addr >= g_modules[i].Base && addr < g_modules[i].End) return &g_modules[i];
    return NULL;
}

// One frame, as module+offset — the form a MAP file resolves to a function.
static void Frame(FILE* f, const char* label, int idx, DWORD64 addr)
{
    const ModuleEntry* module = ModuleOf(addr);
    if (module)
        fprintf(f, "%s[%02d] %s+0x%llX\n", label, idx, module->Name, (unsigned long long)(addr - module->Base));
    else
        fprintf(f, "%s[%02d] 0x%p\n", label, idx, (void*)addr);
}

extern "C" __declspec(dllexport) void InstallCrashTracer()
{
    // Symbols are loaded here, at install time, not in the handler: SymInitialize takes the loader
    // lock, and a thread that faults while holding it would deadlock inside the handler instead of
    // printing anything.
    SymSetOptions(SYMOPT_UNDNAME | SYMOPT_DEFERRED_LOADS | SYMOPT_LOAD_LINES);
    SymInitialize(GetCurrentProcess(), NULL, TRUE);
    CaptureModules();
    AddVectoredExceptionHandler(1, CrashHandler);
}

static void Resolve(FILE* f, int idx, DWORD64 addr, HANDLE hProcess)
{
    if (!addr) { fprintf(f, "[%02d] 0x0\n", idx); return; }

    HMODULE hmod = NULL;
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                       GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       (LPCSTR)addr, &hmod);

    char full[MAX_PATH] = "?";
    if (hmod) GetModuleFileNameA(hmod, full, MAX_PATH);
    const char* base = strrchr(full, '\\');
    base = base ? base + 1 : full;
    DWORD64 off = hmod ? (addr - (DWORD64)hmod) : 0;

    char symbuf[sizeof(SYMBOL_INFO) + 512];
    SYMBOL_INFO* sym = (SYMBOL_INFO*)symbuf;
    ZeroMemory(sym, sizeof(symbuf));
    sym->SizeOfStruct = sizeof(SYMBOL_INFO);
    sym->MaxNameLen = 500;

    DWORD64 disp = 0;
    const char* sname = "";
    if (SymFromAddr(hProcess, addr, &disp, sym))
        sname = sym->Name;

    fprintf(f, "[%02d] 0x%p  %s+0x%llX  %s+0x%llX\n", idx, (void*)addr, base,
            (unsigned long long)off, sname, (unsigned long long)disp);
    fflush(f);
}

static LONG CALLBACK CrashHandler(PEXCEPTION_POINTERS ep)
{
    DWORD code = ep->ExceptionRecord->ExceptionCode;
    if (code != EXCEPTION_ACCESS_VIOLATION &&
        code != EXCEPTION_ILLEGAL_INSTRUCTION &&
        code != EXCEPTION_PRIV_INSTRUCTION &&
        code != 0xC000041D)
        return EXCEPTION_CONTINUE_SEARCH;

    if (code == EXCEPTION_ACCESS_VIOLATION)
    {
        // A stack that grows past a guard page faults too, and Windows fixes it up on the spot:
        // reporting that one as the crash buries the one that mattered.
        MEMORY_BASIC_INFORMATION mbi;
        if (VirtualQuery((LPCVOID)ep->ExceptionRecord->ExceptionInformation[1], &mbi, sizeof(mbi)) &&
            (mbi.Protect & PAGE_GUARD))
            return EXCEPTION_CONTINUE_SEARCH;
    }

    if (InterlockedExchange(&g_reported, 1) != 0)
        return EXCEPTION_CONTINUE_SEARCH;

    HANDLE hProcess = GetCurrentProcess();

    FILE* f = fopen("D:\\QtSharp\\crash_stack.txt", "w");
    if (!f) return EXCEPTION_CONTINUE_SEARCH;
    // Unbuffered: the exception may be handled and the process may carry on, and then nothing ever
    // flushes what was written before the first frame was resolved. An empty file says less than
    // nothing.
    setvbuf(f, NULL, _IONBF, 0);

    fprintf(f, "ExceptionCode=0x%08X RIP=0x%p\n", code,
            (void*)ep->ContextRecord->Rip);
    if (code == EXCEPTION_ACCESS_VIOLATION)
        fprintf(f, "AccessType=%llu FaultAddress=0x%p\n",
                ep->ExceptionRecord->ExceptionInformation[0],
                (void*)ep->ExceptionRecord->ExceptionInformation[1]);

    CONTEXT* c = ep->ContextRecord;
    fprintf(f, "REG RAX=%p RBX=%p RCX=%p RDX=%p\n", (void*)c->Rax, (void*)c->Rbx,
            (void*)c->Rcx, (void*)c->Rdx);
    fprintf(f, "REG RSI=%p RDI=%p RBP=%p RSP=%p\n", (void*)c->Rsi, (void*)c->Rdi,
            (void*)c->Rbp, (void*)c->Rsp);
    fprintf(f, "REG R8=%p R9=%p R10=%p R11=%p R12=%p R13=%p R14=%p R15=%p\n",
            (void*)c->R8, (void*)c->R9, (void*)c->R10, (void*)c->R11,
            (void*)c->R12, (void*)c->R13, (void*)c->R14, (void*)c->R15);

    fprintf(f, "CODE:");
    for (int i = 0; i < 24; i++)
        fprintf(f, " %02X", ((unsigned char*)c->Rip)[i]);
    fprintf(f, "\n");
    fprintf(f, "MARK:before-rcx-dump\n");

    MEMORY_BASIC_INFORMATION mbi;
    if (VirtualQuery((LPCVOID)c->Rcx, &mbi, sizeof(mbi)) &&
        mbi.State == MEM_COMMIT &&
        !(mbi.Protect & (PAGE_NOACCESS | PAGE_GUARD)))
    {
        unsigned long long* q = (unsigned long long*)(c->Rcx - 16);
        for (int i = 0; i < 6; i++)
            fprintf(f, "  [RCX%+d] 0x%p\n", (int)(i * 8 - 16), (void*)q[i]);
    }

    fprintf(f, "MARK:after-rcx-dump\n");
    Frame(f, "FAULT ", 0, ep->ContextRecord->Rip);

    // No StackWalk64 here: it needs the symbol handler, which is what hangs. Scanning the stack for
    // values that point into a module gives candidates rather than a chain, and each one is a
    // module+offset the MAP file resolves — enough to read the call path off.
    DWORD64* stack = (DWORD64*)c->Rsp;
    MEMORY_BASIC_INFORMATION page;
    for (int i = 0; i < 384; i++)
    {
        if (!VirtualQuery(&stack[i], &page, sizeof(page)) || page.State != MEM_COMMIT ||
            (page.Protect & (PAGE_NOACCESS | PAGE_GUARD)) ||
            !(page.Protect & (PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY |
                              PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)))
            break;
        DWORD64 value = stack[i];
        if (ModuleOf(value)) Frame(f, "STACK ", i, value);
    }

    CONTEXT ctx = *ep->ContextRecord;
    STACKFRAME64 frame;
    ZeroMemory(&frame, sizeof(frame));
    frame.AddrPC.Offset    = ctx.Rip; frame.AddrPC.Mode    = AddrModeFlat;
    frame.AddrFrame.Offset = ctx.Rbp; frame.AddrFrame.Mode = AddrModeFlat;
    frame.AddrStack.Offset = ctx.Rsp; frame.AddrStack.Mode = AddrModeFlat;

    for (int i = 1; i < 80; i++)
    {
        if (!StackWalk64(IMAGE_FILE_MACHINE_AMD64, hProcess, GetCurrentThread(),
                         &frame, &ctx, NULL, SymFunctionTableAccess64,
                         SymGetModuleBase64, NULL))
            break;
        if (frame.AddrPC.Offset == 0)
            break;
        Resolve(f, i, frame.AddrPC.Offset, hProcess);
    }

    fprintf(f, "---END---\n");
    fclose(f);
    return EXCEPTION_CONTINUE_SEARCH;
}
