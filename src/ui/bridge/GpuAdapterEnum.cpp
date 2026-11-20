#include "GpuAdapterEnum.h"

#include "veyra/Log.h"

#include <dxgi1_6.h>
#include <wrl/client.h>

#include <cfgmgr32.h>
#include <devguid.h>
#include <devpkey.h>
#include <setupapi.h>

#include <QRegularExpression>
#include <QStringList>

#include <algorithm>
#include <format>
#include <string>
#include <unordered_map>
#include <vector>

#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "setupapi.lib")

namespace veyra::ui::gpu_enum {
namespace {

using Microsoft::WRL::ComPtr;

// DEVPKEY_Device_GpuLuid — Windows 10+; defined locally so older SDKs still build.
static const DEVPROPKEY kDeviceGpuLuid = {
    {0x60b193cb, 0x5276, 0x4d0f, {0x96, 0xfc, 0xf1, 0x73, 0xab, 0xad, 0x3e, 0xc6}}, 9};

struct PnpInfo {
    QString instanceId;
    int busNumber = INT_MAX;
};

QString slotLabel(int oneBased)
{
    static const char* cn[] = {"", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十"};
    if (oneBased >= 1 && oneBased <= 10)
        return QString::fromUtf8(cn[oneBased]) + QStringLiteral("号显卡");
    if (oneBased > 0)
        return QString::number(oneBased) + QStringLiteral("号显卡");
    return QStringLiteral("显卡");
}

bool isIntegrated(uint32_t vendorId, const QString& name)
{
    const QString n = name.toLower();
    if (n.contains(QLatin1String("geforce")) || n.contains(QLatin1String("rtx")) || n.contains(QLatin1String("gtx"))
        || n.contains(QLatin1String("radeon rx")) || n.contains(QLatin1String("arc")))
        return false;
    if (vendorId == 0x8086)
        return true;
    if (vendorId == 0x1002 && !n.contains(QLatin1String("rx")))
        return true;
    return false;
}

int busFromLocation(const QString& location)
{
    if (location.isEmpty())
        return INT_MAX;
    // Win10+: "@…pci.sys,#65536;PCI bus %1…;(1,0,0)"
    static const QRegularExpression trailing(QStringLiteral(R"(\((\d+)\s*,\s*\d+\s*,\s*\d+\)\s*$)"));
    if (const auto m = trailing.match(location); m.hasMatch())
        return m.captured(1).toInt();
    // "PCI bus 1, device 0, function 0" / "PCI 匯流排 1，裝置 0，函數 0"
    static const QRegularExpression prose(QStringLiteral(R"((?:PCI\s*)?(?:bus|匯流排)\s*(\d+))"),
        QRegularExpression::CaseInsensitiveOption);
    if (const auto m = prose.match(location); m.hasMatch())
        return m.captured(1).toInt();
    return INT_MAX;
}

int busFromRegistry(const QString& instanceId)
{
    if (instanceId.isEmpty())
        return INT_MAX;
    const QString path = QStringLiteral("SYSTEM\\CurrentControlSet\\Enum\\") + instanceId;
    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, reinterpret_cast<LPCWSTR>(path.utf16()), 0, KEY_READ, &key) != ERROR_SUCCESS)
        return INT_MAX;
    wchar_t loc[512]{};
    DWORD type = 0, bytes = sizeof(loc);
    int bus = INT_MAX;
    if (RegQueryValueExW(key, L"LocationInformation", nullptr, &type, reinterpret_cast<LPBYTE>(loc), &bytes) == ERROR_SUCCESS
        && (type == REG_SZ || type == REG_EXPAND_SZ))
        bus = busFromLocation(QString::fromWCharArray(loc));
    if (bus == INT_MAX) {
        DWORD ui = 0;
        bytes = sizeof(ui);
        if (RegQueryValueExW(key, L"UINumber", nullptr, &type, reinterpret_cast<LPBYTE>(&ui), &bytes) == ERROR_SUCCESS
            && type == REG_DWORD)
            bus = int(ui);
    }
    RegCloseKey(key);
    return bus;
}

QString stableIdFromPnp(const QString& instanceId)
{
    // Preference-friendly form of the Windows instance path (unique per board).
    QString id = instanceId;
    id.replace(QLatin1Char('\\'), QLatin1Char('/'));
    return QStringLiteral("pnp:") + id;
}

QString legacyChipId(uint32_t vendorId, uint32_t deviceId, uint32_t subSysId)
{
    return QStringLiteral("%1:%2:%3")
        .arg(vendorId, 4, 16, QLatin1Char('0'))
        .arg(deviceId, 4, 16, QLatin1Char('0'))
        .arg(subSysId, 8, 16, QLatin1Char('0'));
}

std::unordered_map<uint64_t, PnpInfo> pnpByLuid()
{
    std::unordered_map<uint64_t, PnpInfo> out;
    HDEVINFO set = SetupDiGetClassDevsW(&GUID_DEVCLASS_DISPLAY, nullptr, nullptr, DIGCF_PRESENT);
    if (set == INVALID_HANDLE_VALUE)
        return out;
    SP_DEVINFO_DATA info{};
    info.cbSize = sizeof(info);
    for (DWORD index = 0; SetupDiEnumDeviceInfo(set, index, &info); ++index) {
        wchar_t instance[512]{};
        if (!SetupDiGetDeviceInstanceIdW(set, &info, instance, 512, nullptr))
            continue;
        DEVPROPTYPE propType = 0;
        uint64_t luid = 0;
        if (!SetupDiGetDevicePropertyW(set, &info, &kDeviceGpuLuid, &propType,
                reinterpret_cast<PBYTE>(&luid), sizeof(luid), nullptr, 0)
            || propType != DEVPROP_TYPE_UINT64 || luid == 0)
            continue;
        PnpInfo pnp;
        pnp.instanceId = QString::fromWCharArray(instance);
        pnp.busNumber = busFromRegistry(pnp.instanceId);
        out[luid] = std::move(pnp);
    }
    SetupDiDestroyDeviceInfoList(set);
    return out;
}

} // namespace

std::vector<Adapter> listAdapters(const std::set<uint64_t>& measuredEngineLuids)
{
    const auto pnpMap = pnpByLuid();
    std::vector<Adapter> adapters;

    ComPtr<IDXGIFactory6> factory;
    if (FAILED(CreateDXGIFactory2(0, IID_PPV_ARGS(&factory))))
        return adapters;

    ComPtr<IDXGIAdapter1> adapter;
    for (UINT i = 0; SUCCEEDED(factory->EnumAdapterByGpuPreference(
             i, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, IID_PPV_ARGS(adapter.ReleaseAndGetAddressOf())));
         ++i) {
        DXGI_ADAPTER_DESC1 d{};
        if (FAILED(adapter->GetDesc1(&d)) || (d.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) || d.VendorId == 0x1414)
            continue;
        const uint64_t luid = (uint64_t(uint32_t(d.AdapterLuid.HighPart)) << 32) | uint32_t(d.AdapterLuid.LowPart);
        if (!measuredEngineLuids.empty() && !measuredEngineLuids.count(luid)) {
            veyra::log::info("perf", std::format("gpu adapter skipped (no engine counters) luid={:08X}:{:08X} {}",
                uint32_t(luid >> 32), uint32_t(luid), QString::fromWCharArray(d.Description).toStdString()));
            continue;
        }

        Adapter a;
        a.luidHigh = uint32_t(d.AdapterLuid.HighPart);
        a.luidLow = d.AdapterLuid.LowPart;
        const QString desc = QString::fromWCharArray(d.Description).trimmed();
        a.legacyId = legacyChipId(d.VendorId, d.DeviceId, d.SubSysId);
        int same = 0;
        for (const auto& prev : adapters)
            if (prev.legacyId.section(QLatin1Char('#'), 0, 0) == a.legacyId)
                ++same;
        if (same)
            a.legacyId += QStringLiteral("#%1").arg(same);

        if (const auto it = pnpMap.find(luid); it != pnpMap.end()) {
            a.pnpId = it->second.instanceId;
            a.busNumber = it->second.busNumber;
            a.id = stableIdFromPnp(a.pnpId);
        } else {
            a.id = a.legacyId;
            a.busNumber = INT_MAX;
        }
        a.name = desc; // slot prefix after PCI sort
        if (isIntegrated(d.VendorId, desc))
            a.slotIndex = -1; // marker: iGPU, unknown discrete slot
        adapters.push_back(std::move(a));
    }

    // Sort by PCI bus ascending: on this dual‑GPU layout the smaller bus is the
    // upper (first) PCIe slot → 一号, then 二号 below. Do not invert by bus size.
    std::stable_sort(adapters.begin(), adapters.end(), [](const Adapter& x, const Adapter& y) {
        if (x.busNumber != y.busNumber)
            return x.busNumber < y.busNumber;
        const int cmp = QString::compare(x.pnpId, y.pnpId, Qt::CaseInsensitive);
        if (cmp != 0)
            return cmp < 0;
        if (x.luidHigh != y.luidHigh)
            return x.luidHigh < y.luidHigh;
        return x.luidLow < y.luidLow;
    });

    int slot = 0;
    for (auto& a : adapters) {
        if (a.slotIndex < 0)
            a.slotIndex = 0;
        else
            a.slotIndex = ++slot;
    }
    // List order: 一号 first, iGPU last.
    std::stable_sort(adapters.begin(), adapters.end(), [](const Adapter& x, const Adapter& y) {
        const bool ix = x.slotIndex == 0, iy = y.slotIndex == 0;
        if (ix != iy)
            return !ix; // discrete before iGPU
        if (x.slotIndex != y.slotIndex)
            return x.slotIndex < y.slotIndex;
        return x.busNumber < y.busNumber;
    });
    for (auto& a : adapters) {
        if (a.slotIndex == 0) {
            if (!a.name.contains(QStringLiteral("核显")) && !a.name.contains(QStringLiteral("iGPU"), Qt::CaseInsensitive))
                a.name += QStringLiteral(" [核显]");
        } else {
            a.name = slotLabel(a.slotIndex) + QStringLiteral(" | ") + a.name;
        }
        veyra::log::info("perf", std::format(
            "gpu adapter slot={} id={} legacy={} bus={} luid={:08X}:{:08X} pnp={} name={}",
            a.slotIndex, a.id.toStdString(), a.legacyId.toStdString(),
            a.busNumber == INT_MAX ? -1 : a.busNumber,
            a.luidHigh, a.luidLow, a.pnpId.toStdString(), a.name.toStdString()));
    }
    return adapters;
}

const Adapter* findAdapter(const std::vector<Adapter>& adapters, const QString& wanted)
{
    if (wanted.isEmpty())
        return nullptr;
    for (const auto& a : adapters) {
        if (a.id == wanted || a.legacyId == wanted)
            return &a;
        if (!a.pnpId.isEmpty() && a.pnpId.compare(wanted, Qt::CaseInsensitive) == 0)
            return &a;
        if (wanted.startsWith(QLatin1String("pnp:"), Qt::CaseInsensitive)) {
            QString rest = wanted.mid(4);
            rest.replace(QLatin1Char('/'), QLatin1Char('\\'));
            if (a.pnpId.compare(rest, Qt::CaseInsensitive) == 0)
                return &a;
        }
    }
    return nullptr;
}

} // namespace veyra::ui::gpu_enum