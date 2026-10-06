import json

import ida_auto
import ida_funcs
import ida_name
import ida_nalt
import ida_pro
import ida_typeinf
import idc


ida_auto.auto_wait()
library = ida_typeinf.get_idati()
types = []
type_count = 0
for info in library.named_types():
    type_count += 1
    if len(types) < 100:
        types.append(info.get_type_name())

functions = []
for address in (0x140001000, 0x140001020, 0x140001030, 0x140001040, 0x140001050):
    functions.append({"address": hex(address), "name": ida_name.get_name(address), "type": idc.get_type(address)})

vector = ida_typeinf.tinfo_t()
vector_size = vector.get_size() if vector.get_named_type(None, "Vector3") else None
owner = ida_typeinf.tinfo_t()
owner_size = owner.get_size() if owner.get_named_type(None, "RecursiveOwner") else None
union = ida_typeinf.tinfo_t()
union_size = union.get_size() if union.get_named_type(None, "RecursiveValue") else None
globals = []
for address in (0x140002000, 0x140002020, 0x140002040, 0x140002081, 0x140003000):
    info = ida_typeinf.tinfo_t()
    found = ida_nalt.get_tinfo(info, address)
    globals.append({"address": hex(address), "name": ida_name.get_name(address), "type": idc.get_type(address),
                    "size": info.get_size() if found else None, "is_struct": found and info.is_struct(),
                    "is_ptr": found and info.is_ptr(), "is_array": found and info.is_array()})
print("DWARF_IDA_RESULT=" + json.dumps({"functions": functions, "globals": globals, "types": types, "type_count": type_count, "function_count": ida_funcs.get_func_qty(), "vector_size": vector_size, "recursive_owner_size": owner_size, "recursive_union_size": union_size}))
ida_pro.qexit(0)
