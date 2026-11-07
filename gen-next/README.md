# qtgen-next

A clean rewrite of the QtSharp binding generator. It now produces the bindings in
`tools/regen-api.ps1` and in the CI/release workflows; the old `gen/` executable is kept for model
extraction, `split-core` and the module descriptor. The old code generator remains in the tree as
the byte-for-byte oracle, but no build path invokes it.

## Layering

```
Program
  └─ GeneratorDriver                 one run: load → passes → emit → save
       ├─ ModelDocument              the JSON model, opened once
       ├─ ModelCatalog               module metadata and dependency closure
       ├─ GeneratorContext           per-run state; no static run state below the driver
       ├─ PassPipeline               ordered model transformations
       └─ Emitters                   read the finished context and produce files
            ├─ CSharpEmitter          the C# declaration kinds
            │    ├─ EnumEmitter
            │    ├─ FreeFunctionEmitter
            │    ├─ CSharpClassEmitter
            │    ├─ QObjectRegistrationEmitter
            │    ├─ StructEmitter
            │    └─ ContainerGlueEmitter / CallbackEmitter
            ├─ TypeTableEmitter       the C# → Qt metatype table
            └─ NativeEmitter          the C++ translation unit
```

Rules:

* **Passes** own naming, inheritance, ownership, type-shape and default-value decisions. They may
  add state to `GeneratorContext`; later passes read it.
* **Emitters** never mutate the model and never decide a type mapping ad hoc. They ask the type
  system and read the names the passes produced.
* **No static per-run state** below `GeneratorDriver`. A `GeneratorContext` is created per run and
  disposed at the end.
* **Deterministic output**: the same model produces the same files, independent of dictionary
  enumeration or process state.

## The model and type system

`BindingModel` is the run's class/enum vocabulary: which rows this assembly emits, which belong to
a dependency, the C# and C++ spellings, ownership/virtual-chain analysis, member naming and the
container-shape facade. It opens the module closure in `BindingModelPass`, so an addon resolves a
base class that lives in another module's model.

`TypeSystem` is the regex-registered handler set (`Types/*.cs`): a type spelling maps to its
managed surface, ABI/C++ shape and marshalling through one handler. `ParamHandlers` does the same
for parameter passing (optional struct, in/out string, out, ref, plain input). The mainline calls
`ManagedType`/`ParamFor` and never tests `IsQList`/`IsOut` itself.

## Current status

All 18 modules are byte-identical to the old generator, both the generated C# tree and the native
`.cpp`. The switch was made only after that gate passed:

```powershell
# for every module in the old regen script
dotnet run --project gen-next/gen-next.csproj -c Release -- `
  codegen model/qtsharp.qtcore.json out/gen-next/qtcore --module qtcore
diff -r out/qtcore/generated out/gen-next/qtcore/generated
```

The combined model still produces only the shared native glue with `--native-only`, exactly like
the old generator. `tools/regen-api.ps1` now runs `qtgen-next codegen` for every module; `qtgen`
itself remains the extractor/splitter.

Transitional dependency: `gen-next.csproj` references `../gen/gen.csproj` for `ApiTypes`, the
hand-maintained table of runtime structs and C++ struct names. The table moves into the shared
model layer when the remaining generator-side tables are lifted out of the old project.

## Layout

| Path | Purpose |
|---|---|
| `Model/` | JSON document, options and the model's flag vocabulary |
| `Context/` | per-run state, module catalog and dependency closure |
| `Passes/` | ordered model transformations |
| `Types/` | type and parameter handlers, plus the type system |
| `Core/` | the binding model: vocabulary, hierarchy, members, marshalling |
| `Emitters/` | C# and C++ output |
| `Output/` | `CodeWriter` and file set |
