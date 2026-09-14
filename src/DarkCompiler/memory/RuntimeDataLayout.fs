// RuntimeDataLayout.fs - Shared placement rules for writable ELF instrumentation.

module RuntimeDataLayout

/// ELF images use a page-aligned base. Keep writable counters off executable
/// code pages, including 4/16/64 KiB host pages, so QEMU does not invalidate hot
/// translated code on every instrumentation write. Normal images have no pad.
let elfCounterOffset (dataEnd: int) : int =
    (dataEnd + 65535) &&& (~~~65535)
