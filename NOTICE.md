# NOTICE — Origin and attribution

## Upstream

This repository is a **modified fork / derivative** of:

- **Project:** Veyra (Veyra-NRVideo)
- **Upstream:** https://github.com/Likely7/Veyra-NRVideo
- **Upstream licence (original Veyra code):** GNU General Public License v3.0 (`LICENSE`)
- **Base corresponding source:** Veyra 2.1.6 application source

Original authors and contributors retain copyright on upstream code.
This fork does **not** claim ownership of the original Veyra project.

## This fork

- **Maintainer (modifications):** JACKYLI6207
- **Repository:** https://github.com/JACKYLI6207/Veyra-NRVideo-mods
- **Licence for combined work:** GPL-3.0 (same as upstream). Modifications are released under GPL-3.0.

### Changes in this fork (summary)

1. **Dual identical GPUs:** PCI-bus ordered slot labels（一号 / 二号）, stable adapter ids, settings UI fixes so same-model cards do not dual-check; upper PCIe slot (smaller PCI bus on the tested board) = 一号, listed first.
2. **Screen capture FPS:** custom numeric frame-rate limit (0–240 Hz), with commit-before-start so the value is applied when starting capture.
3. **Window follow rate:** when capturing a window with FPS follow (0), track the window’s live content present rate (duplicate WGC frames dropped), not the monitor refresh; monitor capture still follows display refresh.
4. **Compare split UI:** 0–100% split slider and swap-sides toggle for 原画/增强分屏 (drag on picture still works).
5. **Window chrome maximize:** always-visible min/max/close; maximize fills the monitor work area while keeping Pro/Node panels (distinct from picture-only fullscreen).
6. **Split + frame generation:** in 分屏, the enhanced side keeps FG; the original side holds real source frames (no flash). Full-original compare still pauses FG. XeSS whole-buffer FG stays off during compare; application FG (DLSS/VFG/FSR) remains.
7. **Split FPS badges:** left/right live FPS overlays (原画 = source/input rate, 增强 = submit/FG rate), including picture fullscreen.
8. **Helper tools (optional):** `tools/gpu-bind/` — Windows helper sources to write `monitorGpu` / `computeOnMonitorGpu` for portable builds.

## Third-party / proprietary

See `THIRD_PARTY_NOTICES.md` and files under `licenses/`.

**Not included in this Git repository** (obtain separately under their own terms):

- NVIDIA DLSS / NGX SDK and Optical Flow SDK
- Proprietary / restricted GPU runtime DLLs and models shipped only in upstream portable packages
- Intel XeSS / AMD FidelityFX SDK trees (stage under `third_party_local/` locally; see `third_party_local/README.md`)

Chiaki-related Remote Play code remains subject to **AGPL-3.0** and the upstream OpenSSL exception where applicable (`licenses/remoteplay`).

## Disclaimer

Unofficial community modification. Not affiliated with or endorsed by the original Veyra authors, NVIDIA, AMD, Intel, or Sony.
Use at your own risk. Keep licence texts when redistributing source or binaries; for GPL binaries, provide corresponding complete source.
