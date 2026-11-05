import ast
import itertools
import io
import json
from pathlib import Path
import tempfile
import time
import types
import unittest
from unittest.mock import Mock, call, patch


ROOT = Path(__file__).resolve().parents[3]
SCRIPTS = ROOT / "Il2CppInspector.Common/Outputs/ScriptResources"
BADADDR = (1 << 64) - 1
shared = (SCRIPTS / "shared_base.py").read_text(encoding="utf-8-sig")
processor = (ROOT / "Il2CppInspector.Plugin/Shared/Morax/RuntimeCaches.py").read_text()
shared = shared.replace("        # %GAME_METADATA_PROCESSOR%", processor)
BASE = {}
exec(compile(shared, "shared_base.py", "exec"), BASE)


class Backend:
    def __init__(self, enabled, fake, boundaries_only=False):
        self.apply_structures = enabled
        self.supports_fake_string_segment = fake
        self.function_boundaries_only = boundaries_only
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
    namespace = {**BASE, "itertools": itertools,
                 "ida_idaapi": types.SimpleNamespace(BADADDR=BADADDR), **globals}
    exec(compile(ast.fix_missing_locations(ast.Module(body=body, type_ignores=[])), name, "exec"), namespace)
    return namespace


class ProgressBar:
    def __init__(self, total=None, **options):
        self.total = total
        self.n = 0
        self.start_t = time.time()

    def reset(self, total):
        self.total, self.n = total, 0

    def set_description_str(self, *args, **kwargs):
        pass

    def set_postfix_str(self, *args, **kwargs):
        pass

    def refresh(self):
        pass

    def close(self):
        pass

    @staticmethod
    def format_interval(seconds):
        return str(int(seconds))


def terminal_classes(**globals):
    namespace = target_classes("IDA", **globals)
    tree = ast.parse((SCRIPTS / "IDA_terminal.py").read_text())
    tree.body = [node for node in tree.body if not isinstance(node, (ast.Import, ast.ImportFrom, ast.Expr))]
    namespace.update(time=time, tqdm=ProgressBar, Path=Path, json=json, tempfile=tempfile,
                     print=lambda *args, **kwargs: None)
    exec(compile(tree, "IDA_terminal.py", "exec"), namespace)
    return namespace


class ScriptResourcesTests(unittest.TestCase):
    def test_terminal_resume_at_every_item_preserves_metadata_and_utf8_offsets(self):
        ns = terminal_classes()
        for legacy, fake, enabled, boundaries_only in (
            (False, True, True, False), (False, False, True, False),
            (True, True, True, False), (True, False, False, False),
            (False, True, True, True),
        ):
            data = metadata(legacy)
            for key in data:
                data[key] = data[key] * 2
            if not legacy:
                data["stringLiterals"][0] = {**data["stringLiterals"][0], "string": "测试"}

            def make_backend():
                backend = Backend(enabled, fake, boundaries_only)
                def write_string(address, value):
                    backend.calls.append(("write_string", (address, value)))
                    return len(value.encode("utf-8")) + 1
                backend.write_string = write_string
                return backend

            counter, backend = ns["CountingStatusHandler"](), make_backend()
            ns["CountingScriptContext"](backend, counter).process_metadata(data)
            self.assertEqual(backend.calls, [], "Counting must not modify the database")
            total = sum(count for _, count in counter.plan)

            def execute(backend, resume=None, stop=None):
                status = ns["TerminalStatusHandler"](io.StringIO(), resume)
                status.keys = None
                status.initialize()
                status.set_plan(counter.plan)
                update = status.update_progress
                def advance(progress=1):
                    update(progress)
                    if status.total_bar.n == stop:
                        raise ns["ImportCancelled"]()
                status.update_progress = advance
                context = ns["TerminalScriptContext"](backend, status)
                if stop is None:
                    context.process_metadata(data)
                    self.assertEqual(status.total_bar.n, total)
                else:
                    with self.assertRaises(ns["ImportCancelled"]):
                        context.process_metadata(data)
                status.shutdown()
                return status

            baseline = make_backend()
            execute(baseline)
            # Cache/segment setup is intentionally repeated on resuming a partial stage.
            def operations(backend):
                return [entry for entry in backend.calls if entry[0] not in ("cache_function_types", "create_fake_segment")]
            for stop in range(1, total + 1):
                with self.subTest(legacy=legacy, fake=fake, enabled=enabled, boundaries_only=boundaries_only, stop=stop):
                    backend = make_backend()
                    paused = execute(backend, stop=stop)
                    execute(backend, (paused.stage, paused.index))
                    self.assertEqual(operations(backend), operations(baseline))

    def test_terminal_completed_header_is_skipped_without_double_counting(self):
        ns = terminal_classes()
        status = ns["TerminalStatusHandler"](io.StringIO(), (0, 1))
        status.keys = None
        status.initialize()
        backend = ns["TerminalDisassemblerInterface"](status)
        status.update_step("Processing Types", 1)
        with patch.object(ns["IDADisassemblerInterface"], "import_type_definitions") as parse:
            backend.import_type_definitions()
        status.update_progress()
        parse.assert_not_called()
        self.assertEqual((status.index, status.total_bar.n), (1, 1))
        status.shutdown()

    def test_terminal_q_consumes_extended_keys_and_raises_without_enter(self):
        ns = terminal_classes()
        status = ns["TerminalStatusHandler"](io.StringIO())
        status.keys = Mock()
        status.keys.kbhit.side_effect = [True, True]
        status.keys.getwch.side_effect = [chr(224), "K", "Q"]
        with self.assertRaises(ns["ImportCancelled"]):
            status.check_cancelled()
        self.assertEqual(status.keys.getwch.call_count, 3)

    def test_terminal_checkpoint_is_committed_after_database_and_matches_its_marker(self):
        import os
        import uuid
        node = Mock()
        node.supset.return_value = True
        ns = terminal_classes(ida_auto=Mock(), ida_ida=Mock(), ida_loader=Mock())
        ns.update(os=os, uuid=uuid)
        with tempfile.TemporaryDirectory() as directory:
            checkpoint = Path(directory) / "il2cpp_t.progress.json"
            database = Path(directory) / "test.i64"
            identity = {"database": os.path.normcase(str(database.resolve()))}
            checkpoint.write_text("previous")
            ns["ida_loader"].get_path.return_value = str(database)
            def save(*args):
                self.assertEqual(checkpoint.read_text(), "previous")
                return True
            ns["ida_loader"].save_database.side_effect = save
            # Pausing while replaying skipped stages must retain the later resume pointer.
            status = types.SimpleNamespace(stage=1, index=2, resume_stage=5, resume_index=7)
            self.assertTrue(ns["_save_checkpoint"](checkpoint, identity, status, node))
            record = json.loads(checkpoint.read_text())
            self.assertEqual((record["stage"], record["index"]), (5, 7))
            self.assertFalse(any(isinstance(value, list) for value in record.values()))
            node.supstr.return_value = node.supset.call_args.args[1]
            self.assertEqual(ns["_load_checkpoint"](checkpoint, identity, node), (5, 7))
            node.supstr.return_value = "another-database"
            with self.assertRaisesRegex(ValueError, "saved database"):
                ns["_load_checkpoint"](checkpoint, identity, node)
            with self.assertRaisesRegex(ValueError, "input files"):
                ns["_load_checkpoint"](checkpoint, {"database": "different"}, node)
            ns["ida_loader"].save_database.side_effect = None
            ns["ida_loader"].save_database.return_value = False
            before = checkpoint.read_bytes()
            self.assertFalse(ns["_save_checkpoint"](checkpoint, identity, status, node))
            self.assertEqual(checkpoint.read_bytes(), before)

    def test_function_boundaries_only_skips_all_annotations_and_plugin_metadata(self):
        for enabled, fake, legacy in itertools.product((False, True), repeat=3):
            with self.subTest(enabled=enabled, fake=fake, legacy=legacy):
                backend = Backend(enabled, fake, boundaries_only=True)
                status = Mock(spec=BASE["BaseStatusHandler"])
                data = metadata(legacy)
                data["functionAddresses"] = ["0x1010", "0x0000", "0x1000"]
                BASE["ScriptContext"](backend, status).process_metadata(data)
                self.assertEqual(backend.calls, [
                    ("define_function", (0x1000, 0x1010)),
                    ("define_function", (0x1010, None)),
                ])
                status.update_step.assert_called_once_with("Processing function boundaries", 3)
                self.assertEqual(status.update_progress.call_count, 3)

        backend = Backend(True, True, boundaries_only=True)
        BASE["ScriptContext"](backend, Mock(spec=BASE["BaseStatusHandler"])).process_metadata({"functionAddresses": []})
        self.assertEqual(backend.calls, [])

    def test_ida_function_boundaries_only_creates_functions_with_pdata_without_importing_types(self):
        ida = Mock(INFFL_AUTO=1)
        ida.inf_get_genflags.return_value = 3
        typeinfo, clang, segment, functions = Mock(), Mock(), Mock(), Mock()
        segment.get_segment_ea_by_name.return_value = 0
        namespace = target_classes("IDA", ida_ida=ida, ida_typeinf=typeinfo, ida_srclang=clang,
                                   ida_segment=segment, ida_funcs=functions, ida_bytes=Mock(), ida_ua=Mock(),
                                   IDACLANG_AVAILABLE=True, FOLDERS_AVAILABLE=True)
        backend = namespace["IDADisassemblerInterface"](Mock(spec=BASE["BaseStatusHandler"]))
        backend.function_boundaries_only = True
        backend.on_start()
        typeinfo.del_til.assert_not_called()
        typeinfo.idc_parse_types.assert_not_called()
        clang.parse_decls_with_parser.assert_not_called()
        backend.define_function(0x1000, 0x1010)
        functions.add_func.assert_called_once_with(0x1000, 0x1010)
        backend.on_finish()
        self.assertEqual(ida.inf_set_genflags.call_args_list, [call(2), call(3)])

    def test_ida_function_comments_resolve_function_start_and_skip_missing_functions(self):
        for start in (0, 0x1000, BADADDR):
            with self.subTest(start=start):
                functions = Mock(spec=["get_func_start", "set_func_cmt_ea"])
                functions.get_func_start.return_value = start
                ns = target_classes("IDA", ida_funcs=functions)
                backend = ns["IDADisassemblerInterface"](Mock())
                backend.set_function_comment(0x1010, "Function signature")
                functions.get_func_start.assert_called_once_with(0x1010)
                if start == BADADDR:
                    functions.set_func_cmt_ea.assert_not_called()
                else:
                    functions.set_func_cmt_ea.assert_called_once_with(start, "Function signature", True)

    def test_ida_pdata_lookup_handles_address_zero_and_missing_segments(self):
        for address in (0, BADADDR):
            with self.subTest(address=address):
                ida = Mock(INFFL_AUTO=1, DEMNAM_GCC3=1, DEMNAM_NAME=2)
                ida.inf_get_genflags.return_value = 1
                segment = Mock(spec=["get_segment_ea_by_name"])
                segment.get_segment_ea_by_name.return_value = address
                functions = Mock(spec=["add_func"])
                ns = target_classes("IDA", ida_ida=ida, ida_segment=segment,
                                    ida_funcs=functions, ida_bytes=Mock(), ida_ua=Mock(),
                                    FOLDERS_AVAILABLE=False)
                backend = ns["IDADisassemblerInterface"](Mock())
                backend.apply_structures = False
                backend.on_start()
                backend.define_function(0x1000, 0x1010)
                segment.get_segment_ea_by_name.assert_called_once_with(".pdata")
                if address == BADADDR:
                    functions.add_func.assert_called_once_with(0x1000, 0x1010)
                else:
                    functions.add_func.assert_not_called()

    def test_terminal_string_segment_creation_and_resume_use_address_api(self):
        for is_32_bit in (False, True):
            with self.subTest(is_32_bit=is_32_bit):
                info = Mock(spec=["start_ea", "end_ea", "set_perm"])
                info.start_ea, info.end_ea = 0x8000, 0x8100
                segment = Mock(spec=["get_segment_ea_by_name", "segment_info_t",
                                     "get_segment_info", "add_segm", "set_segment_addressing",
                                     "set_segment_info", "SEGPERM_READ"], SEGPERM_READ=4)
                segment.segment_info_t.return_value = info
                segment.get_segment_info.return_value = True
                segment.get_segment_ea_by_name.return_value = BADADDR
                ida = Mock()
                ida.inf_get_max_ea.return_value = 0x8000
                ns = terminal_classes(ida_ida=ida, ida_segment=segment)
                backend = ns["TerminalDisassemblerInterface"](Mock())
                backend._is_32_bit = is_32_bit
                self.assertEqual(backend.create_fake_segment(".strings", 0x100), 0x8000)
                segment.add_segm.assert_called_once_with(0, 0x8000, 0x8100, ".strings", "DATA")
                segment.set_segment_addressing.assert_called_once_with(0x8000, 1 if is_32_bit else 2)
                info.set_perm.assert_called_once_with(4)
                segment.set_segment_info.assert_called_once_with(info)
                segment.get_segment_ea_by_name.return_value = 0x8000
                self.assertEqual(backend.create_fake_segment(".strings", 0x100), 0x8000)
                self.assertEqual(segment.add_segm.call_count, 1)
                with self.assertRaisesRegex(ValueError, "smaller"):
                    backend.create_fake_segment(".strings", 0x101)
                segment.get_segment_info.return_value = False
                with self.assertRaisesRegex(RuntimeError, "existing string segment"):
                    backend.create_fake_segment(".strings", 0x100)

    def test_method_info_without_method_body_preserves_annotations_and_skips_reference(self):
        for enabled, method_address in itertools.product((False, True), (None, "0x00000000", "0x1010")):
            with self.subTest(enabled=enabled, method_address=method_address):
                backend = Backend(enabled, False)
                context = BASE["ScriptContext"](backend, Mock(spec=BASE["BaseStatusHandler"]))
                definition = {
                    "virtualAddress": "0x2000", "name": "MethodInfo_Publish",
                    "dotNetSignature": "Void Publish[CompleteMessage](CompleteMessage)",
                }
                if method_address is not None:
                    definition["methodAddress"] = method_address
                context.define_il_method_info(definition)
                self.assertIn(("set_data_name", (0x2000, definition["name"])), backend.calls)
                self.assertIn(("set_data_comment", (0x2000, definition["dotNetSignature"])), backend.calls)
                self.assertEqual(("set_data_type", (0x2000, "struct MethodInfo *")) in backend.calls, enabled)
                references = [args for name, args in backend.calls if name == "add_cross_reference"]
                self.assertEqual(references, [(0x1010, 0x2000)] if method_address == "0x1010" else [])

    def test_native_fields_with_null_managed_types_preserve_names_and_types(self):
        for enabled in (False, True):
            with self.subTest(enabled=enabled):
                backend = Backend(enabled, False)
                context = BASE["ScriptContext"](backend, Mock(spec=BASE["BaseStatusHandler"]))
                native = {"virtualAddress": "0x4000", "name": "NativeString_Tick", "type": "char *", "dotNetType": None}
                context.define_field_from_json(native)
                self.assertIn(("set_data_name", (0x4000, "NativeString_Tick")), backend.calls)
                self.assertEqual(("set_data_type", (0x4000, "char *")) in backend.calls, enabled)
                self.assertNotIn("set_data_comment", {name for name, _ in backend.calls})
                context.define_field_from_json(metadata()["typeInfoPointers"][0])
                self.assertIn(("set_data_comment", (0x2000, "Type")), backend.calls)

    def test_morax_native_caches_continue_to_type_references(self):
        processor = (ROOT / "Il2CppInspector.Plugin/Shared/Morax/RuntimeCaches.py").read_text()
        source = (SCRIPTS / "shared_base.py").read_text(encoding="utf-8-sig")
        namespace = {}
        exec(compile(source.replace("        # %GAME_METADATA_PROCESSOR%", processor), "morax.py", "exec"), namespace)
        for enabled in (False, True):
            with self.subTest(enabled=enabled):
                data = metadata()
                data["moraxNativeStrings"] = [{"virtualAddress": "0x4000", "name": "NativeString_Tick", "type": "char *", "dotNetType": None, "length": 4, "value": "Tick"}]
                data["moraxEmptyArrays"] = [{"virtualAddress": "0x5000", "name": "EmptyArray_Int32", "type": "struct Int32__Array *", "dotNetType": "System.Int32[]", "length": 0, "subtype": 0}]
                data["typeRefPointers"][0].update(virtualAddress="0x6000", name="FollowingTypeRef")
                backend = Backend(enabled, False)
                namespace["ScriptContext"](backend, Mock(spec=namespace["BaseStatusHandler"])).process_metadata(data)
                names = [args for name, args in backend.calls if name == "set_data_name"]
                self.assertLess(names.index((0x5000, "EmptyArray_Int32")), names.index((0x4000, "NativeString_Tick")))
                self.assertLess(names.index((0x4000, "NativeString_Tick")), names.index((0x6000, "FollowingTypeRef")))
                self.assertIn(("set_data_comment", (0x5000, "System.Int32[]")), backend.calls)

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
        segment.get_segment_ea_by_name.return_value = BADADDR
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
                segment.get_segment_ea_by_name.return_value = BADADDR
                namespace = target_classes("IDA", header_path=header, ida_ida=ida, ida_typeinf=typeinfo, ida_srclang=clang,
                                           ida_segment=segment, IDACLANG_AVAILABLE=use_clang, FOLDERS_AVAILABLE=False,
                                           DEFAULT_TIL=None, TINFO_DEFINITE=1, __file__=str(SCRIPTS / "fixture.py"))
                backend = namespace["IDADisassemblerInterface"](Mock(spec=BASE["BaseStatusHandler"]))
                backend.import_type_header = import_header
                backend.on_start()
                self.assertEqual(clang.parse_decls_with_parser.call_count, int(bool(header) and import_header and use_clang))
                self.assertEqual(typeinfo.idc_parse_types.call_count, int(bool(header) and import_header and not use_clang))
                self.assertEqual(typeinfo.del_til.called, bool(header) and import_header)
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
        for apply_types, boundaries_only in itertools.product((False, True), repeat=2):
            with self.subTest(apply_types=apply_types, boundaries_only=boundaries_only):
                view = Mock(address_size=8, endianness="little")
                parser = Mock()
                namespace = target_classes("BinaryNinja", bv=view, Endianness=types.SimpleNamespace(LittleEndian="little"), open=Mock(side_effect=AssertionError("Header was opened")),
                                           TypeParser=parser, Symbol=lambda *args: args, SymbolType=types.SimpleNamespace(DataSymbol="data"))
                backend = namespace["BinaryNinjaDisassemblerInterface"](Mock(spec=BASE["BaseStatusHandler"]))
                backend.apply_structures = apply_types
                backend.function_boundaries_only = boundaries_only
                backend.import_type_header = boundaries_only
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
        for apply_types, boundaries_only in itertools.product((False, True), repeat=2):
            with self.subTest(apply_types=apply_types, boundaries_only=boundaries_only):
                program = Mock()
                program.getExecutableFormat.return_value = "Portable Executable"
                get_types = Mock(side_effect=AssertionError("Types were queried"))
                namespace = target_classes("Ghidra", currentProgram=program, getDataTypes=get_types, setAnalysisOption=Mock())
                backend = namespace["GhidraDisassemblerInterface"].__new__(namespace["GhidraDisassemblerInterface"])
                backend.apply_structures = apply_types
                backend.function_boundaries_only = boundaries_only
                backend.import_type_header = boundaries_only
                backend.on_start()
                get_types.assert_not_called()


if __name__ == "__main__":
    unittest.main()
