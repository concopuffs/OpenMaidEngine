# Local third-party tools

This directory is reserved for optional, machine-local binaries. Its contents are ignored by Git;
only this README and `.gitignore` are repository content.

The installed-data archive parity test can use `BinExtractALF.exe` here together with its matching
`LzssCpp.dll`. These files are not required by the runtime or the asset-independent test suite, and
their provenance and redistribution terms have not been verified. Do not commit them. The historical
local copies audited on 2026-08-03 had these SHA-256 values:

- `BinExtractALF.exe`: `46167CDF3DA1F02733CE1E7F0DA99D155E934EDE0987CA79C8927F48FEB25FE2`
- `LzssCpp.dll`: `596B9DDB07CA5C29CEE609E846F1512654EDEC56310A90D6C2A6951BE1E39FE1`

`pe-sieve32.exe` is no longer used. The failed IAT reconstruction experiment is retained in
`docs/engine-re.md`; the Frida import-map workflow replaced it.
