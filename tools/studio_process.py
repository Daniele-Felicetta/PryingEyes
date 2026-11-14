"""OS-backed locks and containment of owned process trees; no project/application dependencies."""
from contextlib import contextmanager
import ctypes
import errno
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
import uuid


def atomic_json(path, data):
    path = Path(path); path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temp.open("w", encoding="utf-8") as stream:
            json.dump(data, stream, indent=2); stream.flush(); os.fsync(stream.fileno())
        os.replace(temp, path)
    finally:
        temp.unlink(missing_ok=True)


@contextmanager
def file_lock(path, wait=False, check=lambda: None):
    path = Path(path); path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a+b") as handle:
        if handle.tell() == 0: handle.write(b"0"); handle.flush()
        while True:
            check(); handle.seek(0)
            try:
                if os.name == "nt":
                    import msvcrt
                    msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except OSError as error:
                if error.errno not in (errno.EACCES, errno.EAGAIN, errno.EDEADLK): raise
                if not wait: raise RuntimeError("PROJECT_BUSY: resource is locked") from error
                time.sleep(.2)
        try: yield
        finally:
            handle.seek(0)
            if os.name == "nt": msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else: fcntl.flock(handle.fileno(), fcntl.LOCK_UN)


if os.name == "nt":
    from ctypes import wintypes as w
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    class BasicLimits(ctypes.Structure):
        _fields_ = [("processTime", ctypes.c_longlong), ("jobTime", ctypes.c_longlong), ("flags", w.DWORD),
                    ("minWorking", ctypes.c_size_t), ("maxWorking", ctypes.c_size_t), ("active", w.DWORD),
                    ("affinity", ctypes.c_size_t), ("priority", w.DWORD), ("scheduling", w.DWORD)]
    class ExtendedLimits(ctypes.Structure):
        _fields_ = [("basic", BasicLimits), ("io", ctypes.c_ulonglong * 6), ("processMemory", ctypes.c_size_t),
                    ("jobMemory", ctypes.c_size_t), ("peakProcess", ctypes.c_size_t), ("peakJob", ctypes.c_size_t)]
    for name, result, arguments in [
        ("CreateJobObjectW", w.HANDLE, [ctypes.c_void_p, w.LPCWSTR]),
        ("SetInformationJobObject", w.BOOL, [w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD]),
        ("AssignProcessToJobObject", w.BOOL, [w.HANDLE, w.HANDLE]),
        ("TerminateJobObject", w.BOOL, [w.HANDLE, w.UINT]),
        ("CloseHandle", w.BOOL, [w.HANDLE]),
        ("OpenProcess", w.HANDLE, [w.DWORD, w.BOOL, w.DWORD]),
        ("WaitForSingleObject", w.DWORD, [w.HANDLE, w.DWORD]),
        ("GetProcessTimes", w.BOOL, [w.HANDLE, ctypes.POINTER(w.FILETIME), ctypes.POINTER(w.FILETIME), ctypes.POINTER(w.FILETIME), ctypes.POINTER(w.FILETIME)])]:
        function = getattr(kernel, name); function.restype = result; function.argtypes = arguments


def process_identity(pid):
    """Creation token, not just PID; None only after observing that the process is absent/exited."""
    if os.name == "nt":
        handle = kernel.OpenProcess(0x100000 | 0x1000, False, int(pid))
        if not handle:
            error = ctypes.get_last_error()
            if error == 87: return None
            raise OSError(error, "PROCESS_LIVENESS_UNVERIFIED")
        try:
            if kernel.WaitForSingleObject(handle, 0) == 0: return None
            values = [w.FILETIME() for _ in range(4)]
            if not kernel.GetProcessTimes(handle, *(ctypes.byref(v) for v in values)): raise ctypes.WinError(ctypes.get_last_error())
            return str(values[0].dwHighDateTime << 32 | values[0].dwLowDateTime)
        finally: kernel.CloseHandle(handle)
    proc = Path(f"/proc/{int(pid)}/stat")
    if Path("/proc").exists():
        try:
            fields = proc.read_text().rsplit(")", 1)[1].split()
            return None if fields[0] == "Z" else fields[19]
        except FileNotFoundError: return None
    result = subprocess.run(["ps", "-o", "lstart=", "-p", str(int(pid))], capture_output=True, text=True)
    return result.stdout.strip() or None


class OwnedProcess:
    """A waiting launcher is contained BEFORE it may start the command. Cancellation never targets an arbitrary PID.
    Windows lifecycle: https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects
    """
    def __init__(self, command, cwd, stdout):
        self.handle = None; self.child = None
        options = {"start_new_session": True} if os.name != "nt" else {"creationflags": 0x08000000}
        if os.name == "nt":
            self.handle = kernel.CreateJobObjectW(None, None)
            if not self.handle: raise ctypes.WinError(ctypes.get_last_error())
            limits = ExtendedLimits(); limits.basic.flags = 0x2000  # KILL_ON_JOB_CLOSE, not inheritable
            if not kernel.SetInformationJobObject(self.handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
                self.close(); raise ctypes.WinError(ctypes.get_last_error())
        try:
            self.child = subprocess.Popen([sys.executable, str(Path(__file__).resolve()), *map(str, command)],
                cwd=cwd, stdin=subprocess.PIPE, stdout=stdout, stderr=subprocess.STDOUT, **options)
            if self.handle and not kernel.AssignProcessToJobObject(self.handle, int(self.child._handle)):
                raise ctypes.WinError(ctypes.get_last_error())
            self.child.stdin.write(b"RUN\n"); self.child.stdin.close()
        except BaseException:
            if self.child is not None: self.child.kill(); self.child.wait()
            self.close(); raise

    @property
    def pid(self): return self.child.pid
    @property
    def returncode(self): return self.child.returncode
    def wait(self, timeout=None): return self.child.wait(timeout)
    def stop(self):
        if self.handle:
            if not kernel.TerminateJobObject(self.handle, 130): raise ctypes.WinError(ctypes.get_last_error())
        elif self.child.poll() is None:
            try: os.killpg(self.child.pid, signal.SIGKILL)
            except ProcessLookupError: pass
        self.child.wait(timeout=10)
    def close(self):
        if self.handle:
            kernel.CloseHandle(self.handle); self.handle = None
        elif self.child is not None and self.child.poll() is None: self.stop()


if __name__ == "__main__":
    if sys.stdin.buffer.readline() != b"RUN\n": raise SystemExit(125)
    options = {}
    if os.name == "nt":
        startup = subprocess.STARTUPINFO(); startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW; startup.wShowWindow = 0
        options = {"creationflags": 0x08000000, "startupinfo": startup}
    child = subprocess.Popen(sys.argv[1:], stdin=subprocess.DEVNULL, **options)
    print("STUDIO owned command PID:", child.pid, flush=True)
    raise SystemExit(child.wait())
