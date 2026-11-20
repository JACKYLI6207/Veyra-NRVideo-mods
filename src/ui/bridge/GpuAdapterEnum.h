#pragma once
// GPU list for Settings → monitor / compute GPU.
// Dual identical cards: sort by PCI bus (GPU-SET style), stable PNP instance ids,
// and unique "N号显卡 | name" labels so the menu cannot check two rows at once.
#include <QString>
#include <climits>
#include <cstdint>
#include <set>
#include <vector>

namespace veyra::ui::gpu_enum {

struct Adapter {
    QString id;        // stable preference key (pnp:… or legacy vendor:device:subsys[#n])
    QString legacyId;  // pre-2.1.6-style id for preference migration
    QString name;      // UI label
    QString pnpId;     // raw PCI\VEN_… instance when known
    uint32_t luidHigh = 0;
    uint32_t luidLow = 0;
    int busNumber = INT_MAX;
    int slotIndex = 0; // 1-based discrete GPU slot; 0 = iGPU / unknown
};

// measuredEngineLuids: PDH "GPU Engine" LUIDs (empty = do not filter).
std::vector<Adapter> listAdapters(const std::set<uint64_t>& measuredEngineLuids);

const Adapter* findAdapter(const std::vector<Adapter>& adapters, const QString& wanted);

} // namespace veyra::ui::gpu_enum