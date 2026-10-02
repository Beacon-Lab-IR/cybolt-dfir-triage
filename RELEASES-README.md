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

### Opcion A: GitHub Actions (recomendado)

Workflow: `.github/workflows/release.yml`

1. Actions → **Build ISO and publish release** → Run workflow
2. O push de un tag: `git tag v5.2.19 && git push origin v5.2.19`

El job:
- Copia `iso/` (scripts/manual Cybolt) a `staging/`
- Descarga `kape.zip` desde `Beacon-Lab-IR/armeria` (`KAPE/kape.zip`) y verifica SHA-256
- Extrae `kape.exe`, `Targets/`, `Documentation/`
- Compila las dos GUIs y descarga winpmem
- Genera el ISO y crea/actualiza el Release en este repo

No hace falta secret extra: usa `GITHUB_TOKEN`.

### Opcion B: Manual con gh CLI

Si tenes `staging/` local completo:

```bash
./build.sh 5.2.19

gh release create v5.2.19 \
    --repo Beacon-Lab-IR/kape-media \
    --title "KAPE-MEDIA v5.2.19" \
    --notes "SHA en output de build.sh" \
    dist/KAPE-MEDIA-v5.2.19.iso
```

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
