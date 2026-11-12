# Ktool — Revit 2024 Add-in

Ktool is a C# / WPF Autodesk Revit 2024 add-in. The original solution is named **Dim**; keep `Dim.sln` and `Dim.csproj` until all namespaces, manifests, and references are migrated.

## Import status

The repository is initialized; **the complete 132-file source archive has not yet been imported**. To import locally, download `Ktool_GitHub_Source.zip` from the ChatGPT conversation and run `Import-Ktool.ps1` on Windows.

## Prerequisites

- Autodesk Revit 2024
- Visual Studio with .NET Framework 4.8 targeting pack, desktop/WPF tools
- A local Revit 2024 installation to supply Revit API references

## After importing

Build `Dim.sln` using Release / x64; use `-p:SkipRevitDeploy=true` for builds which should not install the add-in.

**Important:** Source import and GitHub upload are not evidence of a successful Revit runtime build. Review/test the add-in on a copy of an RVT.
