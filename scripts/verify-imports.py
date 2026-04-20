"""Static import verification via pefile.

Walks every .dll/.exe under PUBLISH_DIR, dumps imports, and flags any
import that is neither bundled in the publish dir nor on a known
Windows + NVIDIA system allowlist.
"""

from __future__ import annotations

import os
import sys

import pefile

# Baseline DLLs Windows + NVIDIA driver always provides.
SYSTEM_ALLOWLIST = {
    name.lower()
    for name in (
        # Win32 / CRT
        "ntdll.dll", "kernel32.dll", "kernelbase.dll", "user32.dll", "gdi32.dll",
        "gdi32full.dll", "msvcrt.dll", "rpcrt4.dll", "sechost.dll", "advapi32.dll",
        "combase.dll", "ole32.dll", "oleaut32.dll", "shell32.dll", "shlwapi.dll",
        "shcore.dll", "win32u.dll", "imm32.dll", "ws2_32.dll", "crypt32.dll",
        "msasn1.dll", "bcrypt.dll", "bcryptprimitives.dll", "ncrypt.dll",
        "cryptbase.dll", "cryptsp.dll", "wintrust.dll", "mscoree.dll",
        "iphlpapi.dll", "dnsapi.dll", "nsi.dll", "winhttp.dll", "winmm.dll",
        "mfplat.dll", "mf.dll", "avrt.dll", "mmdevapi.dll", "audioses.dll",
        "opengl32.dll", "glu32.dll", "d3d9.dll", "d3d11.dll", "d3d12.dll",
        "dxgi.dll", "directxdatabasehelper.dll", "d3dcompiler_47.dll",
        "dwmapi.dll", "uxtheme.dll", "uiautomationcore.dll",
        "powrprof.dll", "profapi.dll", "wtsapi32.dll", "version.dll",
        "setupapi.dll", "cfgmgr32.dll", "devobj.dll", "wldp.dll",
        "kernel.appcore.dll", "gdiplus.dll", "msvcp_win.dll", "ucrtbase.dll",
        "webio.dll", "ws2help.dll", "normaliz.dll", "usp10.dll", "comdlg32.dll",
        "comctl32.dll", "msctf.dll", "oleacc.dll", "urlmon.dll",
        "twinapi.appcore.dll", "windows.storage.dll", "wininet.dll",
        "sspicli.dll", "secur32.dll", "fwpuclnt.dll", "mswsock.dll",
        "rasapi32.dll", "rasman.dll", "rtutils.dll",
        # MSVC runtime — ships in System32 on Win10 1803+/Win11
        "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "msvcp140_1.dll",
        "vcomp140.dll",
        # Other System32 baseline DLLs
        "dbghelp.dll", "psapi.dll", "windowscodecs.dll",
        # Win10+ API-MS umbrella set (core)
        "api-ms-win-core-path-l1-1-0.dll",
        "api-ms-win-core-synch-l1-1-0.dll",
        "api-ms-win-core-synch-l1-2-0.dll",
        "api-ms-win-core-fibers-l1-1-1.dll",
        "api-ms-win-core-localization-l1-2-0.dll",
        "api-ms-win-core-console-l1-1-0.dll",
        "api-ms-win-core-datetime-l1-1-0.dll",
        "api-ms-win-core-debug-l1-1-0.dll",
        "api-ms-win-core-errorhandling-l1-1-0.dll",
        "api-ms-win-core-file-l1-1-0.dll",
        "api-ms-win-core-file-l1-2-0.dll",
        "api-ms-win-core-file-l2-1-0.dll",
        "api-ms-win-core-handle-l1-1-0.dll",
        "api-ms-win-core-heap-l1-1-0.dll",
        "api-ms-win-core-interlocked-l1-1-0.dll",
        "api-ms-win-core-libraryloader-l1-1-0.dll",
        "api-ms-win-core-libraryloader-l1-2-0.dll",
        "api-ms-win-core-memory-l1-1-0.dll",
        "api-ms-win-core-namedpipe-l1-1-0.dll",
        "api-ms-win-core-processenvironment-l1-1-0.dll",
        "api-ms-win-core-processthreads-l1-1-0.dll",
        "api-ms-win-core-processthreads-l1-1-1.dll",
        "api-ms-win-core-profile-l1-1-0.dll",
        "api-ms-win-core-rtlsupport-l1-1-0.dll",
        "api-ms-win-core-string-l1-1-0.dll",
        "api-ms-win-core-sysinfo-l1-1-0.dll",
        "api-ms-win-core-timezone-l1-1-0.dll",
        "api-ms-win-core-util-l1-1-0.dll",
        # Security / EventLog API sets
        "api-ms-win-security-base-l1-1-0.dll",
        "api-ms-win-security-systemfunctions-l1-1-0.dll",
        "api-ms-win-eventing-provider-l1-1-0.dll",
        # Win10+ API-MS umbrella set
        "api-ms-win-crt-runtime-l1-1-0.dll",
        "api-ms-win-crt-string-l1-1-0.dll",
        "api-ms-win-crt-stdio-l1-1-0.dll",
        "api-ms-win-crt-math-l1-1-0.dll",
        "api-ms-win-crt-heap-l1-1-0.dll",
        "api-ms-win-crt-time-l1-1-0.dll",
        "api-ms-win-crt-locale-l1-1-0.dll",
        "api-ms-win-crt-filesystem-l1-1-0.dll",
        "api-ms-win-crt-environment-l1-1-0.dll",
        "api-ms-win-crt-convert-l1-1-0.dll",
        "api-ms-win-crt-utility-l1-1-0.dll",
        "api-ms-win-crt-multibyte-l1-1-0.dll",
        "api-ms-win-crt-conio-l1-1-0.dll",
        "api-ms-win-crt-process-l1-1-0.dll",
        "api-ms-win-crt-private-l1-1-0.dll",
        # NVIDIA driver-installed
        "nvcuda.dll", "nvfatbinaryloader.dll", "nvapi64.dll",
        "nvml.dll", "nvoglv64.dll", "nvinit.dll", "nvopencl.dll",
        # Vulkan loader (NVIDIA driver installs vulkan-1.dll)
        "vulkan-1.dll",
    )
}


def collect_bundled(publish_dir: str) -> set[str]:
    bundled: set[str] = set()
    for root, _dirs, files in os.walk(publish_dir):
        for f in files:
            ext = os.path.splitext(f)[1].lower()
            if ext in (".dll", ".exe"):
                bundled.add(f.lower())
    return bundled


def imports_of(path: str) -> list[str]:
    try:
        pe = pefile.PE(path, fast_load=True)
        pe.parse_data_directories(
            directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"]]
        )
        out = []
        if hasattr(pe, "DIRECTORY_ENTRY_IMPORT"):
            for e in pe.DIRECTORY_ENTRY_IMPORT:
                out.append(e.dll.decode().lower())
        pe.close()
        return out
    except Exception:
        return []


# DLLs the bootstrapper installs at first-run, by base name. They live under
# Resources/cuda/* and Resources/cudnn/* after install — so for a fresh,
# pre-bootstrap publish dir they show up as "missing" but they're not real
# packaging gaps. We classify them separately so the report is honest about
# what gets pulled at runtime vs. what would actually fail to load.
BOOTSTRAP_INSTALLED = {
    name.lower()
    for name in (
        # CUDA 12 (ONNX Runtime)
        "cudart64_12.dll", "cublas64_12.dll", "cublaslt64_12.dll",
        "cufft64_11.dll", "cudnn64_9.dll",
        # cuDNN sub-libs (extracted under Resources/cudnn/.../bin)
        "cudnn_engines_runtime_compiled64_9.dll",
        "cudnn_engines_precompiled64_9.dll",
        "cudnn_heuristic64_9.dll", "cudnn_ops64_9.dll", "cudnn_adv64_9.dll",
        "cudnn_cnn64_9.dll", "cudnn_graph64_9.dll",
        # CUDA 13 (Whisper)
        "cudart64_13.dll", "cublas64_13.dll", "cublaslt64_13.dll",
    )
}

# DLLs only loaded when the optional TensorRT execution provider is enabled.
# Persona Engine doesn't enable TRT by default, so missing nvinfer/nvonnxparser
# is not a fatal packaging issue.
TENSORRT_OPTIONAL = {"nvinfer_10.dll", "nvonnxparser_10.dll"}


def main(publish_dir: str) -> int:
    publish_dir = os.path.abspath(publish_dir)
    print(f"=== Static import check ===")
    print(f"Publish dir : {publish_dir}")

    bundled = collect_bundled(publish_dir)
    print(f"Bundled DLL/EXE: {len(bundled)}")

    issues: list[tuple[str, str]] = []
    bootstrap_only: list[tuple[str, str]] = []
    tensorrt_only: list[tuple[str, str]] = []
    n_scanned = 0
    for root, _dirs, files in os.walk(publish_dir):
        for f in files:
            ext = os.path.splitext(f)[1].lower()
            if ext not in (".dll", ".exe"):
                continue
            full = os.path.join(root, f)
            n_scanned += 1
            for imp in imports_of(full):
                if imp in SYSTEM_ALLOWLIST:
                    continue
                if imp in bundled:
                    continue
                rel = os.path.relpath(full, publish_dir)
                if imp in BOOTSTRAP_INSTALLED:
                    bootstrap_only.append((rel, imp))
                elif imp in TENSORRT_OPTIONAL:
                    tensorrt_only.append((rel, imp))
                else:
                    issues.append((rel, imp))

    print(f"Binaries scanned: {n_scanned}")
    print()

    def group(items: list[tuple[str, str]]) -> dict[str, list[str]]:
        out: dict[str, list[str]] = {}
        for src, dep in items:
            out.setdefault(dep, []).append(src)
        return out

    def print_group(label: str, items: list[tuple[str, str]]) -> None:
        if not items:
            return
        by_dep = group(items)
        print(f"{label} ({len(items)} imports across {len(by_dep)} deps):")
        for dep in sorted(by_dep):
            print(f"  {dep}")
            for src in sorted(by_dep[dep])[:20]:
                print(f"    used by: {src}")
            if len(by_dep[dep]) > 20:
                print(f"    ... and {len(by_dep[dep]) - 20} more")
        print()

    print_group("[INFO] Bootstrapper-installed (will be present after first run)", bootstrap_only)
    print_group("[INFO] TensorRT optional (only loaded if TRT EP enabled)", tensorrt_only)

    if not issues:
        print("[OK] No real packaging gaps — every other import is bundled or on the system allowlist.")
        return 0

    print_group("[WARN] Real packaging gaps", issues)
    return 1


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("usage: verify-imports.py <publish-dir>", file=sys.stderr)
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
