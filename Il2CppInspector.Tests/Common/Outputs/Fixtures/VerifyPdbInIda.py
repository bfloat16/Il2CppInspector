import json

import ida_name
import ida_nalt
import ida_pro
import ida_typeinf
import idc

try:
    flags = ida_typeinf.tinfo_t()
    if not flags.get_named_type(None, "Flags"):
        raise RuntimeError("PDB bitfield type missing")
    details = ida_typeinf.udt_type_data_t()
    flags.get_udt_details(details)
    enums = []
    for name in ["ByteMode", "IntMode"]:
        info = ida_typeinf.tinfo_t()
        if not info.get_named_type(None, name):
            raise RuntimeError("PDB enum missing: " + name)
        enums.append(info.get_size())
    sizes, types = [], []
    for ea in [0x140002010, 0x140002020, 0x140002030]:
        info = ida_typeinf.tinfo_t()
        if not ida_nalt.get_tinfo(info, ea):
            raise RuntimeError("PDB global type missing: " + hex(ea))
        sizes.append(info.get_size())
        types.append(str(info))
    print("PDB_IDA_RESULT=" + json.dumps({"functionName": ida_name.get_name(0x140001020), "functionType": idc.get_type(0x140001020),
          "flagOffsets": [m.offset for m in details], "enumWidths": enums, "globalSizes": sizes, "globalTypes": types}))
finally:
    ida_pro.qexit(0)
