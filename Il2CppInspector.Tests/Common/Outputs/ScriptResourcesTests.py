import ast
import itertools
from pathlib import Path
import types
import unittest
from unittest.mock import Mock


ROOT = Path(__file__).resolve().parents[3]
SCRIPTS = ROOT / "Il2CppInspector.Common/Outputs/ScriptResources"
shared = (SCRIPTS / "shared_base.py").read_text(encoding="utf-8-sig")
processor = (ROOT / "Il2CppInspector.Plugin/ZZZ/Outputs/RuntimeCaches.py").read_text()
shared = shared.replace("        # %GAME_METADATA_PROCESSOR%", processor)
BASE = {}
exec(compile(shared, "shared_base.py", "exec"), BASE)


class Backend:
    def __init__(self, enabled, fake):
        self.apply_structures = enabled
        self.supports_fake_string_segment = fake
        self.calls = []

    def __getattr__(self, name):
        def record(*args):
            self.calls.append((name, args))
            if name == "create_fake_segment":
                return 0x8000
            if name == "write_string":
                return len(args[1]) + 1
        return record


def metadata(legacy=False):
    function = {
        "virtualAddress": "0x1000", "name": "method", "signature": "void method()",
        "dotNetSignature": "Method()", "group": "Assembly/Type",
    }
    field = {
        "virtualAddress": "0x2000", "name": "field", "type": "struct Type *",
        "dotNetType": "Type", "value": "value", "count": 2,
    }
    literal = {"name": "literal", "string": "text"}
    literal.update({"ordinal": 0} if legacy else {"virtualAddress": "0x3000"})
    result = {name: [function.copy()] for name in (
        "methodDefinitions", "constructedGenericMethods", "customAttributesGenerators",
        "methodInvokers", "functionMetadata", "apis",
    )}
    result.update({name: [field.copy()] for name in (
        "typeInfoPointers", "typeRefPointers", "typeMetadata", "arrayMetadata",
        "fields", "fieldRvas", "moraxRuntimeCaches",
    )})
    result["methodInfoPointers"] = [{**function, "methodAddress": "0x1010"}]
    result["functionAddresses"] = ["0x1000", "0x1010"]
    result["stringLiterals"] = [literal]
    return result


def target_classes(name, header_path="il2cpp.h", **globals):
    source = (SCRIPTS / "Targets" / f"{name}.py").read_text(encoding="utf-8-sig")
    tree = ast.parse(source.replace("%TYPE_HEADER_RELATIVE_PATH%", header_path))
    body = [ast.ImportFrom(module="__future__", names=[ast.alias(name="annotations")], level=0)]
    body += [node for node in tree.body if isinstance(node, ast.ClassDef) and node.name.endswith(("DisassemblerInterface", "StatusHandler"))]
    namespace = {**BASE, "itertools": itertools, **globals}
    exec(compile(ast.fix_missing_locations(ast.Module(body=body, type_ignores=[])), name, "exec"), namespace)
    return namespace


class ScriptResourcesTests(unittest.TestCase):
    def test_empty_debug_symbol_arrays_preserve_data_application(self):
        data = metadata()
        for key in ("methodDefinitions", "constructedGenericMethods", "customAttributesGenerators", "methodInvokers", "functionAddresses", "functionMetadata", "apis"):
            data[key] = []
        for fake in (False, True):
            with self.subTest(fake=fake):
                backend = Backend(True, fake)
                context = BASE["ScriptContext"](backend, Mock(spec=BASE["BaseStatusHandler"]))
                context.process_metadata(data)
                calls = {name for name, _ in backend.calls}
                self.assertFalse(calls & {"define_function", "set_function_name", "set_function_type", "set_function_comment"})
                self.assertTrue({"set_data_name", "set_data_comment", "add_cross_reference", "set_data_type", "define_data_array"} <= calls)

    def test_generated_target_templates_compile(self):
        for path in (SCRIPTS / "Targets").glob("*.py"):
            with self.subTest(target=path.stem):
                compile(shared + "\n" + path.read_text(encoding="utf-8-sig"), path.name, "exec")

    def test_disabled_types_preserve_annotations_in_all_string_modes(self):
        typed = {"set_data_type", "set_function_type", "cache_function_types", "define_data_array", "import_c_typedef"}
        for legacy, fake in itertools.product((False, True), repeat=2):
            with self.subTest(legacy=legacy, fake=fake):
                backend = Backend(False, fake)
                BASE["ScriptContext"](backend, Mock(spec=BASE["BaseStatusHandler"])).process_metadata(metadata(legacy))
                calls = {name for name, _ in backend.calls}
                self.assertFalse(calls & typed)
                self.assertTrue({"define_function", "set_function_name", "set_function_comment", "set_data_name", "set_data_comment", "add_cross_reference"} <= calls)

    def test_enabled_types_and_incomplete_signatures(self):
        self.assertTrue(BASE["BaseDisassemblerInterface"].apply_structures)
        self.assertTrue(BASE["BaseDisassemblerInterface"].import_type_header)
        backend = Backend(True, False)
        backend.import_type_header = False
        context = BASE["ScriptContext"](backend, Mock(spec=BASE["BaseStatusHandler"]))
        context.process_metadata(metadata())
        calls = {name for name, _ in backend.calls}
        self.assertTrue({"set_data_type", "set_function_type", "cache_function_types", "define_data_array"} <= calls)
        self.assertTrue({"define_function", "set_function_name", "set_function_comment", "add_cross_reference"} <= calls)
        backend.calls.clear()
        context.define_il_method({**metadata()["methodDefinitions"][0], "signatureComplete": False})
        self.assertNotIn("set_function_type", {name for name, _ in backend.calls})
        context.process_metadata(metadata(legacy=True))
        self.assertIn("import_c_typedef", {name for name, _ in backend.calls})

    def test_ida_disabled_does_not_touch_type_libraries_or_parse_header(self):
        ida = Mock(INFFL_AUTO=1, DEMNAM_GCC3=1, DEMNAM_NAME=2)
        ida.inf_get_genflags.return_value = 1
        typeinfo, clang, segment = Mock(), Mock(), Mock()
        segment.get_segm_by_name.return_value = None
        namespace = target_classes("IDA", ida_ida=ida, ida_typeinf=typeinfo, ida_srclang=clang,
                                   ida_segment=segment, IDACLANG_AVAILABLE=True, FOLDERS_AVAILABLE=False)
        backend = namespace["IDADisassemblerInterface"](Mock(spec=BASE["BaseStatusHandler"]))
        backend.apply_structures = False
        backend.on_start()
        typeinfo.del_til.assert_not_called()
        typeinfo.idc_parse_types.assert_not_called()
        clang.parse_decls_with_parser.assert_not_called()
        ida.inf_set_genflags.assert_called_once()
        backend.on_finish()
        self.assertEqual(ida.inf_set_genflags.call_count, 2)

    def test_ida_imports_header_only_when_a_header_path_is_generated(self):
        for header, use_clang, import_header in itertools.product(("", "il2cpp.h"), (False, True), (False, True)):
            with self.subTest(header=header, use_clang=use_clang, import_header=import_header):
                ida = Mock(INFFL_AUTO=1, DEMNAM_GCC3=1, DEMNAM_NAME=2)
                ida.inf_get_genflags.return_value = 1
                typeinfo, clang, segment = Mock(), Mock(), Mock()
                typeinfo.get_c_macros.return_value = "original"
                typeinfo.idc_parse_decl.return_value = (0, b"type", b"fields")
                segment.get_segm_by_name.return_value = None
                namespace = target_classes("IDA", header_path=header, ida_ida=ida, ida_typeinf=typeinfo, ida_srclang=clang,
                                           ida_segment=segment, IDACLANG_AVAILABLE=use_clang, FOLDERS_AVAILABLE=False,
                                           DEFAULT_TIL=None, TINFO_DEFINITE=1, __file__=str(SCRIPTS / "fixture.py"))
                backend = namespace["IDADisassemblerInterface"](Mock(spec=BASE["BaseStatusHandler"]))
                backend.import_type_header = import_header
                backend.on_start()
                self.assertEqual(clang.parse_decls_with_parser.call_count, int(bool(header) and import_header and use_clang))
                self.assertEqual(typeinfo.idc_parse_types.call_count, int(bool(header) and import_header and not use_clang))
                self.assertEqual(typeinfo.del_til.called, bool(header) and import_header)
                self.assertTrue(backend.apply_structures)
                backend.set_data_type(0x1000, "struct Type *")
                typeinfo.apply_type.assert_called_once()
                backend.on_finish()
                self.assertEqual(ida.inf_set_genflags.call_args.args, (1,))

    def test_binary_ninja_debug_uses_loaded_types_without_opening_header(self):
        view = Mock(address_size=8, endianness="little")
        view.get_data_var_at.return_value = None
        parser = Mock()
        namespace = target_classes("BinaryNinja", header_path="", bv=view, Endianness=types.SimpleNamespace(LittleEndian="little"),
                                   open=Mock(side_effect=AssertionError("Header was opened")), TypeParser=parser,
                                   Symbol=lambda *args: args, SymbolType=types.SimpleNamespace(DataSymbol="data"))
        backend = namespace["BinaryNinjaDisassemblerInterface"](Mock(spec=BASE["BaseStatusHandler"]))
        backend.on_start()
        parser.default.parse_types_from_source.assert_not_called()
        view.define_user_types.assert_not_called()
        backend.set_data_name(0x1000, "field")
        view.define_user_symbol.assert_called_once_with(("data", 0x1000, "field"))
        backend.on_finish()

    def test_ghidra_debug_preserves_image_base_and_uses_loaded_pointer_types(self):
        program = Mock()
        program.getExecutableFormat.return_value = "Executable and Linking Format (ELF)"
        get_types = Mock(return_value=["loaded-type"])
        namespace = target_classes("Ghidra", header_path="", currentProgram=program, getDataTypes=get_types, setAnalysisOption=Mock(),
                                   PointerDataType=lambda base, manager: ("pointer", base), VoidDataType=types.SimpleNamespace(dataType="void"))
        backend = namespace["GhidraDisassemblerInterface"].__new__(namespace["GhidraDisassemblerInterface"])
        backend.on_start()
        get_types.assert_not_called()
        program.setImageBase.assert_not_called()
        self.assertEqual(backend._get_data_type("struct Type **"), ("pointer", ("pointer", "loaded-type")))

    def test_binary_ninja_skips_header_with_types_enabled_or_disabled(self):
        for apply_types in (False, True):
            with self.subTest(apply_types=apply_types):
                view = Mock(address_size=8, endianness="little")
                parser = Mock()
                namespace = target_classes("BinaryNinja", bv=view, Endianness=types.SimpleNamespace(LittleEndian="little"), open=Mock(side_effect=AssertionError("Header was opened")),
                                           TypeParser=parser, Symbol=lambda *args: args, SymbolType=types.SimpleNamespace(DataSymbol="data"))
                backend = namespace["BinaryNinjaDisassemblerInterface"](Mock(spec=BASE["BaseStatusHandler"]))
                backend.apply_structures = apply_types
                backend.import_type_header = False
                backend.on_start()
                parser.default.parse_types_from_source.assert_not_called()
                view.define_user_types.assert_not_called()
                loaded_var = Mock() if apply_types else None
                view.get_data_var_at.return_value = loaded_var
                backend.set_data_name(0x1000, "field")
                if apply_types:
                    self.assertEqual(loaded_var.name, "field")
                else:
                    view.define_user_symbol.assert_called_once_with(("data", 0x1000, "field"))
                backend.on_finish()
                view.commit_undo_actions.assert_called_once()

    def test_ghidra_skips_header_check_with_types_enabled_or_disabled(self):
        for apply_types in (False, True):
            with self.subTest(apply_types=apply_types):
                program = Mock()
                program.getExecutableFormat.return_value = "Portable Executable"
                get_types = Mock(side_effect=AssertionError("Types were queried"))
                namespace = target_classes("Ghidra", currentProgram=program, getDataTypes=get_types, setAnalysisOption=Mock())
                backend = namespace["GhidraDisassemblerInterface"].__new__(namespace["GhidraDisassemblerInterface"])
                backend.apply_structures = apply_types
                backend.import_type_header = False
                backend.on_start()
                get_types.assert_not_called()
                self.assertIs(backend.xrefs, program.getReferenceManager.return_value)


if __name__ == "__main__":
    unittest.main()
