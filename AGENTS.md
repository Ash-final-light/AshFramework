# AshFramework Agent Guide

## Project Baseline

- This is a Unity 2022.3 project. PromptUGUI therefore requires UniTask; do not remove it as if this were Unity 6.
- PromptUGUI lint tools target .NET 10. Verify `dotnet --list-sdks` includes a 10.x SDK before running them.
- Treat Unity `.meta` files as part of their assets. Move, add, or delete an asset together with its `.meta` file.
- Do not edit generated files under `Library/`, `Temp/`, `obj/`, or `Assets/Packages/`.
- `Assets/Packages/` is restored by NuGetForUnity from `Assets/packages.config` and is intentionally not versioned. On a fresh checkout, let the restore finish (or use `NuGet > Restore Packages`) before diagnosing missing R3 types.
- Keep changes scoped. Do not replace existing AshFramework UI utilities unless the task explicitly requests a migration.

## PromptUGUI Skill Routing

Use the repository skills under `.agents/skills/` whenever the task matches:

- `authoring-promptugui-xml`: create or modify `.ui.xml` documents, layouts, controls, styles, themes, variants, animations, or navigation markup.
- `authoring-promptugui-pxl`: create or modify `.pxl` sprites, icons, button states, or 9-slice artwork.
- `scripting-promptugui-csharp`: write C# that loads PromptUGUI documents, opens screens, gets controls, binds R3 streams, or defines custom controls.
- `using-promptugui-addressables`: integrate PromptUGUI documents, locale files, or sprite sets with Unity Addressables. Do not use Addressables APIs unless the package is installed.

The skills are copied from PromptUGUI commit `90b706cc699f342206d5b74b9d878691caf97d71`. When the package revision changes, refresh all four skill directories from the same revision in the same change.

## PromptUGUI Conventions

- Wrap every player-facing string authored in C# with `PromptUGUI.Application.UI.Tr("...")`.
- Do not wrap text inside `.ui.xml`; PromptUGUI extracts XML text directly.
- Do not wrap logs, exception text, paths, URLs, asset keys, identifiers, JSON fragments, format strings, or punctuation-only values.
- Prefer `UI.LoadDocumentAsync` and explicit resolver setup over direct filesystem access.
- Track R3 subscriptions with `.AddTo(screen)` or `.AddTo(control)` according to the required lifetime.
- Do not edit PromptUGUI source in `Library/PackageCache`. Pin or upgrade the package through `Packages/manifest.json`.

## Screen Adaptation Ownership

- Legacy prefab/uGUI screens may continue using `AshFramework.UI.ScreenAdaptation.Components.SafeArea` and `CanvasWidthAdjuster`.
- PromptUGUI screens use `<Screen reference="...">`, orientation variants, and PromptUGUI `<SafeArea>`.
- Never attach AshFramework `SafeArea` or `CanvasWidthAdjuster` to the same Canvas or RectTransform hierarchy managed by a PromptUGUI Screen. Two adaptation systems would both modify layout and scaling.
- Existing hierarchy and raycast editor helpers can be used with either UI path because they do not own runtime screen adaptation.

## Validation

After changing a `.ui.xml` file, locate the installed package and run its linter:

```powershell
$promptPackage = Get-ChildItem Library/PackageCache -Directory -Filter 'com.promptugui.core@*' | Select-Object -First 1
dotnet run --project "$($promptPackage.FullName)/.lint/UIXmlLint" -- <path-to-ui.xml>
```

After changing a `.pxl` file, render it and inspect the generated PNG; a successful parse is not sufficient visual validation:

```powershell
$promptPackage = Get-ChildItem Library/PackageCache -Directory -Filter 'com.promptugui.core@*' | Select-Object -First 1
dotnet run --project "$($promptPackage.FullName)/.lint/PxlPreview" -- <path-to-file.pxl> --scale 16 --guides
```

For layered `.pxl` files, also pass `--layers`. Never write preview PNGs into a SpriteSet source folder.

Before reporting completion for runtime or C# changes:

1. Let Unity finish package resolution and compilation.
2. Check the Unity Console for compile and import errors.
3. Run the relevant EditMode or PlayMode tests when present.
4. For UI layout changes, open `Tools > PromptUGUI > UI Preview` (`F8`) and verify the affected screen in all relevant orientations and pages.
