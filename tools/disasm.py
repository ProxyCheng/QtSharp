import sys, struct
from capstone import *

def rva_to_off(data, rva):
    e_lfanew = struct.unpack_from('<I', data, 0x3C)[0]
    nsec = struct.unpack_from('<H', data, e_lfanew + 6)[0]
    opt = e_lfanew + 24
    size_opt = struct.unpack_from('<H', data, e_lfanew + 20)[0]
    sec = opt + size_opt
    for i in range(nsec):
        base = sec + i * 40
        name = data[base:base+8].rstrip(b'\x00').decode('latin1')
        va = struct.unpack_from('<I', data, base + 12)[0]
        vsize = struct.unpack_from('<I', data, base + 8)[0]
        praw = struct.unpack_from('<I', data, base + 20)[0]
        if va <= rva < va + max(vsize, 1):
            return praw + (rva - va), name, va
    return None, None, None

path = sys.argv[1]
rva = int(sys.argv[2], 16)
nbytes = int(sys.argv[3]) if len(sys.argv) > 3 else 96
back = int(sys.argv[4]) if len(sys.argv) > 4 else 64

data = open(path, 'rb').read()
off, name, va = rva_to_off(data, rva)
print(f"# {path} rva=0x{rva:X} -> section {name} va=0x{va:X} fileoff=0x{off:X}")
start = rva - back
code = data[rva_to_off(data, start)[0]: rva_to_off(data, start)[0] + back + nbytes]
md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = False
for insn in md.disasm(code, start):
    mark = '  <== CRASH RETURN' if insn.address == rva else ''
    print(f"0x{insn.address:X}: {insn.mnemonic:<8} {insn.op_str}{mark}")
