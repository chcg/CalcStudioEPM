# Third-party notices

CalcStudio for Oracle EPM is licensed under the GNU GPL v3.0 (see [LICENSE](LICENSE)).
It uses or is built with the components below, each under its own license. All of them are
compatible with the GPL v3.0.

## Newtonsoft.Json

Shipped next to the plugin as `Newtonsoft.Json.dll` (restored from NuGet at build time).

- Project: https://www.newtonsoft.com/json
- License: MIT

```
The MIT License (MIT)

Copyright (c) 2007 James Newton-King

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## DllExport

Build-time tool that adds the native exports Notepad++ calls. Included under
`CalcStudioEPM/packages/DllExport.1.8.0/`, with its own `LICENSE.txt` and
`3rd-party-NOTICES.txt`; it is not shipped in the plugin DLL.

- Project: https://github.com/3F/DllExport
- License: MIT — Copyright (c) 2009-2015 Robert Giesecke; Copyright (c) 2016-2025 Denis Kuzmin
  and DllExport contributors. Full text: `CalcStudioEPM/packages/DllExport.1.8.0/LICENSE.txt`.

## Notepad++ plugin interface

`CalcStudioEPM/NppPlugin/PluginBase.cs` (the `NppPluginNET` FuncItem / NppData structures and
message enums) follows the plugin infrastructure of
[NotepadPlusPlusPluginPack.Net](https://github.com/kbilsted/NotepadPlusPlusPluginPack.Net)
(Apache License 2.0), itself a fork of UFO's NppPlugin.NET. Message and notification values
are those defined by Notepad++'s `Notepad_plus_msgs.h` and Scintilla's `Scintilla.h`.

## Oracle EPM Groovy API signatures

`CalcStudioEPM/Resources/groovy_api.json` lists type, method, parameter and return-type names of
the Oracle EPM Groovy API, compiled from Oracle's public EPM Groovy Scripting Reference so that
autocomplete can offer them. It contains no description text from that reference. Calc Script
function names and syntax lines in `CalcStudioEPM/Config` are likewise limited to names and
parameter lists. Oracle, Hyperion and Essbase are registered trademarks of Oracle and/or its
affiliates; this project is not affiliated with Oracle.
