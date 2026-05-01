# GameNetworkingSockets — Self-Contained Native Build Rehberi

Bu rehber, **OpenSSL + protobuf + abseil bağımlılıklarını içine gömülmüş** tek bir native DLL/SO üretmek için adım adım talimatları içerir. Sonuç:

- **Windows**: `GameNetworkingSockets.dll` (~6 MB, hiçbir bağımlılık DLL'i gerektirmez)
- **Linux**: `libGameNetworkingSockets.so` (~12 MB, sadece libc/libstdc++ gerektirir)

Bu DLL'ler doğrudan `bindings/csharp/native/{win-x64,linux-x64}/` altına kopyalanır ve NuGet paketine dahil edilir.

---

## Önkoşullar

### Windows (native DLL için)

- **Visual Studio 2022** Community/Professional/Enterprise — C++ workload kurulu
- **MSVC toolset 14.40+** (17.10+ ile gelir; 14.38 gibi eski toolset'ler vcpkg ile uyumsuz STL sembol hatası verir)
- **vcpkg** — `C:\vcpkg` altında kurulu (zaten kurulu varsayılır)
- **Windows SDK 10.0.26100+**

### Linux build (WSL Ubuntu)

- **Ubuntu 24.04** WSL distro (`docker-desktop` distrosu **uygun değil**, gerçek bir Ubuntu lazım)
- **Ev dizininde vcpkg** (`~/vcpkg`, fork'un kendi içinde değil)
- Apt paketleri: `cmake`, `g++` (13+), `git`, `ninja-build`, `pkg-config`, `autoconf`, `automake`, `libtool`, `zip`, `unzip`, `tar`, `build-essential`

### Genel

- **.NET 6 SDK veya üstü** (NuGet pack için)
- **PowerShell 5.1+** (Windows komutları için)

---

## 0. Tek seferlik kurulum

### 0.1 WSL Ubuntu kontrolü ve eksik araç kurulumu

```bash
wsl -l -v
```

`Ubuntu` distrosu yoksa:

```bash
wsl --install Ubuntu
```

Eksik araçları kur:

```bash
wsl -d Ubuntu -- bash -lc 'sudo apt-get update -qq && sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq pkg-config autoconf automake libtool zip unzip tar build-essential ninja-build'
```

### 0.2 vcpkg kurulumu (WSL Ubuntu)

WSL'in **kendi filesystem**'ine kur (`/mnt/c/...` üzerine değil — çok yavaş):

```bash
wsl -d Ubuntu -- bash -lc 'cd ~ && git clone https://github.com/microsoft/vcpkg.git && cd vcpkg && ./bootstrap-vcpkg.sh -disableMetrics'
```

**Not:** `--depth 1` yapma — vcpkg'in baseline mekanizması full git history'ye ihtiyaç duyar.

### 0.3 vcpkg kurulumu (Windows)

`C:\vcpkg` zaten kurulu olmalı. Yoksa:

```powershell
cd C:\
git clone https://github.com/microsoft/vcpkg.git
.\vcpkg\bootstrap-vcpkg.bat -disableMetrics
```

### 0.4 vcpkg.json baseline güncellemesi

Repo'nun kök dizinindeki `vcpkg.json`'ın `builtin-baseline` alanı, kullandığın vcpkg'in HEAD commit'i ile **aynı olmalı**. Aksi takdirde "failed to git show baseline.json" hatası alırsın.

Hem Windows hem WSL vcpkg'inin HEAD'ini al:

```powershell
# Windows vcpkg
cd C:\vcpkg ; git rev-parse HEAD
```

```bash
# WSL vcpkg
wsl -d Ubuntu -- bash -lc 'cd ~/vcpkg && git rev-parse HEAD'
```

İkisi farklıysa, hangisinde build edeceksen onun HEAD'ini `vcpkg.json`'a yaz:

```json
"builtin-baseline": "<vcpkg-HEAD-commit-hash>",
```

---

## 1. Linux build (WSL)

### 1.1 Repo'yu WSL'in iç fs'ine kopyala

`/mnt/c/...` üzerinden build çok yavaş (10x), permission sorunları çıkar. Kopyala:

```bash
wsl -d Ubuntu -- bash -lc 'mkdir -p ~/build && rm -rf ~/build/gns && cp -r "/mnt/c/Users/NAMLI/OneDrive/Masaüstü/GameNetworkingSockets-fork" ~/build/gns && cd ~/build/gns && rm -rf build-linux build-static vcpkg_installed'
```

### 1.2 vcpkg.json baseline'ını WSL HEAD ile güncelle

Eğer Windows ve WSL vcpkg HEAD'leri farklıysa, WSL kopyasındaki `vcpkg.json`'u güncelle:

```bash
wsl -d Ubuntu -- bash -lc '
  HEAD=$(cd ~/vcpkg && git rev-parse HEAD)
  cd ~/build/gns
  sed -i "s/\"builtin-baseline\": \".*\"/\"builtin-baseline\": \"$HEAD\"/" vcpkg.json
  cat vcpkg.json | grep baseline
'
```

### 1.3 CMake configure

```bash
wsl -d Ubuntu -- bash -lc '
  cd ~/build/gns
  cmake -S . -B build-linux -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE=$HOME/vcpkg/scripts/buildsystems/vcpkg.cmake \
    -DVCPKG_TARGET_TRIPLET=x64-linux \
    -DBUILD_SHARED_LIBS=ON \
    -DBUILD_EXAMPLES=OFF \
    -DBUILD_TESTS=OFF \
    -DCMAKE_BUILD_TYPE=Release
'
```

**İlk çalıştırmada vcpkg OpenSSL ve protobuf'u sıfırdan derler — 5-10 dakika sürer.** Sonraki build'lerde cache'lenir, çok hızlı olur.

`x64-linux` triplet'i default'ta **static library** üretir. GNS shared (`.so`) olarak derlenir, içine bağımlılıklar gömülür.

### 1.4 Ninja ile derle

```bash
wsl -d Ubuntu -- bash -lc 'cd ~/build/gns/build-linux && ninja'
```

Sonuç: `~/build/gns/build-linux/bin/libGameNetworkingSockets.so` (~12 MB)

### 1.5 Bağımlılıkları doğrula

```bash
wsl -d Ubuntu -- bash -lc 'ldd ~/build/gns/build-linux/bin/libGameNetworkingSockets.so'
```

**Sadece şunları görmelisin:**
- `linux-vdso.so.1`
- `libstdc++.so.6`
- `libm.so.6`
- `libgcc_s.so.1`
- `libc.so.6`
- `/lib64/ld-linux-x86-64.so.2`

OpenSSL (`libssl.so`, `libcrypto.so`) veya protobuf (`libprotobuf.so`) görünüyorsa **build yanlış** — bağımlılıklar gömülmemiş demektir.

### 1.6 binding klasörüne kopyala

```bash
wsl -d Ubuntu -- bash -lc 'cp ~/build/gns/build-linux/bin/libGameNetworkingSockets.so "/mnt/c/Users/NAMLI/OneDrive/Masaüstü/GameNetworkingSockets-fork/bindings/csharp/native/linux-x64/"'
```

---

## 2. Windows build

### 2.1 vcpkg paketlerini kontrol et

`C:\vcpkg\installed\x64-windows-static-md\lib\` altında şu lib'ler olmalı:
- `libssl.lib`
- `libcrypto.lib`
- `libprotobuf.lib`
- `libprotobuf-lite.lib`
- `absl_*.lib` (çoklu)

Yoksa vcpkg manifest mode CMake configure sırasında otomatik kuracak (uzun sürer).

### 2.2 vcpkg.json baseline'ını Windows HEAD ile güncelle

```powershell
$wHead = (& git -C C:\vcpkg rev-parse HEAD).Trim()
$repo = "c:\Users\NAMLI\OneDrive\Masaüstü\GameNetworkingSockets-fork"
(Get-Content "$repo\vcpkg.json") -replace '"builtin-baseline": ".*"', "`"builtin-baseline`": `"$wHead`"" | Set-Content "$repo\vcpkg.json"
```

### 2.3 Doğru MSVC toolset'i seç

**KRİTİK:** vcpkg paketlerinin uyumlu olduğu yeni STL sembolleri için MSVC **14.40+** lazım. Mevcut toolset'leri listele:

```powershell
Get-ChildItem "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC" -Directory | Select-Object Name
```

Eğer sadece `14.38.33130` gibi eski toolset varsa, Visual Studio Installer'dan **MSVC v143 — VS 2022 C++ x64/x86 build tools (Latest)** komponentini kur.

### 2.4 vcvarsall + Ninja generator ile configure

Visual Studio generator (`-G "Visual Studio 17 2022"`) `-T version=14.44` flag'ini doğru iletmiyor — eski toolset kullanmaya devam ediyor. **Ninja generator** ile `vcvarsall.bat -vcvars_ver=14.44` environment'ı garanti çalışır.

```powershell
$repo = "c:\Users\NAMLI\OneDrive\Masaüstü\GameNetworkingSockets-fork"
Remove-Item -Recurse -Force "$repo\build-win-static" -ErrorAction SilentlyContinue

$vcvars = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat"
$cmake = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$ninja = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"

cmd /c "`"$vcvars`" x64 -vcvars_ver=14.44 && `"$cmake`" -S `"$repo`" -B `"$repo\build-win-static`" -G Ninja -DCMAKE_MAKE_PROGRAM=`"$ninja`" -DCMAKE_BUILD_TYPE=Release -DCMAKE_TOOLCHAIN_FILE=C:/vcpkg/scripts/buildsystems/vcpkg.cmake -DVCPKG_TARGET_TRIPLET=x64-windows-static-md -DBUILD_SHARED_LIBS=ON -DBUILD_EXAMPLES=OFF -DBUILD_TESTS=OFF -DProtobuf_USE_STATIC_LIBS=ON"
```

**KRİTİK FLAG'LER:**
- `VCPKG_TARGET_TRIPLET=x64-windows-static-md` — bağımlılıklar static, ama CRT shared (modern Windows uyumlu)
- `Protobuf_USE_STATIC_LIBS=ON` — `PROTOBUF_USE_DLLS` define'ını engellemek için. Yoksa "unresolved external `__declspec(dllimport)`" linker hatası alırsın.
- `BUILD_SHARED_LIBS=ON` — GNS'i DLL olarak derle (içine bağımlılıklar gömülü)
- `-vcvars_ver=14.44` — yeni MSVC toolset'i zorla. Bu olmadan eski 14.38 kullanılır ve `__std_find_first_of_trivial_pos_1` gibi STL sembol hataları alırsın.

Configure cache'de `CMAKE_CXX_COMPILER` doğru sürümü göstermelidir:

```powershell
Select-String -Path "$repo\build-win-static\CMakeCache.txt" -Pattern "CMAKE_CXX_COMPILER:"
```

Çıktı `MSVC\14.44.35207\bin\Hostx64\x64\cl.exe` benzeri olmalı (14.38 değil).

### 2.5 Ninja ile derle

```powershell
$repo = "c:\Users\NAMLI\OneDrive\Masaüstü\GameNetworkingSockets-fork"
$vcvars = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat"
$cmake = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"

cmd /c "`"$vcvars`" x64 -vcvars_ver=14.44 && `"$cmake`" --build `"$repo\build-win-static`""
```

Sonuç: `build-win-static\bin\GameNetworkingSockets.dll` (~6 MB)

### 2.6 Bağımlılıkları doğrula

```powershell
$dll = "c:\Users\NAMLI\OneDrive\Masaüstü\GameNetworkingSockets-fork\build-win-static\bin\GameNetworkingSockets.dll"
$vcvars = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat"
cmd /c "`"$vcvars`" x64 -vcvars_ver=14.44 >nul && dumpbin /dependents `"$dll`""
```

**Sadece şu Windows sistem DLL'lerini görmelisin:**
- `WS2_32.dll`, `CRYPT32.dll`, `WINMM.dll`, `IPHLPAPI.DLL` — Windows networking
- `KERNEL32.dll`, `USER32.dll`, `ADVAPI32.dll`, `dbghelp.dll` — Windows core
- `MSVCP140.dll`, `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll` — VC++ Redistributable
- `api-ms-win-crt-*.dll` — Universal CRT

`libssl-3-x64.dll`, `libcrypto-3-x64.dll`, `abseil_dll.dll`, `libprotobuf*.dll` görünüyorsa **build yanlış**.

### 2.7 binding klasörüne kopyala

```powershell
$repo = "c:\Users\NAMLI\OneDrive\Masaüstü\GameNetworkingSockets-fork"
Copy-Item "$repo\build-win-static\bin\GameNetworkingSockets.dll" "$repo\bindings\csharp\native\win-x64\GameNetworkingSockets.dll" -Force
```

`bindings/csharp/native/win-x64/` altında **sadece** `GameNetworkingSockets.dll` olmalı. Eski 5 bağımlılık varsa sil:

```powershell
@("abseil_dll.dll","libcrypto-3-x64.dll","libssl-3-x64.dll","libprotobuf.dll","libprotobuf-lite.dll") | ForEach-Object {
    $f = "$repo\bindings\csharp\native\win-x64\$_"
    if (Test-Path $f) { Remove-Item $f -Force }
}
```

---

## 3. NuGet paketleme ve push

### 3.1 Sürüm yükselt

`bindings/csharp/GameNetworkingSockets.csproj` içinde `<Version>` alanını artır:

```xml
<Version>1.7.0</Version>
```

### 3.2 Pack

```powershell
$repo = "c:\Users\NAMLI\OneDrive\Masaüstü\GameNetworkingSockets-fork"
& dotnet pack "$repo\bindings\csharp\GameNetworkingSockets.csproj" -c Release
```

Sonuç: `bindings\csharp\bin\Release\GameNetworkingSockets.CSharp.<sürüm>.nupkg`

### 3.3 Paket içeriğini doğrula

```powershell
$nupkg = "c:\Users\NAMLI\OneDrive\Masaüstü\GameNetworkingSockets-fork\bindings\csharp\bin\Release\GameNetworkingSockets.CSharp.1.7.0.nupkg"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::OpenRead($nupkg).Entries | Sort-Object FullName | Format-Table FullName, Length
```

**Beklenen içerik:**
- `lib/netstandard2.1/GameNetworkingSockets.CSharp.dll` (~36 KB, managed)
- `runtimes/win-x64/native/GameNetworkingSockets.dll` (~6 MB, self-contained)
- `runtimes/linux-x64/native/libGameNetworkingSockets.so` (~12 MB, self-contained)
- `build/GameNetworkingSockets.CSharp.targets` (~600 byte, otomatik kopyalama mantığı)

Toplam paket boyutu ~7 MB civarı olmalı. Eğer 1 MB altındaysa native DLL'ler eksik.

### 3.4 Local feed'e push

```powershell
& dotnet nuget push "c:\Users\NAMLI\OneDrive\Masaüstü\GameNetworkingSockets-fork\bindings\csharp\bin\Release\GameNetworkingSockets.CSharp.1.7.0.nupkg" --source "C:\local-nuget-source"
```

### 3.5 Tüketici tarafında doğrula

Throwia server veya başka bir tüketici projede:

```bash
dotnet add package GameNetworkingSockets.CSharp --version 1.7.0
dotnet build
```

Build output (`bin/Debug/net6.0/`) altında **sadece** şunlar olmalı (GNS ile alakalı):
- `GameNetworkingSockets.CSharp.dll` (managed)
- `GameNetworkingSockets.dll` (native, 6 MB)
- `runtimes/{win-x64,linux-x64}/native/...` (RID-spesifik kopyalar)

---

## 4. Sık karşılaşılan hatalar

### `failed to git show versions/baseline.json`

`vcpkg.json`'daki `builtin-baseline`, vcpkg'in HEAD commit'inden farklı. **Çözüm:** Bölüm 2.2 veya 1.2.

### `unresolved external symbol __declspec(dllimport) ... protobuf::...`

`Protobuf_USE_STATIC_LIBS=ON` flag'i eksik. Configure'a ekle.

### `unresolved external symbol __std_find_first_of_trivial_pos_1` (veya benzeri `__std_*`)

MSVC toolset eski (14.38 vs.). vcpkg paketleri yeni STL sembollerine ihtiyaç duyar. **Çözüm:** Bölüm 2.3 ve 2.4 — `vcvars_ver=14.44` ile yeni toolset zorla.

### `cmake: command not found`

CMake PATH'de değil. VS bundled cmake'i tam yolla çağır:
`C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe`

### Linux build'de `bash: not found`

Default WSL distrosu `docker-desktop` (Alpine). `wsl -d Ubuntu` ile Ubuntu'yu açıkça hedef al.

### Linux build çok yavaş

Repo `/mnt/c/...` üzerinde derleniyor. WSL'in iç fs'ine kopyala (`~/build/gns/`).

### Build output'ta hâlâ `libcrypto-3-x64.dll` vs. görünüyor

Tüketici projenin csproj'unda eski referans var. `Shared.csproj`'da `<Content Include="$(GNSNativePath)*">` gibi bir blok bağımlılıkları manuel kopyalıyordu. Sil — 1.6.0+ paketi artık kendi başına getiriyor.

---

## 5. Süre tahmini

| Adım | İlk çalıştırma | Sonraki (cache'li) |
|---|---|---|
| WSL araç kurulumu | ~5 dk | — |
| vcpkg bootstrap (WSL + Win) | ~1 dk | — |
| Linux configure (vcpkg deps) | ~10 dk | ~30 sn |
| Linux ninja build | ~3 dk | ~30 sn |
| Windows configure (vcpkg deps) | ~5 dk (varsa cache, 5 sn) | ~5 sn |
| Windows ninja build | ~3 dk | ~30 sn |
| NuGet pack + push | ~10 sn | ~10 sn |

**Toplam ilk seferde:** ~30 dk
**Sonraki build'lerde:** ~5 dk

---

## 6. Repo'da neyi commit'leyebilirsin

- `bindings/csharp/native/win-x64/GameNetworkingSockets.dll` — yeni self-contained sürüm
- `bindings/csharp/native/linux-x64/libGameNetworkingSockets.so` — yeni self-contained sürüm
- `bindings/csharp/GameNetworkingSockets.csproj` — sürüm bump
- `vcpkg.json` — baseline güncellemesi (upstream Valve repo'sundan ayrılır, bu fork'a özgü)

`build-win-static/`, `build-linux/`, `vcpkg_installed/` `.gitignore`'da olmalı.
