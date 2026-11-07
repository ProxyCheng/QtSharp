# qtgen

The QtSharp binding generator. One executable owns the whole pipeline: model extraction
(`extract2`), the per-module split (`split-core`), the module descriptor (`module`) and code
generation (`codegen`). `tools/regen-api.ps1` and the CI/release workflows build this project and
call `qtgen codegen` for every module.

## Layering

```
Program
  └─ GeneratorDriver                 one run: load → passes → emit → save
       ├─ ModelDocument              the JSON model, opened once
       ├─ ModuleCatalog              module metadata and dependency closure
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

The generator reproduces the old output byte for byte for all 18 modules, both the C# tree and the
native `.cpp`; the only deliberate difference is the qualification of a namespaced enum used as a
container element (the old output failed to compile Qt3DRender on Linux). The combined model still
produces only the shared native glue with `--native-only`.

```powershell
dotnet run --project gen/gen.csproj -c Release -- `
  codegen model/qtsharp.qtcore.json out/gen/qtcore --module qtcore
diff -r out/qtcore/generated out/gen/qtcore/generated
```

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
| `ApiModel*.cs`, `ClangExtractor.cs`, `CoreSplit.cs` | model extraction, module registry and split |
| `Program.cs`, `HostPlatform.*` | the `qtgen` command line and its host paths |
