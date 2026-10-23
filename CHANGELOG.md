# Changelog

Versions are `0.015.N`: the release (set in `CalcStudioEPM.csproj`) and the build number. The
DLL's file version is `0.15.N.0`.

## v0.015 — first public release

- Published as open source under the GNU GPL v3.0.
- Renamed from `NppEPMPlugin` to `CalcStudioEPM`: DLL, plugin folder, project and namespaces.
  Settings are copied from `%AppData%\Notepad++\plugins\NppEPMPlugin` on the first start, and a
  warning appears while the old `plugins\NppEPMPlugin` copy is still installed.
- **About CalcStudio…** at the end of the Plugins menu: what the plugin does, version and build
  date, author and links, the "not an Oracle product" disclaimer, the license, and
  *Copy version info* for bug reports. Its text lives in `CalcStudioEPM/AboutInfo.cs`.
- Every build is stamped with its own build number (`CalcStudioEPM/BuildNumber.txt`).
- `build_deploy.bat` finds MSBuild with `vswhere` and works from any folder.
