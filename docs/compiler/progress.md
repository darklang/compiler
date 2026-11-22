# Stdlib preparation progress

The first compile after installing a new darkc prepares the standard library and, given
`--cache-dir DIR`, keeps it in DIR. That takes about a minute and a half and several GB of memory.
Later compiles with the same DIR read it from there.

With `--verbose=json`, darkc prints one JSON object per line on stderr as preparation goes on,
for a caller such as `dark compile` to show. Without the flag it prints nothing.

```text
$ darkc --verbose=json -q -r -e "2 + 3"
{"type":"progress","task":"prepare-stdlib","stage":"parse","percent":0,"elapsedMs":40}
{"type":"progress","task":"prepare-stdlib","stage":"type-check","percent":10,"elapsedMs":9825}
{"type":"progress","task":"prepare-stdlib","stage":"convert","percent":11,"elapsedMs":12092}
{"type":"progress","task":"prepare-stdlib","stage":"optimize","percent":39,"elapsedMs":38545}
{"type":"progress","task":"prepare-stdlib","stage":"lower","percent":83,"elapsedMs":78453}
{"type":"progress","task":"prepare-stdlib","stage":"write","percent":95,"elapsedMs":88416}
{"type":"progress","task":"prepare-stdlib","stage":"ready","percent":100,"cached":false,"elapsedMs":96149}
5
```

Every line has:

| Field | Meaning |
| --- | --- |
| `type` | always `"progress"` |
| `task` | always `"prepare-stdlib"` |
| `stage` | see below |
| `percent` | an estimate of how much of a cold preparation is done, 0 to 100 |
| `elapsedMs` | milliseconds since this darkc started |

The stages, in order:

| Stage | Meaning |
| --- | --- |
| `waiting` | another darkc is preparing the same cache directory; this one waits for its result. `pid` is that process, `percent` how far it is. Repeated when its stage changes. |
| `parse`, `type-check`, `convert`, `optimize`, `lower` | the steps of a cold preparation |
| `write` | storing the result in the cache |
| `ready` | the stdlib is ready; `cached` is true when it was read from the cache, by this process or after waiting |

A cache hit prints only `ready`. Other stage names may be added; a reader should show the
`percent` of any stage it does not know.

## One preparation at a time

darkc takes a lock on `stdlib.lock` in the cache directory before preparing, so two compiles
started together prepare once: the second waits, then reads the first one's result. The lock
goes away with its process, so a killed preparation never blocks the next one, and the next one
removes the `stdlib-*.partial` file it left.
