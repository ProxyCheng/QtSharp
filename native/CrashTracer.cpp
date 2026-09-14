#include <windows.h>
#include <dbghelp.h>
#include <stdio.h>

static LONG CALLBACK CrashHandler(PEXCEPTION_POINTERS ep);
static volatile LONG g_reported = 0;

extern "C" __declspec(dllexport) void InstallCrashTracer()
{
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

    if (InterlockedExchange(&g_reported, 1) != 0)
        return EXCEPTION_CONTINUE_SEARCH;

    HANDLE hProcess = GetCurrentProcess();
    SymSetOptions(SYMOPT_UNDNAME | SYMOPT_DEFERRED_LOADS | SYMOPT_LOAD_LINES);
    SymInitialize(hProcess, NULL, TRUE);

    FILE* f = fopen("D:\\QtSharp\\crash_stack.txt", "w");
    if (!f) return EXCEPTION_CONTINUE_SEARCH;

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

    MEMORY_BASIC_INFORMATION mbi;
    if (VirtualQuery((LPCVOID)c->Rcx, &mbi, sizeof(mbi)) &&
        mbi.State == MEM_COMMIT &&
        !(mbi.Protect & (PAGE_NOACCESS | PAGE_GUARD)))
    {
        unsigned long long* q = (unsigned long long*)(c->Rcx - 16);
        for (int i = 0; i < 6; i++)
            fprintf(f, "  [RCX%+d] 0x%p\n", (int)(i * 8 - 16), (void*)q[i]);
    }

    Resolve(f, 0, ep->ContextRecord->Rip, hProcess);

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
