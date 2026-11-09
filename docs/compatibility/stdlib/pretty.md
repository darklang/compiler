# Pretty-printing parity

Semantic comparison baseline: `darklang/dark@0b3888d8e4f30d48ecd738f5cbe5cc2b8d958460`.
The [source ledger](../../porting/README.md) owns current support and test dispositions.

`Stdlib.Pretty` is pure Dark in the pinned interpreter, so the compiler has no
copy of its own: it selects the imported original from
`upstream/packages/darklang/stdlib/pretty.dark`. Rendering uses
`String.displayWidth`, so zero-width styling and wide Unicode text do not
distort group-fit decisions.

Layout coverage is main's own suite, imported as
`upstream/packages/darklang/tests/stdlib/pretty.dark`.
