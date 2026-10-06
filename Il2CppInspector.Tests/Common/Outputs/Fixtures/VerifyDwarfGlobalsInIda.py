import json

import ida_name
import ida_nalt
import ida_pro
import ida_typeinf
import idc


with open(idc.ARGV[1], encoding="utf-8") as source:
    expected = json.load(source)["globals"]

failures = []
probes = []
probe_kinds = set()
for item in expected:
    address = item["address"]
    info = ida_typeinf.tinfo_t()
    reasons = []
    if not ida_nalt.get_tinfo(info, address):
        reasons.append("missing type")
    else:
        if info.get_size() != item["size"]:
            reasons.append("size")
        if info.is_array() != bool(item["count"]):
            reasons.append("array kind")
        if item["count"]:
            array = ida_typeinf.array_type_data_t()
            if not info.get_array_details(array) or array.nelems != item["count"]:
                reasons.append("array length")
            else:
                info = array.elem_type
        if info.is_ptr() != item["pointer"]:
            reasons.append("pointer kind")
        if item["pointer"]:
            info = info.get_pointed_object()
        if info.get_type_name() != item["type"] and str(info) not in (item["type"], "struct " + item["type"]):
            reasons.append("base type: " + str(info))
        if item["fields"]:
            details = ida_typeinf.udt_type_data_t()
            if not info.get_udt_details(details):
                reasons.append("missing structure")
            else:
                actual = {field.name: field.offset // 8 for field in details}
                if any(actual.get(field["name"]) != field["offset"] for field in item["fields"]):
                    reasons.append("structure field offsets")
    if reasons:
        failures.append({"address": hex(address), "expected": item["name"], "type": idc.get_type(address), "reasons": reasons})
    kind = item["type"] if not item["pointer"] else item["type"].rsplit("__Class", 1)[-1]
    if kind not in probe_kinds and len(probes) < 12:
        probe_kinds.add(kind)
        probes.append({"address": hex(address), "name": ida_name.get_name(address), "type": idc.get_type(address)})

print("DWARF_GLOBAL_IDA_RESULT=" + json.dumps({"checked": len(expected), "failed": len(failures), "failures": failures[:20], "probes": probes}))
ida_pro.qexit(0)
