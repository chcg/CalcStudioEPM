# EPM Groovy API for autocomplete

`Resources/groovy_api.json` (embedded in the DLL) holds the EPM Groovy API's types, methods
(parameters and return types), enum constants and supertypes, compiled from Oracle's
EPM Groovy Scripting Reference. It keeps signatures only, no description text.

To rebuild it after Oracle updates the reference (from the project folder):

```bash
mkdir -p %TEMP%\groovydoc
curl -L -o %TEMP%\groovydoc\index-all.html https://docs.oracle.com/en/cloud/saas/enterprise-performance-management-common/groov/index-all.html
python -I tools/groovy_api/fetch_groovydoc.py %TEMP%\groovydoc
python -I tools/groovy_api/parse_groovydoc.py %TEMP%\groovydoc Resources/groovy_api.json
```
