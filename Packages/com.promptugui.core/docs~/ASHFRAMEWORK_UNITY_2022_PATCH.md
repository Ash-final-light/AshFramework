# AshFramework Unity 2022 Compatibility Patch

This embedded package is based on PromptUGUI commit
`90b706cc699f342206d5b74b9d878691caf97d71`.

`Runtime/Controls/Internal/TmpPrimer.cs` uses the TextMeshPro 4 API on Unity 6
and the equivalent TextMeshPro 3.0 API on Unity 2022.3. The upstream package
declares Unity 2022.3 support, but commit `6b7f668de549f969e7be29d57520c77a3ebcda98`
introduced the Unity 6-only member names without a version guard.

Keep the embedded package and the repository skills under `.agents/skills`
on the same upstream revision when updating PromptUGUI.

The upstream package tests are retained under `Tests~` for source inspection but
are not imported into the host project. As an embedded package, a regular `Tests`
directory would be compiled automatically; the current upstream tests require a
newer NUnit API and the Input System package, neither of which is a runtime
dependency of AshFramework.
