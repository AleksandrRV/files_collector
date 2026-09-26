<!-- files-collector-report: 1 -->
# Files Collector report

Review the project.
Focus on the public API.

## Report metadata

```yaml
created_at: 2026-09-25T18:00:00.0000000+07:00
root: "<redacted>"
preset: "Golden"
prefix_preset: "Golden prefix"
included:
  full: 6
  signatures: 1
  listed: 3
excluded: 2
```

## File index

| # | Path | Mode | Bytes | Status |
|--:|---|---|---:|---|
| 1 | `assets/logo.png` | Listed | 48 | binary_extension |
| 2 | `config/settings.json` | Signatures | 83 | read |
| 3 | `data/blob.bin` | Listed | 512 | binary_file |
| 4 | `data/loose-object` | Listed | 180 | binary_file |
| 5 | `docs/legacy-1251.txt` | Full | 51 | read |
| 6 | `docs/notes-utf16.txt` | Full | 32 | read |
| 7 | `empty.txt` | Full | 0 | read |
| 8 | `README.md` | Full | 36 | read |
| 9 | `src/Program.cs` | Full | 111 | read |
| 10 | `vendor/lib/lib.txt` | Full | 8 | read |

## Files

### FILE 1 — `assets/logo.png`

```yaml
path: "assets/logo.png"
mode: Listed
bytes: 48
content: omitted
reason: binary_extension
```

### FILE 2 — `config/settings.json`

```yaml
path: "config/settings.json"
mode: Signatures
bytes: 83
encoding: utf-8
sha256: 235a8768724eb534d9948f3b69e06afb1226984d4148a8a86ce3a38e569ee192
extractor: json-structure-v1
```

```json
name : string
ports []
  [item] : number
features {}
  auth : boolean
```

### FILE 3 — `data/blob.bin`

```yaml
path: "data/blob.bin"
mode: Listed
bytes: 512
content: omitted
reason: binary_file
```

### FILE 4 — `data/loose-object`

```yaml
path: "data/loose-object"
mode: Listed
bytes: 180
content: omitted
reason: binary_file
```

### FILE 5 — `docs/legacy-1251.txt`

```yaml
path: "docs/legacy-1251.txt"
mode: Full
bytes: 51
encoding: windows-1251
sha256: b4b82df2e6cc00c0f23c100a3a2e82366dcf6e663fa6331e8f3557369aa57fbf
```

```
Привет, мир!
Это текст в кодировке Windows-1251.
```

### FILE 6 — `docs/notes-utf16.txt`

```yaml
path: "docs/notes-utf16.txt"
mode: Full
bytes: 32
encoding: utf-16-le
sha256: 705a017d17f8d29734b332158363105102f31f9df24df42e6af76f08669e649b
```

```
UTF-16 заметка
```

### FILE 7 — `empty.txt`

```yaml
path: "empty.txt"
mode: Full
bytes: 0
encoding: utf-8
sha256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
```

```

```

### FILE 8 — `README.md`

```yaml
path: "README.md"
mode: Full
bytes: 36
encoding: utf-8
sha256: 728b5b4addc39f958f27bb100a2985200778971ac57283a1475164ec9ccd2667
```

````markdown
# Demo

```bash
dotnet run
```
````

### FILE 9 — `src/Program.cs`

```yaml
path: "src/Program.cs"
mode: Full
bytes: 111
encoding: utf-8
sha256: ce605394ba8c2f0770f2b6fd54e2916d2a556c7977fcc5832213b5d5fbd3c9c7
```

```csharp
using System;

public static class Program
{
    public static void Main() => Console.WriteLine("Hi");
}
```

### FILE 10 — `vendor/lib/lib.txt`

```yaml
path: "vendor/lib/lib.txt"
mode: Full
bytes: 8
encoding: utf-8
sha256: b5e0dfe3c2b269568c488e74fdc56495a5729538ebc6ef36488c85a7d7a1730e
```

```
library
```
