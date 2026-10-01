# KAPE-MEDIA Releases

Repositorio de binarios release de [KAPE-MEDIA](../). Contiene los
archivos `.iso` finales que NO se commitean al repo principal (por
limite de 100 MB de GitHub) y se distribuyen via GitHub Releases.

## Que hay aca

Cada release es un tag `v<VERSION>` con el ISO adjunto:

```
v5.2.18  -> KAPE-MEDIA-v5.2.18.iso  (561 MiB)
v5.2.17  -> KAPE-MEDIA-v5.2.17.iso
v5.2.16  -> KAPE-MEDIA-v5.2.16.iso
...
```

## Como descargar un release

Desde la linea de comandos:

```bash
# Ultimo release
gh release download --repo Beacon-Lab-IR/kape-media

# Release especifico
gh release download v5.2.18 --repo Beacon-Lab-IR/kape-media

# Archivo especifico a un directorio
gh release download v5.2.18 \
    --repo Beacon-Lab-IR/kape-media \
    --pattern "KAPE-MEDIA-*.iso" \
    --dir /tmp/
```

O desde la web: https://github.com/Beacon-Lab-IR/kape-media/releases/latest

## Como se publica un release

### Opcion A: Manual con gh CLI

Desde el repo principal de codigo, despues de compilar y generar el ISO:

```bash
cd /path/to/kape-media-repo

# Compilar
dotnet build KapeUi/KapeUi.csproj -c Release
dotnet build RamCaptureUi/RamCaptureUi.csproj -c Release

# Generar ISO
./scripts/build.sh  # o build.sh equivalente en macOS/Linux

# Tag + push al repo de codigo
git tag v5.2.18
git push origin v5.2.18

# Subir ISO como Release al repo de binarios
gh release create v5.2.18 \
    --repo Beacon-Lab-IR/kape-media \
    --target main \
    --title "KAPE-MEDIA v5.2.18" \
    --notes-file RELEASE-NOTES.md \
    dist/KAPE-MEDIA-v5.2.18.iso
```

### Opcion B: Automatico con GitHub Actions (recomendado)

Este repo tiene un workflow en `.github/workflows/release.yml` que detecta
tags `v*` en el repo de codigo, compila, genera el ISO, y crea el release
automaticamente.

Para activarlo:

1. En el repo principal `kape-media`, ir a Settings -> Secrets and variables
   -> Actions -> New repository secret.
2. Crear dos secrets:
   - `BEACONLAB_RELEASES_TOKEN`: un GitHub PAT con scope `repo` (solo
     sobre `Beacon-Lab-IR/kape-media`).
   - `BEACONLAB_RELEASES_REPO`: `Beacon-Lab-IR/kape-media`
3. Cada vez que se pushee un tag `v*` al repo principal, el workflow:
   - Compila las dos GUIs.
   - Genera el ISO con `hdiutil makehybrid` o `genisoimage`.
   - Calcula SHA-256.
   - Clona el repo de releases y sube el ISO como binario.
   - Crea el Release en `Beacon-Lab-IR/kape-media` con las notas.

## Como verificar integridad

```bash
# Descargar ISO y SHA-256 companion
gh release download v5.2.18 \
    --repo Beacon-Lab-IR/kape-media \
    --pattern "KAPE-MEDIA-*" \
    --dir /tmp/kape-media-v5.2.18/

# Verificar
cd /tmp/kape-media-v5.2.18/
shasum -a 256 -c SHA256SUMS.txt
```

Si la verificacion falla, NO uses el ISO. Reportar el SHA-256 esperado
vs obtenido al equipo IR.

## Por que Releases y no el tree

- El tree de `kape-media` tiene solo codigo fuente (~pocos MB). Clonar es rapido.
- Los `.iso` (~560 MB) van como assets de GitHub Releases (<2 GB por archivo).
- Asi el historial de git no se infla y bajar un ISO no requiere clonar el repo.
