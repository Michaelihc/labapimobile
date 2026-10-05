# LabAPIMobile

Follow the parent workspace instructions. Reply in English unless requested otherwise.

## Reference inputs

- `.refereces/` contains the supplied server ZIP and two APKs. Treat them as original reference inputs; write patched assemblies, rebuilt APKs and extracted files to separate output directories.
- `.refereces/scpsl-metarepo/` is a junction to `<scpsl-plugins-metarepo>`. It is external reference material, not this project's implementation directory. Do not edit through the junction without explicit authorization to change that repository.
- Read the metarepo's `AGENTS.md` and `.references/AGENTS.md`, then the relevant reference subfolder's instructions before researching APIs or native behavior.
- Official LabAPI source: `.refereces/scpsl-metarepo/.references/LabAPI/LabApi/`.
- Native SL server reference: `.refereces/scpsl-metarepo/.references/Decompiled/DedicatedServer/Assembly-CSharp/`.
- EXILED source mirror: `.refereces/scpsl-metarepo/.references/Reference Plugins/EXILED/`.
- Carl Mod analysis and tooling: `../scpsl-mobile-analysis-20261005/`.

## Compatibility

Use the Carl Mod server assemblies to verify implementation signatures. Similar type names in the SL metarepo do not establish compatibility. Client C# exports in the analysis workspace are IL2CPP metadata views with placeholder method bodies; inspect native code to establish client behavior.
