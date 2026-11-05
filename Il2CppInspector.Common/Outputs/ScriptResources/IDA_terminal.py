# Terminal-only additions. The regular IDA implementation above supplies the API calls.
import contextlib
import hashlib
import sys
import time
import traceback
import uuid
from pathlib import Path

import ida_auto
import ida_loader
import ida_netnode
from tqdm import tqdm


def _pick_stream():
    """Use the real process console so progress output bypasses IDA's redirected stdout."""
    out = getattr(sys, "__stdout__", None)
    if out is not None:
        try:
            if hasattr(out, "reconfigure"):
                out.reconfigure(errors="backslashreplace")
            out.write("")
            out.flush()
            return out
        except Exception:
            pass
    try:
        import ctypes

        cp = ctypes.windll.kernel32.GetConsoleOutputCP()
        enc = "utf-8" if cp == 65001 else "cp%d" % cp
        return open("CONOUT$", "w", buffering=1, encoding=enc, errors="replace")
    except Exception:
        return sys.stderr


class ConsoleWriter:
    def __init__(self, out):
        self.out = out
        self.pending = ""

    def write(self, message):
        self.pending += message
        while "\n" in self.pending:
            line, self.pending = self.pending.split("\n", 1)
            tqdm.write(line, file=self.out)
        return len(message)

    def flush(self):
        if self.pending:
            tqdm.write(self.pending, file=self.out)
            self.pending = ""
        self.out.flush()


class ImportCancelled(Exception):
    pass


class CheckpointError(ValueError):
    pass


class TerminalStatusHandler(BaseStatusHandler):
    def __init__(self, out, resume=None):
        self.out = out
        self.resume_stage, self.resume_index = resume or (0, 0)
        self.stage = -1
        self.index = 0
        self.max_items = 0
        self.completed_items = 0
        self.last_refresh = 0.0
        self.next_key_check = 0.0
        self.stage_bar = None
        self.total_bar = None
        try:
            import msvcrt

            self.keys = msvcrt
        except ImportError:
            self.keys = None

    def initialize(self):
        options = dict(
            ncols=150, ascii=True, leave=True, mininterval=0.1, file=self.out,
            # Pad and cap both text fields so the two bars have identical lengths.
            bar_format="{bar}| {desc:40.40} {percentage:3.0f}% {postfix:54.54}",
        )
        self.stage_bar = tqdm(total=0, desc="Initializing", position=0, **options)
        self.total_bar = tqdm(total=None, desc="Total", position=1, **options)
        print("Press q to pause, save the database/checkpoint, and close (no Enter needed).")

    def check_cancelled(self):
        now = time.monotonic()
        if now < self.next_key_check:
            return
        self.next_key_check = now + 0.05
        try:
            while self.keys is not None and self.keys.kbhit():
                key = self.keys.getwch()
                if key in (chr(0), chr(224)):
                    self.keys.getwch()
                elif key.lower() == "q":
                    raise ImportCancelled("Paused by q.")
        except OSError:
            self.keys = None
            print("Console input unavailable; use Ctrl+C to pause.")

    @property
    def active(self):
        return self.stage >= self.resume_stage and self.index < self.max_items

    def show_step(self, name, total=1, initial=0):
        self.stage_bar.set_description_str(name, refresh=False)
        self.stage_bar.set_postfix_str("", refresh=False)
        self.stage_bar.reset(total=total)
        self.stage_bar.n = initial
        self.refresh(force=True)

    def update_step(self, name, max_items=0):
        if self.stage >= 0:
            self.completed_items += self.max_items
        self.stage += 1
        self.max_items = max_items
        self.index = (
            max_items if self.stage < self.resume_stage
            else self.resume_index if self.stage == self.resume_stage
            else 0
        )
        if not 0 <= self.index <= max_items:
            raise CheckpointError("Checkpoint item index is outside the current stage.")
        self.total_bar.n = self.completed_items + self.index
        self.show_step(name, max_items, self.index)
        self.check_cancelled()

    def update_progress(self, progress=1):
        if not self.active:
            return
        self.index += progress
        self.stage_bar.n = self.index
        self.total_bar.n = self.completed_items + self.index
        self.refresh(force=self.index == self.max_items)
        # The item is committed before checking q; a partial item is replayed.
        self.check_cancelled()

    def set_plan(self, plan):
        if self.resume_stage > len(plan):
            raise CheckpointError("Checkpoint stage is outside the current import plan.")
        limit = plan[self.resume_stage][1] if self.resume_stage < len(plan) else 0
        if not 0 <= self.resume_index <= limit:
            raise CheckpointError("Checkpoint item index is outside the import plan.")
        self.total_bar.total = sum(count for _, count in plan)
        self.refresh(force=True)

    def refresh(self, force=False):
        now = time.monotonic()
        if not force and now - self.last_refresh < 0.1:
            return
        self.last_refresh = now
        for bar in (self.stage_bar, self.total_bar):
            total = "?" if bar.total is None else str(bar.total)
            elapsed = tqdm.format_interval(time.time() - bar.start_t)
            bar.set_postfix_str(f"items={bar.n}/{total} elapsed={elapsed}", refresh=False)
            bar.refresh()

    def shutdown(self):
        if self.stage_bar is not None:
            self.refresh(force=True)
            self.stage_bar.close()
            self.total_bar.close()


class CountingStatusHandler(BaseStatusHandler):
    def __init__(self):
        self.plan = []

    def update_step(self, name, max_items=0):
        self.plan.append((name, max_items))


class CountingScriptContext(ScriptContext):
    def iter_items(self, items):
        return ()

    def should_process_step(self):
        return False


class TerminalDisassemblerInterface(IDADisassemblerInterface):
    def import_type_definitions(self):
        if self._status.active:
            super().import_type_definitions()

    def create_fake_segment(self, name, size):
        # Resume into the saved segment instead of appending a second copy.
        start = ida_segment.get_segment_ea_by_name(name)
        if start != ida_idaapi.BADADDR:
            segment = ida_segment.segment_info_t()
            if not ida_segment.get_segment_info(segment, start):
                raise RuntimeError("Cannot read the existing string segment.")
            if segment.end_ea - segment.start_ea < size:
                raise ValueError("Existing string segment is smaller than the import requires.")
            return start
        return super().create_fake_segment(name, size)


class TerminalScriptContext(ScriptContext):
    def iter_items(self, items):
        if self._status.active:
            start = self._status.index
            for item in itertools.islice(items, start, None):
                self._status.check_cancelled()
                yield item

    def should_process_step(self):
        return self._status.active

    def skipped_items(self):
        return self._status.index


def _file_identity(path):
    path = Path(path).resolve()
    stat = path.stat()
    return dict(path=os.path.normcase(str(path)), size=stat.st_size, mtime_ns=stat.st_mtime_ns)


def _checkpoint_identity(backend):
    directory = Path(backend.get_script_directory())
    header = "%TYPE_HEADER_RELATIVE_PATH%"
    imports_header = bool(
        header and backend.apply_structures and backend.import_type_header
        and not backend.function_boundaries_only
    )
    identity = dict(
        database=os.path.normcase(str(Path(ida_loader.get_path(ida_loader.PATH_TYPE_IDB)).resolve())),
        input_sha256=(ida_nalt.retrieve_input_file_sha256() or b"").hex(),
        imagebase=ida_nalt.get_imagebase(),
        metadata=_file_identity(directory / "%JSON_METADATA_RELATIVE_PATH%"),
        header=_file_identity(directory / header) if imports_header else None,
        script_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        settings=dict(
            function_boundaries_only=backend.function_boundaries_only,
            apply_structures=backend.apply_structures,
            import_type_header=backend.import_type_header,
            supports_fake_string_segment=backend.supports_fake_string_segment,
        ),
    )
    return identity, imports_header


def _load_checkpoint(path, identity, node):
    if not path.exists():
        return None
    record = json.loads(path.read_text(encoding="utf-8"))
    if record.get("version") != 1 or record.get("identity") != identity:
        raise ValueError("Checkpoint does not match this database, input files, script, or options.")
    if record.get("commit") != node.supstr(0):
        raise ValueError("Checkpoint does not match the saved database. Reopen its matching database before resuming.")
    stage, index = record["stage"], record["index"]
    if type(stage) is not int or type(index) is not int or stage < 0 or index < 0:
        raise ValueError("Invalid checkpoint position.")
    print(f"Resuming stage={stage}, next item={index} from {path}")
    return stage, index


def _save_checkpoint(path, identity, status, node):
    destination = Path(ida_loader.get_path(ida_loader.PATH_TYPE_IDB))
    if (
        ida_segment.get_segm_qty() == 0
        or os.path.normcase(str(destination.resolve())) != identity["database"]
        or ida_nalt.get_imagebase() != identity["imagebase"]
        or (ida_nalt.retrieve_input_file_sha256() or b"").hex() != identity["input_sha256"]
    ):
        raise ValueError("The active database changed during import; refusing to overwrite the database or checkpoint.")
    # SDK save_database: flags=0 inherits DBFL_KILL/DBFL_TEMP. A live save must
    # keep the unpacked database available for subsequent kernel/plugin cleanup.
    # DBFL_BAK requests a backup of the previous packed database before overwriting it.
    auto_enabled = ida_auto.enable_auto(False)
    pack_mode = ida_ida.inf_set_pack_mode(ida_ida.IDB_COMPRESSED)
    try:
        commit = uuid.uuid4().hex
        if not node.supset(0, commit):
            raise RuntimeError("Cannot write checkpoint marker to the database.")
        print(f"Saving compressed database: {destination}")
        if not ida_loader.save_database(str(destination), ida_loader.DBFL_BAK):
            print("Save failed. Database remains open; the checkpoint JSON was not advanced.")
            return False
    finally:
        ida_ida.inf_set_pack_mode(pack_mode)
        ida_auto.enable_auto(auto_enabled)
    stage, index = max(0, status.stage), status.index
    if status.stage < status.resume_stage:
        stage, index = status.resume_stage, status.resume_index
    record = dict(
        version=1, identity=identity, commit=commit,
        stage=stage, index=index,
    )
    # Commit JSON only after the matching database has reached disk.
    temporary = path.with_name(path.name + ".tmp")
    with temporary.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(record, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)
    print(f"Database saved: {destination}")
    print(f"Checkpoint saved: {path} (stage={record['stage']}, next item={record['index']})")
    return True


def run_terminal():
    out = _pick_stream()
    writer = ConsoleWriter(out)
    with contextlib.redirect_stdout(writer), contextlib.redirect_stderr(writer):
        status = TerminalStatusHandler(out)
        backend = TerminalDisassemblerInterface(status)
        context = TerminalScriptContext(backend, status)
        checkpoint = Path(__file__).with_suffix(".progress.json")
        # Reject incompatible checkpoints before touching types or functions.
        try:
            if ida_segment.get_segm_qty() == 0:
                raise ValueError(
                    "Cannot import into an empty database. Open the binary or its existing database before running the script; do not use -t."
                )
            identity, imports_header = _checkpoint_identity(backend)
            node = ida_netnode.netnode("$ il2cpp terminal checkpoint", 0, True)
            resume = _load_checkpoint(checkpoint, identity, node)
        except Exception:
            traceback.print_exc(file=out)
            return
        if resume is not None:
            status.resume_stage, status.resume_index = resume
        started = time.monotonic()
        backend_started = False
        exit_code = 0
        status.initialize()
        try:
            backend_started = True
            backend.on_start()
            status.show_step("Loading JSON metadata")
            status.check_cancelled()
            metadata_path = Path(backend.get_script_directory()) / "%JSON_METADATA_RELATIVE_PATH%"
            with metadata_path.open("r", encoding="utf-8") as stream:
                metadata = json.load(stream)["addressMap"]
            counter = CountingStatusHandler()
            CountingScriptContext(backend, counter).process_metadata(metadata)
            plan = ([("Processing Types", 1)] if imports_header else []) + counter.plan
            status.set_plan(plan)
            context.process_metadata(metadata)
            status.stage, status.index = len(plan), 0
            status.total_bar.n = status.total_bar.total
            status.show_step("Complete", initial=1)
            print(f"Import complete. Elapsed: {time.monotonic() - started:.1f}s")
        except (ImportCancelled, KeyboardInterrupt):
            print("Import paused; saving the next item for resume.")
        except CheckpointError:
            traceback.print_exc(file=out)
            return
        except Exception:
            exit_code = 1
            traceback.print_exc(file=out)
        finally:
            try:
                if backend_started:
                    backend.on_finish()
            finally:
                status.shutdown()
        try:
            saved = _save_checkpoint(checkpoint, identity, status, node)
        except Exception:
            traceback.print_exc(file=out)
            print("Save did not finish. Database remains open.")
            saved = False
        writer.flush()
        if saved:
            return exit_code


# qexit shuts down the UI, kernel and IDAPython. Unwind the import frames and
# restore stdout/stderr before invoking it, including on the q cancellation path.
exit_code = run_terminal()
if exit_code is not None:
    ida_pro.qexit(exit_code)
