# KAPE-MEDIA Releases

Los `.iso` finales NO se commitean al tree (limite 100 MB de GitHub) y se
distribuyen como assets de [GitHub Releases](https://github.com/Beacon-Lab-IR/kape-media/releases).

## Que hay aca

Cada release es un tag `v<VERSION>` con el ISO adjunto:

```
v5.2.18  -> KAPE-MEDIA-v5.2.18.iso  (561 MiB)
v5.2.17  -> KAPE-MEDIA-v5.2.17.iso
...
```

## Como descargar

```bash
# Ultimo release
gh release download --repo Beacon-Lab-IR/kape-media

# Release especifico
gh release download v5.2.18 --repo Beacon-Lab-IR/kape-media

# A un directorio
gh release download v5.2.18 \
    --repo Beacon-Lab-IR/kape-media \
    --pattern "KAPE-MEDIA-*.iso" \
    --dir /tmp/
```

Web: https://github.com/Beacon-Lab-IR/kape-media/releases/latest

## Como publicar un release

### Opcion A: Manual con gh CLI (recomendado)

`staging/` (incluye `Tools/kape.zip` ~553 MB) no esta en git. Genera el ISO
en tu maquina y subilo como Release:

```bash
cd /path/to/kape-media

./build.sh 5.2.19

gh release create v5.2.19 \
    --repo Beacon-Lab-IR/kape-media \
    --title "KAPE-MEDIA v5.2.19" \
    --notes "Ver RELEASES-README / README" \
    dist/KAPE-MEDIA-v5.2.19.iso
```

### Opcion B: GitHub Actions

Hay un workflow en `.github/workflows/release.yml` (tag `v*` o
`workflow_dispatch`). Compila las GUIs, descarga winpmem y publica el
Release en este mismo repo con `GITHUB_TOKEN`.

**Limitacion:** en `ubuntu-latest` falla si `staging/` no esta completo
(falta `kape.exe`, `Tools/kape.zip`, etc.). Solo sirve tal cual en un
runner que ya tenga ese staging, o cuando se agregue un paso para
restaurarlo. Hasta entonces, usa la opcion A.

## Como verificar integridad

```bash
gh release download v5.2.18 \
    --repo Beacon-Lab-IR/kape-media \
    --pattern "KAPE-MEDIA-*.iso" \
    --dir /tmp/kape-media-v5.2.18/

cd /tmp/kape-media-v5.2.18/
shasum -a 256 KAPE-MEDIA-v5.2.18.iso
# comparar con el SHA-256 de las notas del Release
```

Si no coincide, NO uses el ISO.

## Por que Releases y no el tree

- El tree tiene solo codigo fuente (~pocos MB). Clonar es rapido.
- Los `.iso` (~560 MB) van como assets de Releases (<2 GB por archivo).
- El historial de git no se infla y bajar un ISO no requiere clonar.
